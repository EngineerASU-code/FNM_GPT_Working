using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Configurator.Core.Architecture;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Configurator
{
    public partial class MainWindow
    {
        private void ClearActiveTable()
        {
            _activeVm = null;
            _activeDbName = "";

            if (_rightPanel.DataTableGrid != null) _rightPanel.DataTableGrid.ItemsSource = null;
            if (_rightPanel.TableTitle != null) _rightPanel.TableTitle.Text = "Выберите таблицу слева";

            _fieldsBuilder?.Clear();
            UpdateFilterButtonState();

            if (_rightPanel.InputPanel != null)
                _rightPanel.InputPanel.Visibility = _fieldsPanelExpanded ? Visibility.Visible : Visibility.Collapsed;

            SafeSetButtonEnabled(_rightPanel.BtnAdd, false);
            SafeSetButtonEnabled(_rightPanel.BtnUpdate, false);
            SafeSetButtonEnabled(_rightPanel.BtnDuplicate, false);
            SafeSetButtonEnabled(_rightPanel.BtnDelete, false);

            SetPaginationButtons(false, false, false, false);

            if (_rightPanel.PageInfo != null) _rightPanel.PageInfo.Text = "";
        }

        private async Task LoadDataAsync(int preferredRowIndex = -1, Dictionary<string, object> preferredIdentity = null)
        {
            if (_activeVm == null) return;

            var vm = _activeVm;
            var tableTitle = $"{_activeDbName} → {vm.CurrentTable}";
            int generation = ++_loadGeneration;

            _dataLoadCts?.Cancel();
            _dataLoadCts = new CancellationTokenSource();
            var ct = _dataLoadCts.Token;

            if (_rightPanel.DataTableGrid != null)
                _rightPanel.DataTableGrid.ItemsSource = null;

            _fieldsBuilder.Clear();
            vm.ClearCurrentRow();
            _rightPanel.FieldsEmptyHint.Visibility = Visibility.Visible;
            _rightPanel.TableTitle.Text = $"{tableTitle} — загрузка...";
            SetPaginationButtons(false, false, false, false);

            try
            {
                bool filterMissingColumn = vm.Filter?.IsActive == true &&
                    (vm.Columns == null || !vm.Columns.Any(c => c.Name.Equals(vm.Filter.Column, StringComparison.OrdinalIgnoreCase)));

                if (filterMissingColumn)
                    ShowFilterNotAppliedNotice(vm.Filter.Column);

                // Первый показ: страница и COUNT запускаются параллельно.
                // Если сохранённый фильтр не относится к текущей таблице, он
                // остаётся настроенным, но к данным временно не применяется.
                var pageTask = filterMissingColumn ? vm.LoadPageIgnoringFilterAsync(ct) : vm.LoadPageAsync(ct);
                var countTask = filterMissingColumn ? vm.GetTotalCountIgnoringFilterAsync(ct) : vm.GetTotalCountAsync(ct);
                var pageData = await pageTask;
                if (_loadGeneration != generation || ct.IsCancellationRequested) return;

                _rightPanel.TableTitle.Text = tableTitle;
                UpdateFilterButtonState();
                BindPageData(pageData, preferredRowIndex, preferredIdentity, resetScroll: preferredRowIndex < 0);

                SafeSetButtonEnabled(_rightPanel.BtnAdd, true);

                if (vm.Columns != null && vm.Columns.Count > 0)
                    _fieldsBuilder.Generate(vm.Columns, vm.KeyColumn);

                // Не заставляем UI ждать COUNT: к этому моменту он обычно уже готов.
                await countTask;
                if (_loadGeneration != generation || ct.IsCancellationRequested) return;

                if (vm.CurrentPage > vm.TotalPages)
                {
                    vm.CurrentPage = vm.TotalPages;
                    await ReloadCurrentPageFastAsync(Math.Max(0, preferredRowIndex), preferredIdentity);
                    return;
                }

                UpdatePagination();
            }
            catch (FilterColumnMissingException ex)
            {
                ShowFilterNotAppliedNotice(ex.Message);
                await ReloadWithoutCurrentFilterAsync(vm, ct);
            }
            catch (OperationCanceledException)
            {
                // Пользователь сменил таблицу/страницу — старый запрос больше не нужен.
            }
            catch (Exception ex) when (vm.Filter?.IsActive == true && ex.Message.Contains("Invalid column name", StringComparison.OrdinalIgnoreCase))
            {
                ShowFilterNotAppliedNotice(vm.Filter.Column);
                await ReloadWithoutCurrentFilterAsync(vm, ct);
            }
            catch (Exception ex)
            {
                if (_loadGeneration != generation || ct.IsCancellationRequested) return;
                _rightPanel.TableTitle.Text = vm.CurrentTable;
                MessageBox.Show("Ошибка загрузки данных: " + ex.Message,
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ReloadCurrentPageFastAsync(
            int preferredRowIndex = -1,
            Dictionary<string, object> preferredIdentity = null)
        {
            if (_activeVm == null) return;

            var vm = _activeVm;
            int generation = ++_loadGeneration;

            _dataLoadCts?.Cancel();
            _dataLoadCts = new CancellationTokenSource();
            var ct = _dataLoadCts.Token;

            try
            {
                // Важное отличие от старой логики: после CRUD НЕ выполняем COUNT(*).
                // Загружается только одна текущая страница.
                var pageData = await vm.ReloadCurrentPageAsync(ct);
                if (_loadGeneration != generation || ct.IsCancellationRequested) return;

                BindPageData(pageData, preferredRowIndex, preferredIdentity, resetScroll: false);
                UpdatePagination();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (_loadGeneration != generation || ct.IsCancellationRequested) return;
                MessageBox.Show("Ошибка обновления таблицы: " + ex.Message,
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BindPageData(
            DataTable pageData,
            int preferredRowIndex,
            Dictionary<string, object> preferredIdentity,
            bool resetScroll)
        {
            var grid = _rightPanel.DataTableGrid;
            if (grid == null) return;

            grid.ItemsSource = pageData?.DefaultView;

            if (resetScroll)
            {
                Dispatcher.BeginInvoke(new Action(ScrollDataGridToTop),
                    DispatcherPriority.Loaded);
            }

            if (pageData == null || pageData.Rows.Count == 0)
            {
                grid.SelectedIndex = -1;
                return;
            }

            int index = preferredRowIndex >= 0
                ? Math.Min(preferredRowIndex, pageData.Rows.Count - 1)
                : -1;

            if (preferredIdentity != null && preferredIdentity.Count > 0)
            {
                for (int i = 0; i < pageData.Rows.Count; i++)
                {
                    bool match = preferredIdentity.All(pair =>
                        pageData.Columns.Contains(pair.Key) &&
                        ValuesEqual(pageData.Rows[i][pair.Key], pair.Value));

                    if (match)
                    {
                        index = i;
                        break;
                    }
                }
            }

            if (index >= 0)
            {
                grid.SelectedIndex = index;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (grid.SelectedItem != null)
                        grid.ScrollIntoView(grid.SelectedItem);
                    QueueSelectionStateUpdate();
                }), DispatcherPriority.Loaded);
            }
            else
            {
                grid.SelectedIndex = -1;
            }
        }

        private static bool ValuesEqual(object a, object b)
        {
            if (a == null || a == DBNull.Value) return b == null || b == DBNull.Value;
            if (b == null || b == DBNull.Value) return false;
            return string.Equals(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        private void SafeSetButtonEnabled(Button btn, bool enabled)
        {
            if (btn != null) btn.IsEnabled = enabled;
        }

        private void ScrollDataGridToTop()
        {
            if (_rightPanel.DataTableGrid == null || _rightPanel.DataTableGrid.Items.Count == 0) return;

            _rightPanel.DataTableGrid.SelectedIndex = -1;

            if (_rightPanel.DataTableGrid.Items.Count > 0)
            {
                _rightPanel.DataTableGrid.ScrollIntoView(_rightPanel.DataTableGrid.Items[0]);
            }

            var scrollViewer = VisualTreeHelper.FindChild<System.Windows.Controls.ScrollViewer>(_rightPanel.DataTableGrid);
            scrollViewer?.ScrollToTop();
        }

        private void DataTableGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            QueueSelectionStateUpdate();
        }

        private void DataTableGrid_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            QueueSelectionStateUpdate();
        }

        private void QueueSelectionStateUpdate()
        {
            Dispatcher.BeginInvoke(new Action(UpdateSelectionState), DispatcherPriority.DataBind);
        }

        private void UpdateSelectionState()
        {
            if (_activeVm == null || _rightPanel.DataTableGrid == null)
                return;

            int selectedCount = _rightPanel.DataTableGrid.SelectedItems.Count;

            if (selectedCount == 1 && _rightPanel.DataTableGrid.SelectedItem is DataRowView row)
            {
                _activeVm.SetCurrentRow(row);
                _fieldsBuilder.Fill(_activeVm.CurrentRowValues);
                _rightPanel.FieldsEmptyHint.Text = "";
                _rightPanel.FieldsEmptyHint.Visibility = Visibility.Collapsed;

                // Активируем CRUD только если строка действительно содержит все
                // значения идентификатора (PK либо уникального индекса).
                bool rowHasIdentity = _activeVm.HasValidKey
                    && (_activeVm.UsesValueIdentity
                        ? _activeVm.CurrentIdentityValues.Count > 0
                        : _activeVm.KeyColumns.All(key => _activeVm.CurrentKeyValues.ContainsKey(key)));

                SafeSetButtonEnabled(_rightPanel.BtnUpdate, rowHasIdentity);
                SafeSetButtonEnabled(_rightPanel.BtnDuplicate, rowHasIdentity);
                SafeSetButtonEnabled(_rightPanel.BtnDelete, rowHasIdentity);
                if (!rowHasIdentity)
                {
                    _rightPanel.BtnUpdate.ToolTip = "Не удалось определить строку";
                    _rightPanel.BtnDuplicate.ToolTip = "Не удалось определить строку";
                    _rightPanel.BtnDelete.ToolTip = "Не удалось определить строку";
                }
                else
                {
                    _rightPanel.BtnUpdate.ToolTip = "Изменить выбранную строку";
                    _rightPanel.BtnDuplicate.ToolTip = "Создать копию выбранной строки";
                    _rightPanel.BtnDelete.ToolTip = "Удалить выбранные строки";
                }
                return;
            }

            _fieldsBuilder.ClearValues();
            _activeVm.ClearCurrentRow();
            _rightPanel.FieldsEmptyHint.Text = selectedCount > 1 ? $"Выбрано строк: {selectedCount}" : "Выберите одну строку в таблице";
            _rightPanel.FieldsEmptyHint.Visibility = Visibility.Visible;
            SafeSetButtonEnabled(_rightPanel.BtnUpdate, false);
            SafeSetButtonEnabled(_rightPanel.BtnDuplicate, false);
            SafeSetButtonEnabled(_rightPanel.BtnDelete, selectedCount > 0 && _activeVm.HasValidKey);
        }

        private async Task<Dictionary<string, List<LookupOption>>> LoadLookupOptionsForTableAsync(string tableName)
        {
            var result = new Dictionary<string, List<LookupOption>>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(_activeDbName)) return result;
            var definition = SystemLookupCatalog.All.FirstOrDefault(x => x.TargetTable.Equals(tableName ?? "", StringComparison.OrdinalIgnoreCase));
            if (definition == null) return result;
            try
            {
                var db = new DatabaseService(_connection.ToConnectionString(_activeDbName));
                if (await db.TableExistsAsync(definition.LookupTable))
                    result[definition.TargetField] = await db.GetLookupOptionsAsync(definition.LookupTable);
            }
            catch { }
            return result;
        }

        private async void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            if (_activeVm == null || string.IsNullOrEmpty(_activeVm.CurrentTable)
                || _activeVm.Columns == null || _activeVm.Columns.Count == 0)
                return;

            int nextRecord = 0;
            bool hasPlc = _activeVm.Columns.Any(c => c.Name.Equals("PLC", StringComparison.OrdinalIgnoreCase));
            bool hasRecord = _activeVm.Columns.Any(c => c.Name.Equals("Record", StringComparison.OrdinalIgnoreCase));
            int? forcedClassNumber = ResolveSystemClassNumber(_activeVm.CurrentTable);
            string classNumberColumn = forcedClassNumber.HasValue ? ResolveClassNumberColumn(_activeVm.Columns) : null;

            // Для PLC + Record начальное значение рассчитывается после выбора PLC в окне.
            // Для таблиц без PLC сохраняем прежнюю глобальную автогенерацию Record.
            if (!hasPlc && hasRecord && !_activeVm.Columns.First(c => c.Name.Equals("Record", StringComparison.OrdinalIgnoreCase)).IsIdentity)
            {
                try { nextRecord = await _activeVm.GetNextRecordValueAsync(); }
                catch (Exception ex) { MessageBox.Show("Не удалось получить значение Record: " + ex.Message); return; }
            }

            var addLookupOptions = await LoadLookupOptionsForTableAsync(_activeVm.CurrentTable);
            var addWindow = new AddRowWindow(
                _activeVm.CurrentTable,
                _activeVm.Columns,
                nextRecord,
                hasRecord ? _activeVm.Columns.First(c => c.Name.Equals("Record", StringComparison.OrdinalIgnoreCase)).Name : "",
                _activeDbName.Length > 0 ? _connection.ToConnectionString(_activeDbName) : null,
                hasPlc ? _activeVm.Columns.First(c => c.Name.Equals("PLC", StringComparison.OrdinalIgnoreCase)).Name : null,
                forcedClassNumber,
                classNumberColumn,
                addLookupOptions)
            { Owner = this };

            addWindow.ShowDialog();

            if (!addWindow.Confirmed || addWindow.Values == null || addWindow.Values.Count == 0)
                return;

            try
            {
                await _activeVm.InsertAsync(addWindow.Values);
                var addTable = _activeVm.CurrentTable;
                var addDbName = _activeDbName;
                var addValues = new Dictionary<string, object>(addWindow.Values);
                var addIdentity = _activeVm.BuildIdentityValues(addValues);
                _activeVm.NotifyInserted(1);
                if (_activeVm.Filter?.IsActive == true)
                    await _activeVm.RefreshTotalCountAsync();

                // Добавленная строка логически находится в конце сортировки.
                // Переходим на последнюю страницу без повторного COUNT(*).
                _activeVm.CurrentPage = _activeVm.TotalPages;
                var preferred = new Dictionary<string, object>(addWindow.Values);

                LogAction($"Добавлена строка · {addTable}", async () =>
                {
                    var undoDb = new DatabaseService(_connection.ToConnectionString(addDbName));
                    await undoDb.DeleteRowsByValuesAsync(addTable, new List<Dictionary<string, object>> { addIdentity });
                });
                await ReloadCurrentPageFastAsync(-1, preferred);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка добавления: " + ex.Message);
            }
        }

        private async void BtnUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_activeVm == null || !_activeVm.HasValidKey) return;
            if (_rightPanel.DataTableGrid.SelectedItem is not DataRowView) return;

            int selectedIndex = Math.Max(0, _rightPanel.DataTableGrid.SelectedIndex);
            var oldIdentity = _activeVm.UsesValueIdentity
                ? new Dictionary<string, object>(_activeVm.CurrentIdentityValues)
                : new Dictionary<string, object>(_activeVm.CurrentKeyValues);
            var oldValues = new Dictionary<string, object>(_activeVm.CurrentRawRowValues);
            var updateTable = _activeVm.CurrentTable;
            var updateDbName = _activeDbName;

            int? editClassNumber = ResolveSystemClassNumber(_activeVm.CurrentTable);
            string editClassNumberColumn = editClassNumber.HasValue ? ResolveClassNumberColumn(_activeVm.Columns) : null;
            var editLookupOptions = await LoadLookupOptionsForTableAsync(_activeVm.CurrentTable);
            var editWindow = new EditRowWindow(
                _activeVm.CurrentTable,
                _activeVm.Columns,
                new Dictionary<string, string>(_activeVm.CurrentRowValues),
                _activeVm.KeyColumn,
                editClassNumberColumn,
                editClassNumber,
                editLookupOptions)
            { Owner = this };

            editWindow.ShowDialog();

            if (!editWindow.Confirmed || editWindow.Values == null || editWindow.Values.Count == 0)
                return;

            try
            {
                Dictionary<string, object> preferredIdentity;

                if (_activeVm.UsesValueIdentity)
                {
                    preferredIdentity = new Dictionary<string, object>(oldIdentity);
                    foreach (var pair in editWindow.Values)
                        if (preferredIdentity.ContainsKey(pair.Key))
                            preferredIdentity[pair.Key] = pair.Value;
                }
                else
                {
                    preferredIdentity = oldIdentity;
                }

                await _activeVm.UpdateAsync(editWindow.Values);
                if (_activeVm.Filter?.IsActive == true)
                    await _activeVm.RefreshTotalCountAsync();

                var afterValues = new Dictionary<string, object>(oldValues);
                foreach (var pair in editWindow.Values) afterValues[pair.Key] = pair.Value;
                var afterIdentity = _activeVm.BuildIdentityValues(afterValues);
                LogAction($"Изменена строка · {updateTable}", async () =>
                {
                    var undoDb = new DatabaseService(_connection.ToConnectionString(updateDbName));
                    await undoDb.UpdateRowByValuesAsync(updateTable, afterIdentity, oldValues);
                });

                // Быстрое обновление: только текущая страница, без COUNT(*).
                await ReloadCurrentPageFastAsync(selectedIndex, preferredIdentity);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка изменения: " + ex.Message);
            }
        }

        private async void BtnDuplicate_Click(object sender, RoutedEventArgs e)
        {
            if (_activeVm == null || !_activeVm.HasValidKey) return;

            try
            {
                var values = await _activeVm.DuplicateCurrentRowAsync();
                var duplicateTable = _activeVm.CurrentTable;
                var duplicateDbName = _activeDbName;
                if (values.Count == 0)
                {
                    MessageBox.Show("Нет данных для дублирования.");
                    return;
                }

                _activeVm.NotifyInserted(1);
                if (_activeVm.Filter?.IsActive == true)
                    await _activeVm.RefreshTotalCountAsync();
                _activeVm.CurrentPage = _activeVm.TotalPages;

                string newRecordMsg = "";
                if (values.TryGetValue(_activeVm.RecordColumn, out var recordValue))
                    newRecordMsg = $" Новый {_activeVm.RecordColumn} = {recordValue}";

                var duplicateIdentity = _activeVm.BuildIdentityValues(values);
                LogAction($"Дублирована строка · {duplicateTable}{newRecordMsg}", async () =>
                {
                    var undoDb = new DatabaseService(_connection.ToConnectionString(duplicateDbName));
                    await undoDb.DeleteRowsByValuesAsync(duplicateTable, new List<Dictionary<string, object>> { duplicateIdentity });
                });

                // Ищем копию по тем значениям, которые реально были вставлены.
                // Это работает и для PLC + Record, и для keyless таблиц.
                await ReloadCurrentPageFastAsync(-1, values);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка дублирования: " + ex.Message);
            }
        }


        private sealed class FilterColumnMissingException : Exception
        {
            public FilterColumnMissingException(string column) : base(column) { }
        }

    }
}

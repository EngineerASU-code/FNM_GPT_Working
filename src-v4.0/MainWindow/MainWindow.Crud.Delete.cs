using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Configurator;

public partial class MainWindow
{
        private async void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_activeVm == null || !_activeVm.HasValidKey) return;

            int selectedCount = _rightPanel.DataTableGrid.SelectedItems.Count;
            if (selectedCount == 0) return;

            int selectedIndex = Math.Max(0, _rightPanel.DataTableGrid.SelectedIndex);

            var keyValuesList = new List<Dictionary<string, object>>();
            var deletedSnapshots = new List<(Dictionary<string, object> Values, Dictionary<string, object> Identity)>();
            string deleteTable = _activeVm.CurrentTable;
            string deleteDbName = _activeDbName;
            foreach (var item in _rightPanel.DataTableGrid.SelectedItems)
            {
                if (item is DataRowView row)
                {
                    var keyValues = _activeVm.BuildIdentityValues(row);
                    if (keyValues.Count > 0)
                    {
                        keyValuesList.Add(keyValues);
                        deletedSnapshots.Add((row.DataView.Table.Columns.Cast<DataColumn>().ToDictionary(c => c.ColumnName, c => row[c.ColumnName]), new Dictionary<string, object>(keyValues)));
                    }
                }
            }

            if (keyValuesList.Count == 0)
            {
                MessageBox.Show("Не удалось получить значения ключа.");
                return;
            }

            string confirmMsg = keyValuesList.Count == 1
                ? "Удалить выбранную строку?"
                : $"Удалить {keyValuesList.Count} строк?";

            if (MessageBox.Show(confirmMsg, "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            try
            {
                int deleted = await _activeVm.DeleteAsync(keyValuesList);

                if (deleted == 0)
                {
                    MessageBox.Show(
                        "Строка не найдена. Возможно, её уже изменил или удалил другой пользователь.",
                        "Удаление не выполнено",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _activeVm.NotifyDeleted(deleted);
                _fieldsBuilder.Clear();
                _activeVm.ClearCurrentRow();

                SafeSetButtonEnabled(_rightPanel.BtnUpdate, false);
                SafeSetButtonEnabled(_rightPanel.BtnDuplicate, false);
                SafeSetButtonEnabled(_rightPanel.BtnDelete, false);

                LogAction($"Удалено строк: {deleted} · {deleteTable}", async () =>
                {
                    var undoDb = new DatabaseService(_connection.ToConnectionString(deleteDbName));
                    var meta = await undoDb.GetTableColumnsAsync(deleteTable);
                    foreach (var item in deletedSnapshots)
                    {
                        var values = item.Values.Where(p => { var m = meta.FirstOrDefault(x => x.Name.Equals(p.Key, StringComparison.OrdinalIgnoreCase)); return m == null || !m.IsReadOnly; })
                                              .ToDictionary(x => x.Key, x => x.Value);
                        if (!await undoDb.RowExistsByValuesAsync(deleteTable, item.Identity))
                            await undoDb.InsertRowAsync(deleteTable, values);
                    }
                });

                // Если удалили последнюю строку последней страницы, откатываемся
                // только на одну страницу назад. COUNT(*) здесь не нужен.
                if (_activeVm.CurrentPage > _activeVm.TotalPages)
                    _activeVm.CurrentPage = _activeVm.TotalPages;

                int preferredIndex = Math.Min(
                    selectedIndex,
                    Math.Max(0, _activeVm.PageSize - 1));

                await ReloadCurrentPageFastAsync(preferredIndex, null);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка удаления: " + ex.Message);
            }
        }
}

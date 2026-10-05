using Configurator.Themes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace Configurator
{
    public partial class MainWindow : Window
    {
        private ConnectionSettings _connection = new ConnectionSettings();
        private Dictionary<string, TableViewModel> _vms = new Dictionary<string, TableViewModel>();
        private TableViewModel _activeVm;
        private string _activeDbName = "";
        private string _selectedDbName = "";
        private DisplayFieldsBuilder _fieldsBuilder;
        private HashSet<string> _failedDatabases = new HashSet<string>();
        private HashSet<string> _loadedTreeNodes = new HashSet<string>();
        private int _loadGeneration = 0;
        private CancellationTokenSource _reconnectCts;
        private CancellationTokenSource _dataLoadCts;
        private bool _fieldsPanelExpanded = false;
        private readonly ActionLogService _actionLog = new ActionLogService();
        private TemplateArchitectureView _templateEditor;
        private ProjectModeView _projectEditor;

        private readonly LeftPanel _leftPanel;
        private readonly RightPanel _rightPanel;

        private string VmCacheKey(string dbName) => $"{_connection.Server}|{dbName}";

        public MainWindow()
        {
            InitializeComponent();
            WindowState = WindowState.Maximized;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _leftPanel = LeftPanelControl;
            _rightPanel = RightPanelControl;
            _templateEditor = TemplateArchitectureControl;
            _projectEditor = ProjectModeControl;
            _templateEditor.ProjectSelectionRequested += ProjectSelectionRequested;
            _projectEditor.ProjectSelectionRequested += ProjectSelectionRequested;

            ThemeManager.InitializeDefault();
            _fieldsBuilder = new DisplayFieldsBuilder(_rightPanel.FieldsContainer);
            _actionLog.Changed += ActionLog_Changed;

            WireUpEvents();
            AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(GlobalButtonClicked), true);
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            Loaded += async (s, e) => await InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            _rightPanel.TableTitle.Text = "Загрузка...";
            try
            {
                _connection.LoadFromFile();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Внимание: {ex.Message}\n\nНастройки будут созданы заново.",
                    "Загрузка настроек", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            ShowKnownDatabasesLoading();
            await TryReconnectDatabasesAsync();
            RebuildTree();
            UpdateAddDbButtonState();
            _rightPanel.TableTitle.Text = "Выберите таблицу слева";
            if (StatusText != null) StatusText.Text = "Готово";
            RefreshActionLogUi();
        }

        private async Task TryReconnectDatabasesAsync()
        {
            _connection.ConnectedDatabases.Clear();
            _failedDatabases.Clear();
            _reconnectCts?.Cancel();
            _dataLoadCts?.Cancel();
            _reconnectCts = new CancellationTokenSource();
            var ct = _reconnectCts.Token;
            var databases = _connection.KnownDatabases
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var checks = databases.Select(async dbName =>
            {
                bool connected = await _connection.TestConnectionAsync(dbName);
                return (dbName, connected);
            }).ToArray();
            var results = await Task.WhenAll(checks);
            if (ct.IsCancellationRequested) return;
            foreach (var result in results)
            {
                _connection.ConnectedDatabases.Add(result.dbName);
                if (!result.connected) _failedDatabases.Add(result.dbName);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _reconnectCts?.Cancel();
            _dataLoadCts?.Cancel();
            _reconnectCts?.Dispose();
            _dataLoadCts?.Dispose();
            try { _connection.SaveToFile(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[OnClosed] Save failed: {ex.Message}"); }
            global::System.Windows.Application.Current.Shutdown();
            base.OnClosed(e);
        }

        private void WireUpEvents()
        {
            _leftPanel.AddDatabaseClicked += () => BtnOpenConnection_Click(null, null);
            _leftPanel.RefreshDatabaseRequested += () => _ = RefreshDatabasesAndSchemaAsync();
            _rightPanel.BtnAdd.Click += BtnAdd_Click;
            _rightPanel.BtnUpdate.Click += BtnUpdate_Click;
            _rightPanel.BtnDuplicate.Click += BtnDuplicate_Click;
            _rightPanel.BtnDelete.Click += BtnDelete_Click;
            _rightPanel.BtnToggleFields.Click += BtnToggleFields_Click;
            _rightPanel.BtnFilter.Click += BtnFilter_Click;
            _rightPanel.UndoButton.Click += BtnUndo_Click;
            _rightPanel.BtnFirst.Click += BtnFirst_Click;
            _rightPanel.BtnPrev.Click += BtnPrev_Click;
            _rightPanel.BtnNext.Click += BtnNext_Click;
            _rightPanel.BtnLast.Click += BtnLast_Click;
            _rightPanel.DataTableGrid.SelectionChanged += DataTableGrid_SelectionChanged;
            _rightPanel.DataTableGrid.MouseLeftButtonUp += DataTableGrid_MouseLeftButtonUp;
            _rightPanel.DataTableGrid.CurrentCellChanged += (s, e) => QueueSelectionStateUpdate();
            SetFieldsPanelExpanded(false, false);
            if (_leftPanel.ServerTree != null)
                _leftPanel.ServerTree.SelectedItemChanged += (s, e) => UpdateRemoveDbMenuState();
            ActivateMode(1);
        }

        private void UpdateRemoveDbMenuState()
        {
            var menuItem = this.FindName("MenuRemoveDb") as MenuItem;
            if (menuItem != null) menuItem.IsEnabled = _leftPanel.ServerTree?.SelectedItem != null;
        }

        private void UpdateAddDbButtonState()
        {
            _leftPanel.SetAddDbButtonEnabled(true);
            if (_connection.ConnectedDatabases.Count > 0) _leftPanel.HidePlaceholder();
            else _leftPanel.ShowPlaceholder();
        }

        private void MenuViewTheme_Click(object sender, RoutedEventArgs e)
        {
            var picker = new ThemePickerWindow { Owner = this };
            picker.ShowDialog();
        }

        private void BtnOpenConnection_Click(object sender, RoutedEventArgs e)
        {
            var window = new ConnectionWindow { Owner = this };
            window.OnDatabaseAdded = (dbName) =>
            {
                string newServer = window.Settings.Server;
                if (!string.IsNullOrEmpty(_connection.Server) && !newServer.Equals(_connection.Server, StringComparison.OrdinalIgnoreCase))
                {
                    _vms.Clear(); _loadedTreeNodes.Clear(); _connection.ConnectedDatabases.Clear(); _connection.KnownDatabases.Clear(); _failedDatabases.Clear(); ClearActiveTable();
                }
                _connection.Server = newServer;
                _connection.User = window.Settings.User;
                _connection.Password = window.Settings.Password;
                if (_connection.ConnectedDatabases.Any(db => db.Equals(dbName, StringComparison.OrdinalIgnoreCase))) return false;
                _connection.ConnectedDatabases.Add(dbName);
                _selectedDbName = dbName;
                if (!_connection.KnownDatabases.Any(db => db.Equals(dbName, StringComparison.OrdinalIgnoreCase))) _connection.KnownDatabases.Add(dbName);
                _failedDatabases.Remove(dbName);
                RebuildTree();
                UpdateAddDbButtonState();
                _templateEditor?.Configure(_connection, GetActiveDatabases(), dbName);
                _projectEditor?.Configure(_connection, GetActiveDatabases(), dbName);
                try { _connection.SaveToFile(); }
                catch (Exception ex) { MessageBox.Show($"Не удалось сохранить настройки: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning); }
                return true;
            };
            window.ShowDialog();
        }

        private void BtnRemoveDb_Click(object sender, RoutedEventArgs e) { RemoveSelectedDatabase(); UpdateAddDbButtonState(); }
        private void BtnToggleFields_Click(object sender, RoutedEventArgs e) { SetFieldsPanelExpanded(!_fieldsPanelExpanded, true); }

        private void BtnFilter_Click(object sender, RoutedEventArgs e)
        {
            if (_activeVm == null || _activeVm.Columns == null || _activeVm.Columns.Count == 0) return;
            var window = new TableFilterWindow(_activeVm.Columns, _activeVm.Filter) { Owner = this };
            if (window.ShowDialog() != true) return;
            _activeVm.SetFilter(window.Result);
            UpdateFilterButtonState();
            _ = LoadDataAsync();
        }

        private void UpdateFilterButtonState()
        {
            if (_rightPanel?.BtnFilter == null) return;
            bool active = _activeVm?.Filter?.IsActive == true;
            _rightPanel.BtnFilter.Content = active ? "⌕  Фильтр •" : "⌕  Фильтр";
            _rightPanel.BtnFilter.ToolTip = active ? $"Фильтр: {_activeVm.Filter.Column} {_activeVm.Filter.Operator}" : "Фильтр текущей таблицы";
        }

        private void SetFieldsPanelExpanded(bool expanded, bool syncMenu)
        {
            _fieldsPanelExpanded = expanded;
            if (_rightPanel.InputPanel != null)
            {
                _rightPanel.InputPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
                _rightPanel.InputPanel.UpdateLayout();
                Dispatcher.BeginInvoke(new Action(() => _rightPanel.InputPanel.UpdateLayout()), DispatcherPriority.Loaded);
            }
            if (_rightPanel.BtnToggleFields != null)
            {
                _rightPanel.BtnToggleFields.Content = expanded ? "Поля записи  ▲" : "Поля записи  ▼";
                _rightPanel.BtnToggleFields.ToolTip = expanded ? "Свернуть поля записи" : "Развернуть поля записи";
            }
        }

        private List<string> GetActiveDatabases() => _connection.ConnectedDatabases.Where(db => !_failedDatabases.Contains(db)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        private async Task RefreshDatabasesAndSchemaAsync()
        {
            try
            {
                await TryReconnectDatabasesAsync();
                _vms.Clear(); _loadedTreeNodes.Clear(); RebuildTree(); UpdateAddDbButtonState();
                if (!string.IsNullOrWhiteSpace(_selectedDbName) && !_failedDatabases.Contains(_selectedDbName))
                {
                    _templateEditor?.Configure(_connection, GetActiveDatabases(), _selectedDbName);
                    _projectEditor?.Configure(_connection, GetActiveDatabases(), _selectedDbName);
                }
                else
                {
                    string active = GetActiveDatabases().FirstOrDefault() ?? "";
                    _selectedDbName = active;
                    _templateEditor?.Configure(_connection, GetActiveDatabases(), active);
                    _projectEditor?.Configure(_connection, GetActiveDatabases(), active);
                }
                ClearActiveTable();
                LogAction("Состояние подключений и структура БД обновлены");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось обновить структуру БД: {ex.Message}", "Обновление БД", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ActivateMode(int mode)
        {
            EditorModeView.Visibility = mode == 1 ? Visibility.Visible : Visibility.Collapsed;
            Mode2View.Visibility = mode == 2 ? Visibility.Visible : Visibility.Collapsed;
            Mode3View.Visibility = mode == 3 ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = mode switch { 1 => "БД проекта", 2 => "Шаблоны проекта", 3 => "Объекты проекта", _ => "БД проекта" };
            UpdateModeNavigation(mode);
            if (FindName("MenuDatabase") is MenuItem databaseMenu) databaseMenu.IsEnabled = mode == 1;
            if (mode == 2) _templateEditor?.Configure(_connection, GetActiveDatabases(), _selectedDbName);
            if (mode == 3) _projectEditor?.Configure(_connection, GetActiveDatabases(), _selectedDbName);
        }

        private void UpdateModeNavigation(int mode)
        {
            NavDatabase.Style = (Style)FindResource(mode == 1 ? "BtnPrimary" : "BtnSecondary");
            NavTemplates.Style = (Style)FindResource(mode == 2 ? "BtnPrimary" : "BtnSecondary");
            NavProject.Style = (Style)FindResource(mode == 3 ? "BtnPrimary" : "BtnSecondary");
        }

        private void ProjectSelectionRequested(object sender, EventArgs e)
        {
            while (true)
            {
                var window = new ProjectSelectionWindow(GetActiveDatabases(), _selectedDbName) { Owner = this };
                bool selected = window.ShowDialog() == true;
                if (selected)
                {
                    if (string.IsNullOrWhiteSpace(window.SelectedDatabase)) return;
                    _selectedDbName = window.SelectedDatabase;
                    _templateEditor.SetProjectDatabase(_selectedDbName);
                    _projectEditor.SetProjectDatabase(_selectedDbName);
                    LogAction($"Выбран проект «{_selectedDbName}»");
                    return;
                }
                if (!window.ConnectNewRequested) return;
                BtnOpenConnection_Click(this, new RoutedEventArgs());
                if (string.IsNullOrWhiteSpace(_selectedDbName) || !GetActiveDatabases().Any(x => x.Equals(_selectedDbName, StringComparison.OrdinalIgnoreCase))) _selectedDbName = GetActiveDatabases().FirstOrDefault() ?? _selectedDbName;
            }
        }

        private void NavDatabase_Click(object sender, RoutedEventArgs e) => ActivateMode(1);
        private void NavTemplates_Click(object sender, RoutedEventArgs e) => ActivateMode(2);
        private void NavProject_Click(object sender, RoutedEventArgs e) => ActivateMode(3);
        private void MenuViewFullscreen_Click(object sender, RoutedEventArgs e) { WindowState = WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal; }

        private void MenuOptionsCheckFields_Click(object sender, RoutedEventArgs e)
        {
            var activeDatabases = GetActiveDatabases();
            if (activeDatabases.Count == 0) { MessageBox.Show("Сначала подключите хотя бы одну активную базу данных.", "Проверить свободные поля", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            var window = new FreeRecordCheckWindow(_connection, activeDatabases, _selectedDbName) { Owner = this }; window.ShowDialog();
        }

        private void MenuOptionsCheckAddresses_Click(object sender, RoutedEventArgs e)
        {
            var activeDatabases = GetActiveDatabases();
            if (activeDatabases.Count == 0) { MessageBox.Show("Сначала подключите хотя бы одну активную базу данных.", "Проверить свободные адреса", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            var window = new FreeAddressCheckWindow(_connection, activeDatabases, _selectedDbName) { Owner = this }; window.ShowDialog();
        }

        private void MenuOptionsCheckDuplicateAddresses_Click(object sender, RoutedEventArgs e)
        {
            var activeDatabases = GetActiveDatabases();
            if (activeDatabases.Count == 0) { MessageBox.Show("Сначала подключите хотя бы одну активную базу данных.", "Проверить дублирование адресов", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            var window = new DuplicateCheckWindow(_connection, activeDatabases, _selectedDbName, true) { Owner = this }; window.ShowDialog();
        }

        private void MenuOptionsCheckDuplicateRecords_Click(object sender, RoutedEventArgs e)
        {
            var activeDatabases = GetActiveDatabases();
            if (activeDatabases.Count == 0) { MessageBox.Show("Сначала подключите хотя бы одну активную базу данных.", "Проверить дублирование Recordов", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            var window = new DuplicateCheckWindow(_connection, activeDatabases, _selectedDbName, false) { Owner = this }; window.ShowDialog();
        }

        private void MenuDatabaseFreeSql_Click(object sender, RoutedEventArgs e)
        {
            if (_connection.ConnectedDatabases.Count == 0) { MessageBox.Show("Сначала подключите хотя бы одну базу данных.", "Свободный ввод", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            var window = new FreeSqlWindow(_connection, _selectedDbName) { Owner = this }; window.ShowDialog();
        }

        private void MenuHelpHotkeys_Click(object sender, RoutedEventArgs e) { var window = new HotkeysWindow { Owner = this }; window.ShowDialog(); }
        private void MenuHelpAbout_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Configurator v4.1\nАрхитектура подключенной БД · Граф зависимостей · Безопасные операции\n\nProgram/Matrix работают как расширяемые иерархии.", "О программе", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void MenuFileExportProject_Click(object sender, RoutedEventArgs e)
        {
            string database = !string.IsNullOrWhiteSpace(_selectedDbName) ? _selectedDbName : _connection.ConnectedDatabases.FirstOrDefault() ?? "";
            if (string.IsNullOrWhiteSpace(database)) { MessageBox.Show("Сначала подключите и выберите базу проекта.", "Экспорт проекта", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            var dialog = new Microsoft.Win32.SaveFileDialog { Title = "Экспорт базы проекта", Filter = "SQL Server backup (*.bak)|*.bak|Все файлы (*.*)|*.*", DefaultExt = ".bak", AddExtension = true, FileName = database + ".bak" };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                StatusText.Text = $"Экспорт проекта: {database}...";
                var db = new DatabaseService(_connection.ToConnectionString(database));
                await db.BackupDatabaseAsync(database, dialog.FileName);
                LogAction($"Экспорт проекта: {database} → {dialog.FileName}");
                MessageBox.Show("Резервная копия базы проекта создана.", "Экспорт проекта", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { StatusText.Text = "Ошибка экспорта проекта"; MessageBox.Show(ex.Message, "Экспорт проекта", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void MenuFileClearCache_Click(object sender, RoutedEventArgs e) { _vms.Clear(); _loadedTreeNodes.Clear(); RebuildTree(); LogAction("Кэш очищен"); }

        private void MenuFileSettings_Click(object sender, RoutedEventArgs e)
        {
            var admin = new AdminPasswordWindow { Owner = this };
            if (admin.ShowDialog() != true) return;
            var activeDatabases = GetActiveDatabases();
            if (activeDatabases.Count == 0) { MessageBox.Show("Сначала подключите хотя бы одну активную базу данных.", "Текстовые списки", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            var window = new LookupSettingsWindow(_connection, activeDatabases, _selectedDbName) { Owner = this }; window.ShowDialog();
        }

        private void MenuFileExit_Click(object sender, RoutedEventArgs e) => Close();
        private void MenuHelpDocs_Click(object sender, RoutedEventArgs e) => MessageBox.Show("Документация будет добавлена в следующей версии.", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}

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

            // Configurator 4.1 always starts maximized so the graph, schema and object panels
            // have the full engineering workspace available from the first screen.
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
                if (!result.connected)
                    _failedDatabases.Add(result.dbName);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _reconnectCts?.Cancel();
            _dataLoadCts?.Cancel();

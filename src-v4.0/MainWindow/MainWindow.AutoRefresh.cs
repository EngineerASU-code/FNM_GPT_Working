using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace Configurator;

public partial class MainWindow
{
    private DispatcherTimer _databaseAutoRefreshTimer;
    private DateTime _lastSchemaAutoRefresh = DateTime.MinValue;

    private static readonly bool _autoRefreshHandlerRegistered = RegisterAutoRefreshHandler();

    private static bool RegisterAutoRefreshHandler()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(MainWindow_AutoRefreshLoaded));
        return true;
    }

    private static void MainWindow_AutoRefreshLoaded(object sender, RoutedEventArgs e)
    {
        var window = (MainWindow)sender;
        window.StartDatabaseAutoRefresh();
    }

    private void StartDatabaseAutoRefresh()
    {
        if (_databaseAutoRefreshTimer != null) return;

        _databaseAutoRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _databaseAutoRefreshTimer.Tick += async (s, e) => await AutoRefreshDatabaseViewAsync();
        _databaseAutoRefreshTimer.Start();
        _leftPanel.SetLoadingStatus("Автообновление включено", false);
    }

    private async System.Threading.Tasks.Task AutoRefreshDatabaseViewAsync()
    {
        if (_connection == null || _connection.ConnectedDatabases.Count == 0) return;
        if (EditorModeView.Visibility != Visibility.Visible) return;
        if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;

        try
        {
            if (_activeVm != null && !string.IsNullOrWhiteSpace(_activeDbName))
                await LoadDataAsync();

            if ((DateTime.UtcNow - _lastSchemaAutoRefresh).TotalSeconds >= 15)
            {
                _lastSchemaAutoRefresh = DateTime.UtcNow;
                var expanded = _leftPanel.ServerTree.Items.OfType<System.Windows.Controls.TreeViewItem>()
                    .Where(x => x.IsExpanded && x.Tag is string)
                    .Select(x => x.Tag.ToString())
                    .ToList();

                foreach (var dbName in expanded)
                {
                    var node = _leftPanel.ServerTree.Items.OfType<System.Windows.Controls.TreeViewItem>()
                        .FirstOrDefault(x => string.Equals(x.Tag?.ToString(), dbName, StringComparison.OrdinalIgnoreCase));
                    if (node == null) continue;
                    _loadedTreeNodes.Remove(dbName);
                    OnDatabaseExpanded(dbName, node);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AutoRefreshDatabaseView] {ex.Message}");
        }
    }

    protected void StopDatabaseAutoRefresh()
    {
        _databaseAutoRefreshTimer?.Stop();
        _databaseAutoRefreshTimer = null;
    }
}

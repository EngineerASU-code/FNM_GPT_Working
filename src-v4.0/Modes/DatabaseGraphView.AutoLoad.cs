using System;
using System.Windows;
using System.Windows.Threading;

namespace Configurator;

/// <summary>
/// Keeps the graph synchronized with the selected project. The first schema load
/// is always performed after the control is loaded; subsequent project changes are
/// detected without requiring the user to press Refresh.
/// </summary>
public partial class DatabaseGraphView
{
    private DispatcherTimer _autoGraphTimer;
    private string _autoGraphDatabase = string.Empty;
    private bool _autoGraphLoading;
    private bool _autoGraphInitialRefreshDone;

    private static readonly bool _autoGraphHandlersRegistered = RegisterAutoGraphHandlers();

    private static bool RegisterAutoGraphHandlers()
    {
        EventManager.RegisterClassHandler(typeof(DatabaseGraphView), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(AutoGraphLoaded), true);
        EventManager.RegisterClassHandler(typeof(DatabaseGraphView), FrameworkElement.UnloadedEvent,
            new RoutedEventHandler(AutoGraphUnloaded), true);
        return true;
    }

    private static void AutoGraphLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DatabaseGraphView view) return;
        view.StartAutoGraphTimer();
    }

    private static void AutoGraphUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is DatabaseGraphView view)
            view._autoGraphTimer?.Stop();
    }

    private void StartAutoGraphTimer()
    {
        _autoGraphTimer?.Stop();
        _autoGraphInitialRefreshDone = false;
        _autoGraphTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _autoGraphTimer.Tick += async (_, _) =>
        {
            if (_autoGraphLoading || !IsVisible || string.IsNullOrWhiteSpace(_database)) return;

            bool databaseChanged = !string.Equals(_autoGraphDatabase, _database, StringComparison.OrdinalIgnoreCase);
            bool needsInitialLoad = !_autoGraphInitialRefreshDone;
            bool needsDataLoad = _tables.Count == 0;
            if (!databaseChanged && !needsInitialLoad && !needsDataLoad) return;

            _autoGraphLoading = true;
            try
            {
                await RefreshAsync();
                _autoGraphDatabase = _database;
                _autoGraphInitialRefreshDone = true;
                StartCompactGraphSync();
            }
            finally
            {
                _autoGraphLoading = false;
            }
        };
        _autoGraphTimer.Start();
    }
}

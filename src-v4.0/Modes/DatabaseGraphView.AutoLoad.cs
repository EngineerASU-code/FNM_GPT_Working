using System;
using System.Windows;
using System.Windows.Threading;

namespace Configurator;

/// <summary>
/// Keeps the graph view synchronized with the selected project without requiring
/// the user to press the center refresh button. The legacy graph renderer remains
/// available, but the compact renderer is rebuilt automatically after schema load.
/// </summary>
public partial class DatabaseGraphView
{
    private DispatcherTimer _autoGraphTimer;
    private string _autoGraphDatabase = string.Empty;
    private bool _autoGraphLoading;

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
        _autoGraphTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _autoGraphTimer.Tick += async (_, _) =>
        {
            if (_autoGraphLoading || !IsVisible || string.IsNullOrWhiteSpace(_database)) return;
            if (_tables.Count > 0 && string.Equals(_autoGraphDatabase, _database, StringComparison.OrdinalIgnoreCase)) return;

            _autoGraphLoading = true;
            try
            {
                await RefreshAsync();
                _autoGraphDatabase = _database;
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

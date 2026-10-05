using System;
using System.Windows;
using System.Windows.Threading;

namespace Configurator;

/// <summary>
/// Keeps the graph control synchronized with the project selected by MainWindow.
/// The project selector is owned by MainWindow, so mode 2/3 must not rely on a
/// second, duplicated selection state inside the graph control.
/// </summary>
public partial class TemplateArchitectureView
{
    private DispatcherTimer _graphSyncTimer;
    private string _graphConfiguredDatabase = "";
    private bool _graphSyncStarted;

    private static readonly bool _graphBindingHandlersRegistered = RegisterGraphBindingHandlers();

    private static bool RegisterGraphBindingHandlers()
    {
        EventManager.RegisterClassHandler(typeof(TemplateArchitectureView), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(StartGraphBinding));
        EventManager.RegisterClassHandler(typeof(TemplateArchitectureView), FrameworkElement.UnloadedEvent,
            new RoutedEventHandler(StopGraphBinding));
        return true;
    }

    private static void StartGraphBinding(object sender, RoutedEventArgs e)
    {
        var view = (TemplateArchitectureView)sender;
        view.StartGraphBindingTimer();
    }

    private static void StopGraphBinding(object sender, RoutedEventArgs e)
    {
        var view = (TemplateArchitectureView)sender;
        view._graphSyncTimer?.Stop();
        view._graphSyncTimer = null;
        view._graphSyncStarted = false;
    }

    private void StartGraphBindingTimer()
    {
        if (_graphSyncStarted) return;
        _graphSyncStarted = true;
        _graphSyncTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _graphSyncTimer.Tick += GraphSyncTimer_Tick;
        _graphSyncTimer.Start();
        SyncGraphNow();
    }

    private void GraphSyncTimer_Tick(object sender, EventArgs e) => SyncGraphNow();

    private void SyncGraphNow()
    {
        if (DatabaseGraphControl == null || _connection == null || string.IsNullOrWhiteSpace(_selectedDatabase))
            return;

        if (string.Equals(_graphConfiguredDatabase, _selectedDatabase, StringComparison.OrdinalIgnoreCase))
            return;

        _graphConfiguredDatabase = _selectedDatabase;
        DatabaseGraphControl.Configure(_connection, _selectedDatabase);
        TxtStatus.Text = $"Граф БД: чтение структуры «{_selectedDatabase}»…";
    }
}

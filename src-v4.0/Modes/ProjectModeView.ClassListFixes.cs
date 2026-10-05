using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace Configurator;

/// <summary>
/// The project view no longer treats the historical "use in project" registry
/// flag as a filter. All classes represented by the architecture are visible,
/// including Program, Step, Matrix and classes 10-12.
/// </summary>
public partial class ProjectModeView
{
    private DispatcherTimer _classListFixTimer;

    private static readonly bool _classListFixHandlersRegistered = RegisterClassListFixHandlers();

    private static bool RegisterClassListFixHandlers()
    {
        EventManager.RegisterClassHandler(typeof(ProjectModeView), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ClassListFixLoaded), true);
        EventManager.RegisterClassHandler(typeof(ProjectModeView), FrameworkElement.UnloadedEvent,
            new RoutedEventHandler(ClassListFixUnloaded), true);
        return true;
    }

    private static void ClassListFixLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ProjectModeView view)
            view.StartClassListFixTimer();
    }

    private static void ClassListFixUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is ProjectModeView view)
            view._classListFixTimer?.Stop();
    }

    private void StartClassListFixTimer()
    {
        _classListFixTimer?.Stop();
        _classListFixTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _classListFixTimer.Tick += (_, _) => NormalizeClassList();
        _classListFixTimer.Start();
        NormalizeClassList();
    }

    private void NormalizeClassList()
    {
        if (!IsVisible || ClassList == null || _architecture?.Classes == null) return;

        var classes = _architecture.Classes
            .OrderBy(x => x.ClassNumber > 0 ? x.ClassNumber : int.MaxValue)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ClassList.Items.Count != classes.Count || classes.Any(x => !ClassList.Items.Contains(x)))
            ClassList.ItemsSource = classes;
    }
}

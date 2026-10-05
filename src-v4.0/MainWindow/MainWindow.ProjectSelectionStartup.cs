using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace Configurator;

public partial class MainWindow
{
    private static readonly bool _projectSelectionStartupHandlerRegistered = RegisterProjectSelectionStartupHandler();

    private static bool RegisterProjectSelectionStartupHandler()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), ButtonBase.ClickEvent,
            new RoutedEventHandler(MainWindow_ProjectNavigationStartupClicked), true);
        return true;
    }

    private static void MainWindow_ProjectNavigationStartupClicked(object sender, RoutedEventArgs e)
    {
        var window = (MainWindow)sender;
        if (e.OriginalSource is not Button button) return;
        if (button.Name != "NavTemplates" && button.Name != "NavProject") return;
        if (!string.IsNullOrWhiteSpace(window._selectedDbName)) return;
        if (window.GetActiveDatabases().Count == 0) return;

        window.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (string.IsNullOrWhiteSpace(window._selectedDbName))
                window.ProjectSelectionRequested(window, EventArgs.Empty);
        }), DispatcherPriority.Input);
    }
}

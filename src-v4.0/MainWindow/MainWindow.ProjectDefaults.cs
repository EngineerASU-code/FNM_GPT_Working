using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace Configurator;

public partial class MainWindow
{
    private static readonly bool _projectDefaultsHandlersRegistered = RegisterProjectDefaultsHandlers();

    private static bool RegisterProjectDefaultsHandlers()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), ButtonBase.ClickEvent,
            new RoutedEventHandler(MainWindow_ProjectNavigationClicked), true);
        return true;
    }

    private static void MainWindow_ProjectNavigationClicked(object sender, RoutedEventArgs e)
    {
        var window = (MainWindow)sender;
        if (e.OriginalSource is not Button button) return;
        if (button.Name != "NavTemplates" && button.Name != "NavProject") return;

        window.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!string.IsNullOrWhiteSpace(window._selectedDbName)) return;
            var active = window.GetActiveDatabases().FirstOrDefault();
            if (string.IsNullOrWhiteSpace(active)) return;
            window._selectedDbName = active;
            window._templateEditor?.SetProjectDatabase(active);
            window._projectEditor?.SetProjectDatabase(active);
            window.LogAction($"Автоматически выбран первый активный проект «{active}»");
        }), DispatcherPriority.ContextIdle);
    }
}

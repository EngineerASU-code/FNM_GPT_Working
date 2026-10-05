using System;
using System.Windows;

namespace Configurator;

public partial class TemplateArchitectureView
{
    static TemplateArchitectureView()
    {
        EventManager.RegisterClassHandler(typeof(TemplateArchitectureView), FrameworkElement.LoadedEvent, new RoutedEventHandler(GraphHost_LoadedStatic));
    }

    private static void GraphHost_LoadedStatic(object sender, RoutedEventArgs e)
        => ((TemplateArchitectureView)sender).RefreshGraphFromCurrentProject();

    private void RefreshGraphFromCurrentProject()
    {
        if (DatabaseGraphControl == null) return;
        DatabaseGraphControl.Configure(_connection, _selectedDatabase);
    }
}

using System;

namespace Configurator;

public partial class TemplateArchitectureView
{
    private void GraphHost_Loaded(object sender, System.Windows.RoutedEventArgs e)
        => RefreshGraphFromCurrentProject();

    private void GraphHost_IsVisibleChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is bool visible && visible)
            RefreshGraphFromCurrentProject();
    }

    private void RefreshGraphFromCurrentProject()
    {
        if (DatabaseGraphControl == null) return;
        DatabaseGraphControl.Configure(_connection, _selectedDatabase);
    }
}

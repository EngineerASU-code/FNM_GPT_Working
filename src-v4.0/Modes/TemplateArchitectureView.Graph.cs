using System;
using System.Windows;

namespace Configurator;

public partial class TemplateArchitectureView
{
    // The existing 4.1 constructor remains untouched. This hook lets the new graph
    // follow the mode visibility and the currently selected project database.
    private readonly bool _graphHook = RegisterGraphHook();

    private bool RegisterGraphHook()
    {
        IsVisibleChanged += (_, __) => RefreshGraphFromCurrentProject();
        Loaded += (_, __) => RefreshGraphFromCurrentProject();
        return true;
    }

    private void RefreshGraphFromCurrentProject()
    {
        if (DatabaseGraphControl == null) return;
        if (!IsVisible && !DatabaseGraphControl.IsVisible) return;
        DatabaseGraphControl.Configure(_connection, _selectedDatabase);
    }
}

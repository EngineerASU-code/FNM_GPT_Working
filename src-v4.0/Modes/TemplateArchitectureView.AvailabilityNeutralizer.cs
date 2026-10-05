using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Configurator;

/// <summary>
/// Historical class availability is no longer a user-facing project setting.
/// The architecture editor keeps the legacy registry service for compatibility,
/// but the old enable/disable controls are permanently removed from the UI.
/// </summary>
public partial class TemplateArchitectureView
{
    private static readonly bool _availabilityUiHandlersRegistered = RegisterAvailabilityUiHandlers();

    private static bool RegisterAvailabilityUiHandlers()
    {
        EventManager.RegisterClassHandler(typeof(TemplateArchitectureView), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(HideAvailabilityUi), true);
        EventManager.RegisterClassHandler(typeof(TemplateArchitectureView), Selector.SelectionChangedEvent,
            new SelectionChangedEventHandler(HideAvailabilityUiAfterSelection), true);
        return true;
    }

    private static void HideAvailabilityUi(object sender, RoutedEventArgs e)
    {
        if (sender is TemplateArchitectureView view)
            view.HideAvailabilityControls();
    }

    private static void HideAvailabilityUiAfterSelection(object sender, SelectionChangedEventArgs e)
    {
        if (sender is TemplateArchitectureView view && e.OriginalSource == view.ClassList)
            view.HideAvailabilityControls();
    }

    private void HideAvailabilityControls()
    {
        if (ChkAvailable != null)
        {
            ChkAvailable.Visibility = Visibility.Collapsed;
            ChkAvailable.IsEnabled = false;
        }
        if (BtnRestoreArchived != null)
        {
            BtnRestoreArchived.Visibility = Visibility.Collapsed;
            BtnRestoreArchived.IsEnabled = false;
        }
    }
}

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace Configurator;

public partial class ProjectModeView
{
    private static readonly bool _startupFixHandlersRegistered = RegisterStartupFixHandlers();

    private static bool RegisterStartupFixHandlers()
    {
        EventManager.RegisterClassHandler(typeof(ProjectModeView), UIElement.IsVisibleChangedEvent,
            new DependencyPropertyChangedEventHandler(ProjectModeView_IsVisibleChangedStartupFix));
        EventManager.RegisterClassHandler(typeof(ProjectModeView), Selector.SelectionChangedEvent,
            new SelectionChangedEventHandler(ProjectModeView_ClassSelectionChangedStartupFix), true);
        return true;
    }

    private static void ProjectModeView_IsVisibleChangedStartupFix(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is bool visible && visible)
            ScheduleProjectDataReady((ProjectModeView)sender);
    }

    private static void ProjectModeView_ClassSelectionChangedStartupFix(object sender, SelectionChangedEventArgs e)
    {
        var view = (ProjectModeView)sender;
        if (e.OriginalSource == view.ClassList)
            ScheduleProjectDataReady(view);
    }

    private static void ScheduleProjectDataReady(ProjectModeView view)
    {
        view.Dispatcher.BeginInvoke(new Action(async () => await view.EnsureProjectDataReadyAsync()), DispatcherPriority.ContextIdle);
    }

    private async Task EnsureProjectDataReadyAsync()
    {
        if (_connection == null || string.IsNullOrWhiteSpace(_selectedDatabase)) return;

        await ResolveProjectArchitectureAsync();

        // The resolver may have already run for this database. The original
        // implementation returned early in that case, which left a newly
        // selected class without its actual fields/rows until manual refresh.
        RefreshAllProjectClasses();

        if (_selectedClass != null)
        {
            await LoadActualClassFieldsAsync();
            await LoadObjectsAsync();
            RebuildObjectFilters();
        }
    }

    private void RefreshAllProjectClasses()
    {
        var classes = _architecture.Classes
            .Where(x => x.Available)
            .OrderBy(x => x.ClassNumber > 0 ? x.ClassNumber : int.MaxValue)
            .ThenBy(x => x.Name)
            .ToList();

        ClassList.ItemsSource = classes;
    }
}

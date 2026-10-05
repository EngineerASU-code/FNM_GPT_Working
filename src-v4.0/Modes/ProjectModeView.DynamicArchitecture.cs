using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Configurator;

public partial class ProjectModeView
{
    private string _resolvedProjectDatabase = "";
    private bool _resolvingProjectArchitecture;
    private static readonly bool _dynamicHandlersRegistered = RegisterDynamicHandlers();

    private static bool RegisterDynamicHandlers()
    {
        EventManager.RegisterClassHandler(typeof(ProjectModeView), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ProjectModeView_LoadedDynamic));
        EventManager.RegisterClassHandler(typeof(ProjectModeView), FrameworkElement.IsVisibleChangedEvent,
            new DependencyPropertyChangedEventHandler(ProjectModeView_VisibilityChangedDynamic));
        EventManager.RegisterClassHandler(typeof(ProjectModeView), ButtonBase.ClickEvent,
            new RoutedEventHandler(ProjectModeView_ButtonClickedDynamic), true);
        EventManager.RegisterClassHandler(typeof(ProjectModeView), Selector.SelectionChangedEvent,
            new SelectionChangedEventHandler(ProjectModeView_SelectionChangedDynamic), true);
        EventManager.RegisterClassHandler(typeof(ProjectModeView), UIElement.PreviewMouseLeftButtonDownEvent,
            new System.Windows.Input.MouseButtonEventHandler(ProjectModeView_PreviewMouseDownDynamic), true);
        return true;
    }

    private static void ProjectModeView_LoadedDynamic(object sender, RoutedEventArgs e)
        => ScheduleDynamicArchitecture((ProjectModeView)sender);

    private static void ProjectModeView_VisibilityChangedDynamic(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is bool visible && visible)
            ScheduleDynamicArchitecture((ProjectModeView)sender);
    }

    private static void ProjectModeView_ButtonClickedDynamic(object sender, RoutedEventArgs e)
    {
        var view = (ProjectModeView)sender;
        if (e.OriginalSource is not Button button) return;
        var content = button.Content?.ToString() ?? "";
        if (content == "Выбрать проект" || content == "↻")
            ScheduleDynamicArchitecture(view);
    }

    private static void ProjectModeView_SelectionChangedDynamic(object sender, SelectionChangedEventArgs e)
    {
        var view = (ProjectModeView)sender;
        if (e.OriginalSource == view.ObjectList)
            view.Dispatcher.BeginInvoke(new Action(() => view.RenderLookupObjectDetailsAsync()), DispatcherPriority.ContextIdle);
    }

    private static void ProjectModeView_PreviewMouseDownDynamic(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var view = (ProjectModeView)sender;
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject)?.Name != "BtnSave") return;
        e.Handled = true;
        view.Dispatcher.BeginInvoke(new Action(() => view.SaveObjectWithLookupControlsAsync()), DispatcherPriority.Background);
    }

    private static void ScheduleDynamicArchitecture(ProjectModeView view)
    {
        view.Dispatcher.BeginInvoke(new Action(async () => await view.ResolveProjectArchitectureAsync()), DispatcherPriority.ContextIdle);
    }

    private async Task ResolveProjectArchitectureAsync()
    {
        if (_resolvingProjectArchitecture || _connection == null || string.IsNullOrWhiteSpace(_selectedDatabase)) return;
        if (string.Equals(_resolvedProjectDatabase, _selectedDatabase, StringComparison.OrdinalIgnoreCase)) return;

        _resolvingProjectArchitecture = true;
        try
        {
            var db = new DatabaseService(_connection.ToConnectionString(_selectedDatabase));
            var resolver = new ProjectArchitectureResolver();
            await resolver.ResolveAsync(_architecture, db);
            _resolvedProjectDatabase = _selectedDatabase;
            RefreshClasses();
            if (_selectedClass != null)
            {
                await LoadActualClassFieldsAsync();
                await LoadObjectsAsync();
                RebuildObjectFilters();
            }
            TxtStatus.Text = $"Архитектура проекта определена по БД «{_selectedDatabase}»";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Архитектура БД не определена: {ex.Message}";
        }
        finally
        {
            _resolvingProjectArchitecture = false;
        }
    }

    private static T FindAncestor<T>(DependencyObject source) where T : DependencyObject
    {
        var current = source;
        while (current != null)
        {
            if (current is T match) return match;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}

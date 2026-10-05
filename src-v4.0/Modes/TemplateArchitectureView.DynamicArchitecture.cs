using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Configurator;

public partial class TemplateArchitectureView
{
    private string _resolvedProjectDatabaseV42 = "";
    private bool _resolvingProjectArchitectureV42;
    private static readonly bool _dynamicHandlersRegisteredV42 = RegisterDynamicHandlersV42();

    private static bool RegisterDynamicHandlersV42()
    {
        EventManager.RegisterClassHandler(typeof(TemplateArchitectureView), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(TemplateArchitectureView_LoadedDynamic));
        EventManager.RegisterClassHandler(typeof(TemplateArchitectureView), FrameworkElement.IsVisibleChangedEvent,
            new DependencyPropertyChangedEventHandler(TemplateArchitectureView_VisibilityChangedDynamic));
        EventManager.RegisterClassHandler(typeof(TemplateArchitectureView), ButtonBase.ClickEvent,
            new RoutedEventHandler(TemplateArchitectureView_ButtonClickedDynamic), true);
        return true;
    }

    private static void TemplateArchitectureView_LoadedDynamic(object sender, RoutedEventArgs e)
        => ScheduleTemplateArchitecture((TemplateArchitectureView)sender);

    private static void TemplateArchitectureView_VisibilityChangedDynamic(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is bool visible && visible)
            ScheduleTemplateArchitecture((TemplateArchitectureView)sender);
    }

    private static void TemplateArchitectureView_ButtonClickedDynamic(object sender, RoutedEventArgs e)
    {
        var view = (TemplateArchitectureView)sender;
        if (e.OriginalSource is Button button && (button.Content?.ToString() == "Выбрать проект" || button.Content?.ToString() == "↻"))
            ScheduleTemplateArchitecture(view);
    }

    private static void ScheduleTemplateArchitecture(TemplateArchitectureView view)
    {
        view.Dispatcher.BeginInvoke(new Action(async () => await view.ResolveTemplateArchitectureAsync()), DispatcherPriority.ContextIdle);
    }

    private async Task ResolveTemplateArchitectureAsync()
    {
        if (_resolvingProjectArchitectureV42 || _connection == null || string.IsNullOrWhiteSpace(_selectedDatabase)) return;
        if (string.Equals(_resolvedProjectDatabaseV42, _selectedDatabase, StringComparison.OrdinalIgnoreCase)) return;

        _resolvingProjectArchitectureV42 = true;
        try
        {
            var db = new DatabaseService(_connection.ToConnectionString(_selectedDatabase));
            var resolver = new ProjectArchitectureResolver();
            await resolver.ResolveAsync(_architecture, db);
            _resolvedProjectDatabaseV42 = _selectedDatabase;
            RefreshClassList();
            if (_selectedClass != null)
                await RenderSelectedClassAsync(_selectedClass);
            TxtStatus.Text = $"Структура классов определена по БД «{_selectedDatabase}»";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Структура проекта не определена: {ex.Message}";
        }
        finally
        {
            _resolvingProjectArchitectureV42 = false;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Configurator.Core.Architecture;

namespace Configurator;

public partial class ProjectModeView
{
    private static readonly bool _objectVisualHandlersRegistered = RegisterObjectVisualHandlers();

    private static bool RegisterObjectVisualHandlers()
    {
        EventManager.RegisterClassHandler(typeof(ProjectModeView), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ObjectVisualLoaded), true);
        EventManager.RegisterClassHandler(typeof(ProjectModeView), Selector.SelectionChangedEvent,
            new SelectionChangedEventHandler(ObjectVisualSelectionChanged), true);
        EventManager.RegisterClassHandler(typeof(ProjectModeView), ButtonBase.ClickEvent,
            new RoutedEventHandler(ObjectVisualButtonClicked), true);
        return true;
    }

    private static void ObjectVisualLoaded(object sender, RoutedEventArgs e)
    {
        var view = (ProjectModeView)sender;
        view.Dispatcher.BeginInvoke(new Action(() => view.RenderActualObjectCardAsync()), DispatcherPriority.ContextIdle);
    }

    private static void ObjectVisualSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var view = (ProjectModeView)sender;
        if (e.OriginalSource == view.ObjectList)
        {
            view.ObjectEditor.Visibility = Visibility.Collapsed;
            view.Dispatcher.BeginInvoke(new Action(() => view.RenderActualObjectCardAsync()), DispatcherPriority.ContextIdle);
        }
    }

    private static void ObjectVisualButtonClicked(object sender, RoutedEventArgs e)
    {
        var view = (ProjectModeView)sender;
        if (e.OriginalSource is Button button && (button.Name == "BtnSave" || button.Name == "BtnDuplicate" || button.Name == "BtnDelete"))
            view.Dispatcher.BeginInvoke(new Action(() => view.RenderActualObjectCardAsync()), DispatcherPriority.ContextIdle);
    }

    private async void RenderActualObjectCardAsync()
    {
        if (_selectedRow == null || _selectedClass == null || _connection == null || string.IsNullOrWhiteSpace(_selectedDatabase))
        {
            ObjectEditor.Visibility = Visibility.Visible;
            return;
        }

        var fields = _objects.Columns.Cast<DataColumn>()
            .Select(c => _selectedClass.Fields.FirstOrDefault(f => f.Name.Equals(c.ColumnName, StringComparison.OrdinalIgnoreCase))
                ?? new FieldDefinition { Name = c.ColumnName, DataType = c.DataType.Name, Group = "Прочее" })
            .ToList();

        var lookup = new LookupOptionsService(new DatabaseService(_connection.ToConnectionString(_selectedDatabase)));
        var lookupTasks = new Dictionary<string, Task<IReadOnlyList<LookupOption>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            if (!lookupTasks.ContainsKey(field.Name))
                lookupTasks[field.Name] = TryLoadLookupAsync(lookup, _selectedClass.Name, _selectedClass.PrimaryStorage?.TableName, field.Name);
        }
        await Task.WhenAll(lookupTasks.Values);

        ObjectEditor.Children.Clear();
        ObjectEditor.ColumnDefinitions.Clear();
        ObjectEditor.RowDefinitions.Clear();
        ObjectEditor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 430 });

        TxtObjectName.Text = PickName(_selectedRow, _selectedClass);
        string plc = GetText("PLC");
        string record = GetText("Record");
        string area = GetText("Area");
        TxtObjectMeta.Text = string.IsNullOrWhiteSpace(plc) && string.IsNullOrWhiteSpace(record)
            ? $"Класс: {_selectedClass.Name} · все поля текущей таблицы"
            : $"Класс: {_selectedClass.Name} · PLC {plc} · Record {record} · Area {area}";
        TxtPreviewGlyph.Text = _selectedClass.Name.Length > 3 ? _selectedClass.Name[..3].ToUpperInvariant() : _selectedClass.Name.ToUpperInvariant();
        TxtPreviewClass.Text = _selectedClass.Name;

        var groups = fields
            .Select(f => new { Field = f, Group = ResolveObjectGroup(f.Name) })
            .GroupBy(x => x.Group, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => GroupOrder(g.Key))
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in groups)
        {
            var card = new Border
            {
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(11),
                CornerRadius = new CornerRadius(9),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            card.SetResourceReference(Border.BackgroundProperty, "BrushBase");
            card.SetResourceReference(Border.BorderBrushProperty, "BrushBorder");
            card.BorderThickness = new Thickness(1);

            var stack = new StackPanel();
            var header = new TextBlock { Text = group.Key, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
            header.SetResourceReference(TextBlock.ForegroundProperty, "BrushText");
            stack.Children.Add(header);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 230 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 230 });

            int index = 0;
            foreach (var item in group)
            {
                int rowIndex = index / 2;
                int columnIndex = index % 2;
                while (grid.RowDefinitions.Count <= rowIndex)
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var fieldStack = new StackPanel { Margin = new Thickness(columnIndex == 0 ? 0 : 7, 0, 0, 7) };
                var label = new TextBlock { Text = item.Field.Name, FontSize = 9, Margin = new Thickness(0, 0, 0, 3), ToolTip = item.Field.Name };
                label.SetResourceReference(TextBlock.ForegroundProperty, "BrushTextSecondary");
                fieldStack.Children.Add(label);

                lookupTasks.TryGetValue(item.Field.Name, out var task);
                var options = task?.Result ?? Array.Empty<LookupOption>();
                if (options.Count > 0)
                {
                    var combo = new ComboBox
                    {
                        Height = 32,
                        ItemsSource = options,
                        DisplayMemberPath = nameof(LookupOption.Display),
                        SelectedValuePath = nameof(LookupOption.Key),
                        SelectedValue = GetText(item.Field.Name),
                        Tag = item.Field.Name,
                        ToolTip = $"{item.Field.Name}: связанный справочник"
                    };
                    var hidden = new TextBox { Visibility = Visibility.Collapsed, Tag = item.Field.Name, Text = GetText(item.Field.Name) };
                    combo.SelectionChanged += (_, _) => hidden.Text = Convert.ToString(combo.SelectedValue) ?? "";
                    fieldStack.Children.Add(combo);
                    fieldStack.Children.Add(hidden);
                }
                else
                {
                    var box = new TextBox
                    {
                        Text = GetText(item.Field.Name),
                        Height = 32,
                        Tag = item.Field.Name,
                        TextWrapping = TextWrapping.NoWrap,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        ToolTip = $"{item.Field.Name}: {GetText(item.Field.Name)}"
                    };
                    fieldStack.Children.Add(box);
                }

                Grid.SetRow(fieldStack, rowIndex);
                Grid.SetColumn(fieldStack, columnIndex);
                grid.Children.Add(fieldStack);
                index++;
            }

            stack.Children.Add(grid);
            card.Child = stack;
            ObjectEditor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(card, ObjectEditor.RowDefinitions.Count - 1);
            ObjectEditor.Children.Add(card);
        }

        TxtStatus.Text = $"{_selectedClass.Name} · {_objects.Rows.Count} объектов · {_objects.Columns.Count} полей текущей таблицы";
        ObjectEditor.Visibility = Visibility.Visible;
    }

    private static async Task<IReadOnlyList<LookupOption>> TryLoadLookupAsync(LookupOptionsService lookup, string className, string tableName, string fieldName)
    {
        try { return await lookup.TryGetAsync(className, tableName, fieldName); }
        catch { return Array.Empty<LookupOption>(); }
    }

    private static int GroupOrder(string group) => group switch
    {
        "Object" => 0,
        "Visual" => 1,
        "Config" => 2,
        "Conditions" => 3,
        "Parameters" => 4,
        "Address" => 5,
        "Прочее" => 100,
        _ => 50
    };

    private string ResolveObjectGroup(string fieldName)
    {
        try
        {
            var group = FieldGroupCatalog.Resolve(_selectedClass?.Name ?? "", fieldName);
            return string.IsNullOrWhiteSpace(group) ? "Прочее" : group;
        }
        catch
        {
            return "Прочее";
        }
    }
}

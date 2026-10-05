using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Configurator.Core.Architecture;

namespace Configurator;

public partial class ProjectModeView
{
    private void ObjectDetailsHost_Loaded(object sender, RoutedEventArgs e)
    {
        ObjectList.SelectionChanged -= ObjectList_SelectionChanged_4_2;
        ObjectList.SelectionChanged += ObjectList_SelectionChanged_4_2;
    }

    private void ObjectList_SelectionChanged_4_2(object sender, SelectionChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(RenderCompleteObjectDetails), DispatcherPriority.DataBind);
    }

    private void RenderCompleteObjectDetails()
    {
        if (_selectedRow == null || _selectedClass == null) return;

        ObjectEditor.Children.Clear();
        ObjectEditor.ColumnDefinitions.Clear();
        ObjectEditor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });
        ObjectEditor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });

        TxtObjectName.Text = PickName(_selectedRow, _selectedClass);
        string plc = GetText("PLC");
        string record = GetText("Record");
        TxtObjectMeta.Text = string.IsNullOrWhiteSpace(plc) && string.IsNullOrWhiteSpace(record)
            ? $"Класс: {_selectedClass.Name} · все поля объекта"
            : $"PLC {plc} · Record {record} · все поля объекта";
        TxtPreviewGlyph.Text = _selectedClass.Name.Length > 3 ? _selectedClass.Name[..3].ToUpperInvariant() : _selectedClass.Name.ToUpperInvariant();
        TxtPreviewClass.Text = _selectedClass.Name;

        var actualNames = _selectedRow.Table.Columns.Cast<System.Data.DataColumn>().Select(c => c.ColumnName).ToList();
        var fields = new List<FieldDefinition>();
        foreach (var name in actualNames)
        {
            var known = _selectedClass.Fields.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            fields.Add(known ?? new FieldDefinition { Name = name, DataType = _selectedRow.Table.Columns[name].DataType.Name, Group = "Прочее" });
        }

        var grouped = fields
            .Select(f => new { Field = f, Group = ResolveDisplayGroup(f) })
            .GroupBy(x => x.Group, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => GroupOrder(g.Key))
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int column = 0;
        foreach (var group in grouped)
        {
            var card = new Border { Margin = new Thickness(column == 0 ? 0 : 6, 0, 0, 8), Padding = new Thickness(10), CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Stretch };
            card.SetResourceReference(Border.BackgroundProperty, "BrushBase");
            card.SetResourceReference(Border.BorderBrushProperty, "BrushBorder");
            card.BorderThickness = new Thickness(1);
            var stack = new StackPanel();
            var title = new TextBlock { Text = group.Key, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 7) };
            title.SetResourceReference(TextBlock.ForegroundProperty, "BrushText");
            stack.Children.Add(title);

            foreach (var item in group)
            {
                var field = item.Field;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star), MinWidth = 110 });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star), MinWidth = 150 });
                var label = new TextBlock { Text = field.Name, FontSize = 10, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = field.Name };
                label.SetResourceReference(TextBlock.ForegroundProperty, "BrushTextSecondary");
                Grid.SetColumn(label, 0);
                row.Children.Add(label);
                var box = new TextBox { Text = GetText(field.Name), Height = 30, Padding = new Thickness(7, 3, 7, 3), Tag = field.Name, TextWrapping = TextWrapping.NoWrap, HorizontalContentAlignment = HorizontalAlignment.Left, ToolTip = $"{field.Name}: {GetText(field.Name)}" };
                box.SetResourceReference(Control.BackgroundProperty, "BrushPanel");
                box.SetResourceReference(Control.ForegroundProperty, "BrushText");
                box.SetResourceReference(Control.BorderBrushProperty, "BrushBorder");
                Grid.SetColumn(box, 1);
                row.Children.Add(box);
                stack.Children.Add(row);
            }
            card.Child = stack;
            Grid.SetColumn(card, column);
            ObjectEditor.Children.Add(card);
            column = column == 0 ? 1 : 0;
        }
        TxtStatus.Text = $"{_selectedClass.Name} · показаны все {fields.Count} полей объекта";
    }

    private string ResolveDisplayGroup(FieldDefinition field)
    {
        var group = FieldGroupCatalog.Resolve(_selectedClass.Name, field.Name);
        return string.IsNullOrWhiteSpace(group) ? "Прочее" : group;
    }

    private static int GroupOrder(string group) => group switch
    {
        "Object" => 0,
        "Visual" => 1,
        "Config" => 2,
        "Conditions" => 3,
        "Parameters" => 4,
        "Прочее" => 100,
        _ => 50
    };
}

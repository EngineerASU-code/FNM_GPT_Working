using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Configurator.Core.Architecture;

namespace Configurator;

/// <summary>
/// Renders the complete row from the actual SQL table. The editor is rebuilt as
/// one visual tree after selection so the user does not see the old Config-only
/// tree flashing underneath the final object view.
/// </summary>
public partial class ProjectModeView
{
    private static readonly bool _objectDisplayHandlersRegistered = RegisterObjectDisplayHandlers();

    private static bool RegisterObjectDisplayHandlers()
    {
        EventManager.RegisterClassHandler(typeof(ProjectModeView),
            Selector.SelectionChangedEvent,
            new SelectionChangedEventHandler(ObjectDisplaySelectionChanged), true);
        return true;
    }

    private static void ObjectDisplaySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ProjectModeView view || e.OriginalSource != view.ObjectList) return;

        view.ObjectEditor.Visibility = Visibility.Collapsed;
        view.Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(view.RebuildCompleteObjectEditor));
    }

    private void RebuildCompleteObjectEditor()
    {
        if (_selectedRow == null || _selectedClass == null)
        {
            ObjectEditor.Visibility = Visibility.Visible;
            return;
        }

        ObjectEditor.Children.Clear();
        ObjectEditor.ColumnDefinitions.Clear();
        ObjectEditor.RowDefinitions.Clear();
        ObjectEditor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });
        ObjectEditor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });

        TxtObjectName.Text = PickName(_selectedRow, _selectedClass);
        TxtPreviewGlyph.Text = _selectedClass.Name.Length > 3
            ? _selectedClass.Name[..3].ToUpperInvariant()
            : _selectedClass.Name.ToUpperInvariant();
        TxtPreviewClass.Text = _selectedClass.Name;
        TxtObjectMeta.Text = $"PLC {GetText("PLC")} · Record {GetText("Record")} · Area {GetText("Area")}";

        var columns = _objects.Columns.Cast<DataColumn>().ToList();
        var groups = columns
            .GroupBy(c => FieldGroupCatalog.Resolve(_selectedClass.Name, c.ColumnName), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => GroupOrder(g.Key))
            .ToList();

        int rows = Math.Max(1, (int)Math.Ceiling(groups.Count / 2d));
        for (int i = 0; i < rows; i++)
            ObjectEditor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int i = 0; i < groups.Count; i++)
        {
            var card = BuildObjectGroupCard(groups[i].Key, groups[i].ToList());
            Grid.SetColumn(card, i % 2);
            Grid.SetRow(card, i / 2);
            ObjectEditor.Children.Add(card);
        }

        ObjectEditor.Visibility = Visibility.Visible;
    }

    private Border BuildObjectGroupCard(string group, IReadOnlyList<DataColumn> columns)
    {
        var card = new Border
        {
            Margin = new Thickness(4),
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 300
        };
        card.SetResourceReference(Border.BackgroundProperty, "BrushBase");
        card.SetResourceReference(Border.BorderBrushProperty, "BrushBorder");
        card.BorderThickness = new Thickness(1);

        var stack = new StackPanel();
        var header = new TextBlock
        {
            Text = group,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 7)
        };
        header.SetResourceReference(TextBlock.ForegroundProperty, "BrushText");
        stack.Children.Add(header);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        int index = 0;
        foreach (var column in columns)
        {
            if (index % 2 == 0)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var fieldStack = new StackPanel
            {
                Margin = new Thickness(index % 2 == 0 ? 0 : 7, 0, 0, 7)
            };
            var label = new TextBlock
            {
                Text = column.ColumnName,
                FontSize = 10,
                Margin = new Thickness(0, 0, 0, 2)
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "BrushTextSecondary");
            fieldStack.Children.Add(label);

            var box = new TextBox
            {
                Text = GetText(column.ColumnName),
                Height = 32,
                MinWidth = 130,
                Padding = new Thickness(7, 3, 7, 3),
                Tag = column.ColumnName,
                TextWrapping = TextWrapping.NoWrap,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                ToolTip = $"{column.ColumnName}: {GetText(column.ColumnName)}"
            };
            fieldStack.Children.Add(box);
            Grid.SetColumn(fieldStack, index % 2);
            Grid.SetRow(fieldStack, index / 2);
            grid.Children.Add(fieldStack);
            index++;
        }

        stack.Children.Add(grid);
        card.Child = stack;
        return card;
    }

    private static int GroupOrder(string group) => group switch
    {
        "Object" => 0,
        "Config" => 1,
        "Visual" => 2,
        "Conditions" => 3,
        "Parameters" => 4,
        _ => 5
    };
}

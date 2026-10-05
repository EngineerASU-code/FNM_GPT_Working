using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Configurator;

public partial class DatabaseGraphView
{
    private DispatcherTimer _compactGraphSyncTimer;
    private bool _compactGraphReady;

    private static readonly bool _compactGraphHandlersRegistered = RegisterCompactGraphHandlers();

    private static bool RegisterCompactGraphHandlers()
    {
        EventManager.RegisterClassHandler(typeof(DatabaseGraphView), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(CompactGraphLoaded), true);
        EventManager.RegisterClassHandler(typeof(DatabaseGraphView), ButtonBase.ClickEvent,
            new RoutedEventHandler(CompactGraphButtonClicked), true);
        return true;
    }

    private static void CompactGraphLoaded(object sender, RoutedEventArgs e)
    {
        var view = (DatabaseGraphView)sender;
        view.StartCompactGraphSync();
    }

    private static void CompactGraphButtonClicked(object sender, RoutedEventArgs e)
    {
        var view = (DatabaseGraphView)sender;
        if (e.OriginalSource is Button button && (button.Content?.ToString() == "↻" || button.Name == "GraphRefreshButton"))
            view.StartCompactGraphSync();
    }

    private void StartCompactGraphSync()
    {
        _compactGraphReady = false;
        _compactGraphSyncTimer?.Stop();
        _compactGraphSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _compactGraphSyncTimer.Tick += (_, _) =>
        {
            if (_tables.Count == 0) return;
            _compactGraphSyncTimer.Stop();
            _compactGraphReady = true;
            GraphTableList.ItemsSource = _tables.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (GraphTableList.SelectedItem == null && _tables.Count > 0)
                GraphTableList.SelectedItem = _tables[0];
            RenderCompactGraph();
        };
        _compactGraphSyncTimer.Start();
    }

    private void GraphTableList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_compactGraphReady || GraphTableList.SelectedItem is not GraphTable table) return;
        _selectedTable = table.FullName;
        TxtSelectedTable.Text = table.Name;
        RenderCompactGraph();
    }

    private void RenderCompactGraph()
    {
        if (CompactGraphCanvas == null) return;
        CompactGraphCanvas.Children.Clear();
        if (_tables.Count == 0) return;

        var selected = _tables.FirstOrDefault(x => x.FullName.Equals(_selectedTable, StringComparison.OrdinalIgnoreCase)) ?? _tables[0];
        _selectedTable = selected.FullName;
        TxtSelectedTable.Text = selected.Name;

        var neighbors = _relations
            .Where(r => r.Source.Equals(selected.FullName, StringComparison.OrdinalIgnoreCase) || r.Target.Equals(selected.FullName, StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Source.Equals(selected.FullName, StringComparison.OrdinalIgnoreCase) ? r.Target : r.Source)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => _tables.FirstOrDefault(t => t.FullName.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .Where(t => t != null)
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToList();

        var positions = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase)
        {
            [selected.FullName] = new Point(850, 430)
        };

        var left = neighbors.Where(n => HasRelationDirection(n, selected, false)).ToList();
        var right = neighbors.Where(n => !left.Contains(n)).ToList();
        PlaceColumn(left, 400, 130, positions);
        PlaceColumn(right, 1300, 130, positions);

        foreach (var relation in _relations.Where(r => positions.ContainsKey(r.Source) && positions.ContainsKey(r.Target)))
        {
            var a = positions[relation.Source];
            var b = positions[relation.Target];
            bool selectedRelation = relation.Source.Equals(selected.FullName, StringComparison.OrdinalIgnoreCase) || relation.Target.Equals(selected.FullName, StringComparison.OrdinalIgnoreCase);
            var line = new Line
            {
                X1 = a.X,
                Y1 = a.Y,
                X2 = b.X,
                Y2 = b.Y,
                StrokeThickness = selectedRelation ? 2.2 : 1,
                Opacity = selectedRelation ? 1 : .45
            };
            line.SetResourceReference(Shape.StrokeProperty, relation.Kind.Equals("FK", StringComparison.OrdinalIgnoreCase) ? "BrushAccent" : "BrushTextSecondary");
            if (!relation.Kind.Equals("FK", StringComparison.OrdinalIgnoreCase))
                line.StrokeDashArray = new DoubleCollection { 5, 3 };
            CompactGraphCanvas.Children.Add(line);

            var label = new Border
            {
                Background = new SolidColorBrush(Colors.Transparent),
                Padding = new Thickness(3, 1, 3, 1),
                Child = new TextBlock
                {
                    Text = $"{relation.SourceColumns.FirstOrDefault()} → {relation.TargetColumns.FirstOrDefault()}",
                    FontSize = 9,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 190,
                    ToolTip = $"{relation.Kind}: {relation.Rule}"
                }
            };
            ((TextBlock)label.Child).SetResourceReference(TextBlock.ForegroundProperty, "BrushTextSecondary");
            Canvas.SetLeft(label, (a.X + b.X) / 2 - 90);
            Canvas.SetTop(label, (a.Y + b.Y) / 2 - 12);
            CompactGraphCanvas.Children.Add(label);
        }

        AddCompactNode(selected, positions[selected.FullName], true);
        foreach (var neighbor in neighbors)
            AddCompactNode(neighbor, positions[neighbor.FullName], false);

        int hidden = Math.Max(0, _tables.Count - 1 - neighbors.Count);
        TxtGraphScope.Text = hidden == 0
            ? $"Показана выбранная таблица и {neighbors.Count} непосредственных связей"
            : $"Показана выбранная таблица и {neighbors.Count} непосредственных связей · ещё {hidden} таблиц доступны слева";
    }

    private bool HasRelationDirection(GraphTable neighbor, GraphTable selected, bool incoming)
    {
        return _relations.Any(r =>
            incoming
                ? r.Source.Equals(neighbor.FullName, StringComparison.OrdinalIgnoreCase) && r.Target.Equals(selected.FullName, StringComparison.OrdinalIgnoreCase)
                : r.Target.Equals(neighbor.FullName, StringComparison.OrdinalIgnoreCase) && r.Source.Equals(selected.FullName, StringComparison.OrdinalIgnoreCase));
    }

    private static void PlaceColumn(IReadOnlyList<GraphTable> tables, double x, double startY, IDictionary<string, Point> positions)
    {
        if (tables.Count == 0) return;
        double total = (tables.Count - 1) * 105;
        double first = 430 - total / 2;
        for (int i = 0; i < tables.Count; i++)
            positions[tables[i].FullName] = new Point(x, first + i * 105);
    }

    private void AddCompactNode(GraphTable table, Point point, bool selected)
    {
        var border = new Border
        {
            Width = 250,
            Height = 88,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(11),
            Cursor = Cursors.Hand,
            ToolTip = BuildTableToolTip(table)
        };
        border.SetResourceReference(Border.BackgroundProperty, selected ? "BrushSelected" : "BrushPanel");
        border.SetResourceReference(Border.BorderBrushProperty, selected ? "BrushAccent" : "BrushBorder");
        border.BorderThickness = new Thickness(selected ? 2 : 1);
        var stack = new StackPanel();
        var title = new TextBlock { Text = table.Name, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        title.SetResourceReference(TextBlock.ForegroundProperty, "BrushText");
        stack.Children.Add(title);
        var meta = new TextBlock
        {
            Text = table.PrimaryKeyColumns.Count == 0 ? $"{table.Columns.Count} полей · PK не объявлен" : $"{table.Columns.Count} полей · PK: {string.Join(", ", table.PrimaryKeyColumns)}",
            FontSize = 9,
            Margin = new Thickness(0, 4, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        meta.SetResourceReference(TextBlock.ForegroundProperty, "BrushTextSecondary");
        stack.Children.Add(meta);
        border.Child = stack;
        border.MouseLeftButtonDown += (_, _) =>
        {
            _selectedTable = table.FullName;
            GraphTableList.SelectedItem = table;
            RenderCompactGraph();
        };
        Canvas.SetLeft(border, point.X - 125);
        Canvas.SetTop(border, point.Y - 44);
        CompactGraphCanvas.Children.Add(border);
    }
}

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Data.SqlClient;
using Configurator.Infrastructure.Database;

namespace Configurator;

public partial class DatabaseGraphView : UserControl
{
    private ConnectionSettings _connection;
    private string _database = "";
    private readonly List<GraphTable> _tables = new();
    private readonly List<GraphRelation> _relations = new();
    private string _selectedTable;

    public DatabaseGraphView()
    {
        InitializeComponent();
    }

    public void Configure(ConnectionSettings connection, string database)
    {
        _connection = connection;
        _database = database ?? string.Empty;
        _selectedTable = null;
        _ = LoadGraphAsync();
    }

    public async Task RefreshAsync()
    {
        await LoadGraphAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadGraphAsync();
    }

    private async Task LoadGraphAsync()
    {
        if (_connection == null || string.IsNullOrWhiteSpace(_database))
        {
            ClearGraph("Выберите БД проекта для построения графа.");
            return;
        }

        try
        {
            TxtGraphStatus.Text = "Читаем структуру БД...";
            var connectionString = _connection.ToConnectionString(_database);
            if (!await _connection.TestConnectionAsync(_database))
            {
                ClearGraph($"Связь с БД «{_database}» потеряна.");
                return;
            }

            var loaded = await Task.Run(() => ReadSchema(connectionString));
            _tables.Clear();
            _tables.AddRange(loaded.tables);
            _relations.Clear();
            _relations.AddRange(loaded.relations);

            RenderGraph();
            TxtGraphStatus.Text = $"Таблиц: {_tables.Count} · связей: {_relations.Count} · физические FK и обнаруженные логические связи";
        }
        catch (Exception ex)
        {
            ClearGraph($"Не удалось построить граф: {ex.Message}");
        }
    }

    private void ClearGraph(string message)
    {
        _tables.Clear();
        _relations.Clear();
        GraphCanvas.Children.Clear();
        TxtSelectedTable.Text = "все таблицы";
        TxtGraphStatus.Text = message;
    }

    private static (List<GraphTable> tables, List<GraphRelation> relations) ReadSchema(string cs)
    {
        var tables = new Dictionary<string, GraphTable>(StringComparer.OrdinalIgnoreCase);
        var relations = new Dictionary<string, GraphRelation>(StringComparer.OrdinalIgnoreCase);

        using var connection = new SqlConnection(cs);
        connection.Open();

        using (var command = new SqlCommand(@"
SELECT t.TABLE_SCHEMA, t.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE,
       CASE WHEN EXISTS (
           SELECT 1 FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE k
           WHERE k.TABLE_SCHEMA=t.TABLE_SCHEMA AND k.TABLE_NAME=t.TABLE_NAME
             AND k.COLUMN_NAME=c.COLUMN_NAME
             AND OBJECTPROPERTY(OBJECT_ID(k.CONSTRAINT_SCHEMA + '.' + QUOTENAME(k.CONSTRAINT_NAME)), 'IsPrimaryKey') = 1
       ) THEN 1 ELSE 0 END AS IsPrimary
FROM INFORMATION_SCHEMA.TABLES t
JOIN INFORMATION_SCHEMA.COLUMNS c ON c.TABLE_SCHEMA=t.TABLE_SCHEMA AND c.TABLE_NAME=t.TABLE_NAME
WHERE t.TABLE_TYPE='BASE TABLE'
ORDER BY t.TABLE_NAME, c.ORDINAL_POSITION;", connection))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var schema = Convert.ToString(reader["TABLE_SCHEMA"]) ?? "dbo";
                var name = Convert.ToString(reader["TABLE_NAME"]) ?? "";
                var key = schema + "." + name;
                if (!tables.TryGetValue(key, out var table))
                {
                    table = new GraphTable { FullName = key, Name = name };
                    tables[key] = table;
                }
                table.Columns.Add(Convert.ToString(reader["COLUMN_NAME"]) ?? "");
                if (Convert.ToInt32(reader["IsPrimary"]) == 1)
                    table.PrimaryKeyColumns.Add(Convert.ToString(reader["COLUMN_NAME"]) ?? "");
            }
        }

        using (var command = new SqlCommand(@"
SELECT sch1.name AS SourceSchema, tab1.name AS SourceTable,
       col1.name AS SourceColumn,
       sch2.name AS TargetSchema, tab2.name AS TargetTable,
       col2.name AS TargetColumn, fk.name AS ForeignKeyName
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id
JOIN sys.tables tab1 ON tab1.object_id=fkc.parent_object_id
JOIN sys.schemas sch1 ON sch1.schema_id=tab1.schema_id
JOIN sys.columns col1 ON col1.object_id=fkc.parent_object_id AND col1.column_id=fkc.parent_column_id
JOIN sys.tables tab2 ON tab2.object_id=fkc.referenced_object_id
JOIN sys.schemas sch2 ON sch2.schema_id=tab2.schema_id
JOIN sys.columns col2 ON col2.object_id=fkc.referenced_object_id AND col2.column_id=fkc.referenced_column_id
WHERE tab1.is_ms_shipped=0 AND tab2.is_ms_shipped=0
ORDER BY SourceTable, ForeignKeyName, fkc.constraint_column_id;", connection))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var source = Convert.ToString(reader["SourceSchema"]) + "." + Convert.ToString(reader["SourceTable"]);
                var target = Convert.ToString(reader["TargetSchema"]) + "." + Convert.ToString(reader["TargetTable"]);
                var fk = Convert.ToString(reader["ForeignKeyName"]) ?? "FK";
                var id = "FK|" + fk + "|" + source + "|" + target;
                if (!relations.TryGetValue(id, out var relation))
                {
                    relation = new GraphRelation { Source = source, Target = target, Kind = "FK", Rule = fk };
                    relations[id] = relation;
                }
                relation.SourceColumns.Add(Convert.ToString(reader["SourceColumn"]) ?? "");
                relation.TargetColumns.Add(Convert.ToString(reader["TargetColumn"]) ?? "");
            }
        }

        AddKnownLogicalRelations(tables.Values.ToList(), relations);
        return (tables.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList(), relations.Values.ToList());
    }

    private static void AddKnownLogicalRelations(IEnumerable<GraphTable> tables, IDictionary<string, GraphRelation> relations)
    {
        var list = tables.ToList();
        foreach (var source in list)
        {
            AddColumnLookup(list, relations, source, "PLC", "PLC", "Record", "PLC");
            AddColumnLookup(list, relations, source, "Area", "Areas", "AreaName", "Area");
            AddColumnLookup(list, relations, source, "Unit", "Units", "Name", "Unit");
            AddColumnLookup(list, relations, source, "Class", "Classes", "Record", "Class");
            AddColumnLookup(list, relations, source, "PLC_ClasssNumber", "PLC_ClasssNumber", "Record", "PLC_ClasssNumber");
            AddColumnLookup(list, relations, source, "PLCClassNumber", "PLCClassNumber", "Record", "PLCClassNumber");
            AddColumnLookup(list, relations, source, "PLC_ClassNumber", "PLC_ClassNumber", "Record", "PLC_ClassNumber");

            var typeTarget = list.FirstOrDefault(x =>
                x.Name.Equals(source.Name + "Type", StringComparison.OrdinalIgnoreCase) ||
                x.Name.Equals(source.Name + "Types", StringComparison.OrdinalIgnoreCase));
            if (typeTarget != null && source.Has("Type") && typeTarget.Has("Record"))
                AddRelation(relations, source, typeTarget, new[] { "Type" }, new[] { "Record" }, "LOGICAL", "Type lookup");
        }

        foreach (var sourceName in new[] { "Actions", "Events" })
        {
            var source = list.FirstOrDefault(x => x.Name.Equals(sourceName, StringComparison.OrdinalIgnoreCase));
            var target = list.FirstOrDefault(x => x.Name.Equals("Classes", StringComparison.OrdinalIgnoreCase));
            if (source != null && target != null && source.Has("Class") && target.Has("Record"))
                AddRelation(relations, source, target, new[] { "Class" }, new[] { "Record" }, "LOGICAL", "Class lookup");
        }
    }

    private static void AddColumnLookup(List<GraphTable> tables, IDictionary<string, GraphRelation> relations, GraphTable source, string sourceColumn, string targetTableName, string targetColumn, string rule)
    {
        var target = tables.FirstOrDefault(x => x.Name.Equals(targetTableName, StringComparison.OrdinalIgnoreCase));
        if (target == null || ReferenceEquals(source, target) || !source.Has(sourceColumn) || !target.Has(targetColumn)) return;
        AddRelation(relations, source, target, new[] { sourceColumn }, new[] { targetColumn }, "LOGICAL", rule);
    }

    private static void AddRelation(IDictionary<string, GraphRelation> relations, GraphTable source, GraphTable target, IEnumerable<string> sourceColumns, IEnumerable<string> targetColumns, string kind, string rule)
    {
        var sourceList = sourceColumns.ToList();
        var targetList = targetColumns.ToList();
        var id = kind + "|" + source.FullName + "|" + target.FullName + "|" + string.Join(",", sourceList) + "|" + string.Join(",", targetList);
        if (relations.ContainsKey(id)) return;
        relations[id] = new GraphRelation { Source = source.FullName, Target = target.FullName, Kind = kind, Rule = rule, SourceColumns = sourceList, TargetColumns = targetList };
    }

    private void RenderGraph()
    {
        GraphCanvas.Children.Clear();
        if (_tables.Count == 0) return;

        const double nodeWidth = 175;
        const double nodeHeight = 68;
        var centerX = 900d;
        var centerY = 600d;
        var radius = Math.Max(280d, Math.Min(500d, 65d * Math.Sqrt(_tables.Count)));

        var positions = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
        var ordered = _tables.ToList();
        if (!string.IsNullOrWhiteSpace(_selectedTable))
        {
            var selected = ordered.FirstOrDefault(x => x.FullName.Equals(_selectedTable, StringComparison.OrdinalIgnoreCase));
            if (selected != null)
            {
                ordered.Remove(selected);
                ordered.Insert(0, selected);
            }
        }

        for (int i = 0; i < ordered.Count; i++)
        {
            if (i == 0 && !string.IsNullOrWhiteSpace(_selectedTable))
            {
                positions[ordered[i].FullName] = new Point(centerX, centerY);
                continue;
            }
            int index = string.IsNullOrWhiteSpace(_selectedTable) ? i : i - 1;
            int count = string.IsNullOrWhiteSpace(_selectedTable) ? ordered.Count : Math.Max(1, ordered.Count - 1);
            var angle = -Math.PI / 2 + (2 * Math.PI * index / count);
            positions[ordered[i].FullName] = new Point(centerX + radius * Math.Cos(angle), centerY + radius * Math.Sin(angle));
        }

        foreach (var relation in _relations)
        {
            if (!positions.TryGetValue(relation.Source, out var a) || !positions.TryGetValue(relation.Target, out var b)) continue;
            var selected = !string.IsNullOrWhiteSpace(_selectedTable) &&
                           (relation.Source.Equals(_selectedTable, StringComparison.OrdinalIgnoreCase) || relation.Target.Equals(_selectedTable, StringComparison.OrdinalIgnoreCase));
            var line = new Line
            {
                X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y,
                StrokeThickness = selected ? 2.5 : 1.0,
                Opacity = selected ? 1.0 : 0.35
            };
            line.SetResourceReference(Shape.StrokeProperty, selected ? "BrushAccent" : "BrushBorder");
            GraphCanvas.Children.Add(line);

            if (selected || string.IsNullOrWhiteSpace(_selectedTable))
            {
                var label = new TextBlock
                {
                    Text = $"{string.Join(",", relation.SourceColumns)} → {string.Join(",", relation.TargetColumns)}",
                    FontSize = 9,
                    MaxWidth = 170,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = $"{relation.Kind}: {relation.Rule}"
                };
                label.SetResourceReference(TextBlock.ForegroundProperty, "BrushTextSecondary");
                Canvas.SetLeft(label, (a.X + b.X) / 2 - 80);
                Canvas.SetTop(label, (a.Y + b.Y) / 2 - 8);
                GraphCanvas.Children.Add(label);
            }
        }

        foreach (var table in ordered)
        {
            var point = positions[table.FullName];
            var border = new Border
            {
                Width = nodeWidth,
                Height = nodeHeight,
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(8),
                Cursor = Cursors.Hand,
                ToolTip = BuildTableToolTip(table)
            };
            border.SetResourceReference(Border.BackgroundProperty, table.FullName.Equals(_selectedTable, StringComparison.OrdinalIgnoreCase) ? "BrushSelected" : "BrushPanel");
            border.SetResourceReference(Border.BorderBrushProperty, table.FullName.Equals(_selectedTable, StringComparison.OrdinalIgnoreCase) ? "BrushAccent" : "BrushBorder");
            border.BorderThickness = new Thickness(table.FullName.Equals(_selectedTable, StringComparison.OrdinalIgnoreCase) ? 2 : 1);
            var stack = new StackPanel();
            var name = new TextBlock { Text = table.Name, FontWeight = FontWeights.SemiBold, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
            name.SetResourceReference(TextBlock.ForegroundProperty, "BrushText");
            stack.Children.Add(name);
            var key = new TextBlock { Text = table.PrimaryKeyColumns.Count == 0 ? "без PK" : "PK: " + string.Join(", ", table.PrimaryKeyColumns), FontSize = 9, TextTrimming = TextTrimming.CharacterEllipsis };
            key.SetResourceReference(TextBlock.ForegroundProperty, "BrushTextSecondary");
            stack.Children.Add(key);
            var cols = new TextBlock { Text = $"полей: {table.Columns.Count}", FontSize = 9 };
            cols.SetResourceReference(TextBlock.ForegroundProperty, "BrushTextSecondary");
            stack.Children.Add(cols);
            border.Child = stack;
            border.MouseLeftButtonDown += (_, __) =>
            {
                _selectedTable = table.FullName;
                TxtSelectedTable.Text = table.Name;
                RenderGraph();
            };
            Canvas.SetLeft(border, point.X - nodeWidth / 2);
            Canvas.SetTop(border, point.Y - nodeHeight / 2);
            GraphCanvas.Children.Add(border);
        }
    }

    private static string BuildTableToolTip(GraphTable table)
        => $"{table.FullName}\n{(table.PrimaryKeyColumns.Count == 0 ? "PK: отсутствует" : "PK: " + string.Join(", ", table.PrimaryKeyColumns))}\n\nПоля:\n{string.Join(", ", table.Columns)}";

    private sealed class GraphTable
    {
        public string FullName { get; init; } = "";
        public string Name { get; init; } = "";
        public List<string> Columns { get; } = new();
        public List<string> PrimaryKeyColumns { get; } = new();
        public bool Has(string column) => Columns.Any(x => x.Equals(column, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class GraphRelation
    {
        public string Source { get; init; } = "";
        public string Target { get; init; } = "";
        public string Kind { get; init; } = "";
        public string Rule { get; init; } = "";
        public List<string> SourceColumns { get; init; } = new();
        public List<string> TargetColumns { get; init; } = new();
    }
}

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

    public DatabaseGraphView() => InitializeComponent();

    public void Configure(ConnectionSettings connection, string database)
    {
        _connection = connection;
        _database = database ?? string.Empty;
        _selectedTable = null;
        _ = LoadGraphAsync();
    }

    public async Task RefreshAsync() => await LoadGraphAsync();

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadGraphAsync();

    private async Task LoadGraphAsync()
    {
        if (_connection == null || string.IsNullOrWhiteSpace(_database))
        {
            ClearGraph("Выберите БД проекта для построения графа.");
            return;
        }

        try
        {
            TxtGraphStatus.Text = $"Читаем структуру БД «{_database}»...";
            var cs = _connection.ToConnectionString(_database);
            if (!await _connection.TestConnectionAsync(_database))
            {
                ClearGraph($"Связь с БД «{_database}» потеряна.");
                return;
            }

            var loaded = await Task.Run(() => ReadSchema(cs));
            _tables.Clear();
            _tables.AddRange(loaded.tables);
            _relations.Clear();
            _relations.AddRange(loaded.relations);
            RenderGraph();

            int physical = _relations.Count(x => x.Kind.Equals("FK", StringComparison.OrdinalIgnoreCase));
            int logical = _relations.Count - physical;
            TxtGraphStatus.Text = $"Таблиц: {_tables.Count} · связей: {_relations.Count} · PK/FK: {physical} · логических: {logical}";
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
                var column = Convert.ToString(reader["COLUMN_NAME"]) ?? "";
                table.Columns.Add(column);
                if (Convert.ToInt32(reader["IsPrimary"]) == 1) table.PrimaryKeyColumns.Add(column);
            }
        }

        using (var command = new SqlCommand(@"
SELECT sch1.name AS SourceSchema, tab1.name AS SourceTable, col1.name AS SourceColumn,
       sch2.name AS TargetSchema, tab2.name AS TargetTable, col2.name AS TargetColumn, fk.name AS ForeignKeyName
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

    // FK/PK are only one source of truth. Many historical project databases lost
    // constraints during import/export, so these rules deliberately describe the
    // logical dependencies observed in the project family without creating SQL FKs.
    private static void AddKnownLogicalRelations(IReadOnlyList<GraphTable> tables, IDictionary<string, GraphRelation> relations)
    {
        AddClassLinks(tables, relations);
        AddCommonEntityLinks(tables, relations);
        AddPlcTypeLink(tables, relations);
        AddTypeLookups(tables, relations);
        AddProgramLinks(tables, relations);
    }

    private static void AddClassLinks(IReadOnlyList<GraphTable> tables, IDictionary<string, GraphRelation> relations)
    {
        var classes = Find(tables, "Classes");
        if (classes == null) return;
        foreach (var name in new[] { "Actions", "Events" })
        {
            var source = Find(tables, name);
            if (source != null && Has(source, "Class") && HasAny(classes, "Record", "ID", "Id"))
                AddRelation(relations, source, classes, new[] { "Class" }, new[] { FirstExisting(classes, "Record", "ID", "Id") }, "LOGICAL", "Class lookup");
        }
    }

    private static void AddCommonEntityLinks(IReadOnlyList<GraphTable> tables, IDictionary<string, GraphRelation> relations)
    {
        foreach (var source in tables)
        {
            if (source.Name.Equals("PLC", StringComparison.OrdinalIgnoreCase) || source.Name.Equals("Classes", StringComparison.OrdinalIgnoreCase)) continue;
            AddSemanticLookup(tables, relations, source, "PLC", new[] { "PLC" }, new[] { "Record", "ID", "Id", "PLC" }, "PLC lookup");
            AddSemanticLookup(tables, relations, source, "Area", new[] { "Area" }, new[] { "Record", "ID", "Id", "AreaName" }, "Area lookup");
            AddSemanticLookup(tables, relations, source, "Unit", new[] { "Unit" }, new[] { "Record", "ID", "Id", "Name" }, "Unit lookup");
            AddSemanticLookup(tables, relations, source, "PLCClasssNumber", new[] { "PLCClasssNumber", "PLC_ClasssNumber", "PLCClassNumber", "PLC_ClassNumber" }, new[] { "Record", "ID", "Id", "PLCClasssNumber", "PLCClassNumber" }, "PLC class number lookup");
        }
    }

    private static void AddPlcTypeLink(IReadOnlyList<GraphTable> tables, IDictionary<string, GraphRelation> relations)
    {
        var plc = Find(tables, "PLC");
        if (plc == null) return;
        var target = FindAny(tables, "PLCTypes", "PLCType", "PlcTypes");
        if (target == null) return;
        var sourceColumn = FirstExisting(plc, "Type", "PLCType", "Type_Record");
        var targetColumn = FirstExisting(target, "Record", "ID", "Id", "Type");
        if (sourceColumn != null && targetColumn != null)
            AddRelation(relations, plc, target, new[] { sourceColumn }, new[] { targetColumn }, "LOGICAL", "PLC type lookup");
    }

    private static void AddTypeLookups(IReadOnlyList<GraphTable> tables, IDictionary<string, GraphRelation> relations)
    {
        foreach (var source in tables)
        {
            if (!Has(source, "Type")) continue;
            var candidates = new[]
            {
                source.Name + "Type", source.Name + "Types",
                source.Name.TrimEnd('s') + "Type", source.Name.TrimEnd('s') + "Types"
            };
            var target = candidates.Select(x => Find(tables, x)).FirstOrDefault(x => x != null);
            if (target == null || ReferenceEquals(source, target)) continue;
            var targetColumn = FirstExisting(target, "Record", "ID", "Id", "Type", "Name");
            if (targetColumn != null)
                AddRelation(relations, source, target, new[] { "Type" }, new[] { targetColumn }, "LOGICAL", "Type lookup");
        }
    }

    private static void AddProgramLinks(IReadOnlyList<GraphTable> tables, IDictionary<string, GraphRelation> relations)
    {
        AddChild(tables, relations, "Prog_Recipe", "Recipe_Record", "Prog", "Record", "Program recipe");
        AddChild(tables, relations, "Prog_Recipes", "Recipe_Record", "Prog_Recipe", "Record", "Program recipe values");
        AddChild(tables, relations, "Prog_QueueSelections", "Queue_Record", "Prog_Queue", "Record", "Program queue");
        AddChild(tables, relations, "Prog_QueueSelections", "Status_Record", "Statuses", "Record", "Program status");
        AddChild(tables, relations, "Prog_QueueSelections", "Matrix_Record", "Matrix_List", "Record", "Program matrix");
        AddChild(tables, relations, "Prog_QueueSelections", "Seq_Record", "Prog_Seq", "Record", "Program sequence");
    }

    private static void AddChild(IReadOnlyList<GraphTable> tables, IDictionary<string, GraphRelation> relations, string sourceName, string sourceColumn, string targetName, string targetColumn, string rule)
    {
        var source = Find(tables, sourceName);
        var target = Find(tables, targetName);
        if (source != null && target != null && Has(source, sourceColumn) && Has(target, targetColumn))
            AddRelation(relations, source, target, new[] { sourceColumn }, new[] { targetColumn }, "LOGICAL", rule);
    }

    private static void AddSemanticLookup(IReadOnlyList<GraphTable> tables, IDictionary<string, GraphRelation> relations, GraphTable source, string semantic, string[] sourceColumns, string[] targetColumns, string rule)
    {
        var target = semantic.Equals("PLC", StringComparison.OrdinalIgnoreCase) ? FindAny(tables, "PLC")
            : semantic.Equals("Area", StringComparison.OrdinalIgnoreCase) ? FindAny(tables, "Areas", "Area")
            : semantic.Equals("Unit", StringComparison.OrdinalIgnoreCase) ? FindAny(tables, "Units", "Unit")
            : FindAny(tables, "PLCClasssNumbers", "PLCClasssNumber", "PLCClassNumbers", "PLCClassNumber");
        if (target == null || ReferenceEquals(source, target)) return;
        var src = sourceColumns.FirstOrDefault(x => Has(source, x));
        var dst = targetColumns.FirstOrDefault(x => Has(target, x));
        if (src != null && dst != null) AddRelation(relations, source, target, new[] { src }, new[] { dst }, "LOGICAL", rule);
    }

    private static GraphTable Find(IEnumerable<GraphTable> tables, string name) => tables.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    private static GraphTable FindAny(IEnumerable<GraphTable> tables, params string[] names) => names.Select(n => Find(tables, n)).FirstOrDefault(x => x != null);
    private static bool Has(GraphTable table, string column) => table?.Columns.Any(x => x.Equals(column, StringComparison.OrdinalIgnoreCase)) == true;
    private static bool HasAny(GraphTable table, params string[] columns) => columns.Any(x => Has(table, x));
    private static string FirstExisting(GraphTable table, params string[] columns) => columns.FirstOrDefault(x => Has(table, x));

    private static void AddRelation(IDictionary<string, GraphRelation> relations, GraphTable source, GraphTable target, IEnumerable<string> sourceColumns, IEnumerable<string> targetColumns, string kind, string rule)
    {
        var src = sourceColumns.ToList();
        var dst = targetColumns.ToList();
        var id = kind + "|" + source.FullName + "|" + target.FullName + "|" + string.Join(",", src) + "|" + string.Join(",", dst);
        if (relations.ContainsKey(id)) return;
        relations[id] = new GraphRelation { Source = source.FullName, Target = target.FullName, Kind = kind, Rule = rule, SourceColumns = src, TargetColumns = dst };
    }

    private void RenderGraph()
    {
        GraphCanvas.Children.Clear();
        if (_tables.Count == 0) return;
        const double nodeWidth = 190, nodeHeight = 76;
        var centerX = 1000d;
        var centerY = 680d;
        var radius = Math.Max(300d, Math.Min(570d, 70d * Math.Sqrt(_tables.Count)));
        var positions = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
        var ordered = _tables.ToList();
        if (!string.IsNullOrWhiteSpace(_selectedTable))
        {
            var selected = ordered.FirstOrDefault(x => x.FullName.Equals(_selectedTable, StringComparison.OrdinalIgnoreCase));
            if (selected != null) { ordered.Remove(selected); ordered.Insert(0, selected); }
        }
        for (int i = 0; i < ordered.Count; i++)
        {
            if (i == 0 && !string.IsNullOrWhiteSpace(_selectedTable)) { positions[ordered[i].FullName] = new Point(centerX, centerY); continue; }
            int index = string.IsNullOrWhiteSpace(_selectedTable) ? i : i - 1;
            int count = string.IsNullOrWhiteSpace(_selectedTable) ? ordered.Count : Math.Max(1, ordered.Count - 1);
            var angle = -Math.PI / 2 + (2 * Math.PI * index / count);
            positions[ordered[i].FullName] = new Point(centerX + radius * Math.Cos(angle), centerY + radius * Math.Sin(angle));
        }

        foreach (var relation in _relations)
        {
            if (!positions.TryGetValue(relation.Source, out var a) || !positions.TryGetValue(relation.Target, out var b)) continue;
            bool selected = !string.IsNullOrWhiteSpace(_selectedTable) && (relation.Source.Equals(_selectedTable, StringComparison.OrdinalIgnoreCase) || relation.Target.Equals(_selectedTable, StringComparison.OrdinalIgnoreCase));
            var line = new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, StrokeThickness = selected ? 2.5 : 1.2, Opacity = selected ? 1 : 0.5 };
            line.SetResourceReference(Shape.StrokeProperty, relation.Kind.Equals("FK", StringComparison.OrdinalIgnoreCase) ? "BrushAccent" : "BrushTextSecondary");
            if (!relation.Kind.Equals("FK", StringComparison.OrdinalIgnoreCase)) line.StrokeDashArray = new DoubleCollection { 5, 3 };
            GraphCanvas.Children.Add(line);
            if (selected || string.IsNullOrWhiteSpace(_selectedTable))
            {
                var label = new TextBlock { Text = $"{relation.SourceColumns.FirstOrDefault()} → {relation.TargetColumns.FirstOrDefault()}", FontSize = 9, MaxWidth = 180, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = $"{relation.Kind}: {relation.Rule}" };
                label.SetResourceReference(TextBlock.ForegroundProperty, "BrushTextSecondary");
                Canvas.SetLeft(label, (a.X + b.X) / 2 - 80);
                Canvas.SetTop(label, (a.Y + b.Y) / 2 - 8);
                GraphCanvas.Children.Add(label);
            }
        }

        foreach (var table in ordered)
        {
            var point = positions[table.FullName];
            bool isSelected = table.FullName.Equals(_selectedTable, StringComparison.OrdinalIgnoreCase);
            var border = new Border { Width = nodeWidth, Height = nodeHeight, CornerRadius = new CornerRadius(9), Padding = new Thickness(8), Cursor = Cursors.Hand, ToolTip = BuildTableToolTip(table) };
            border.SetResourceReference(Border.BackgroundProperty, isSelected ? "BrushSelected" : "BrushPanel");
            border.SetResourceReference(Border.BorderBrushProperty, isSelected ? "BrushAccent" : "BrushBorder");
            border.BorderThickness = new Thickness(isSelected ? 2 : 1);
            var stack = new StackPanel();
            var name = new TextBlock { Text = table.Name, FontWeight = FontWeights.SemiBold, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
            name.SetResourceReference(TextBlock.ForegroundProperty, "BrushText");
            stack.Children.Add(name);
            var key = new TextBlock { Text = table.PrimaryKeyColumns.Count == 0 ? "PK не объявлен" : "PK: " + string.Join(", ", table.PrimaryKeyColumns), FontSize = 9, TextTrimming = TextTrimming.CharacterEllipsis };
            key.SetResourceReference(TextBlock.ForegroundProperty, "BrushTextSecondary");
            stack.Children.Add(key);
            var cols = new TextBlock { Text = $"полей: {table.Columns.Count}", FontSize = 9 };
            cols.SetResourceReference(TextBlock.ForegroundProperty, "BrushTextSecondary");
            stack.Children.Add(cols);
            border.Child = stack;
            border.MouseLeftButtonDown += (_, __) => { _selectedTable = table.FullName; TxtSelectedTable.Text = table.Name; RenderGraph(); };
            Canvas.SetLeft(border, point.X - nodeWidth / 2);
            Canvas.SetTop(border, point.Y - nodeHeight / 2);
            GraphCanvas.Children.Add(border);
        }
    }

    private static string BuildTableToolTip(GraphTable table) => $"{table.FullName}\n{(table.PrimaryKeyColumns.Count == 0 ? "PK: не объявлен" : "PK: " + string.Join(", ", table.PrimaryKeyColumns))}\n\nПоля:\n{string.Join(", ", table.Columns)}";

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

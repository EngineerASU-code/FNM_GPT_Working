using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Configurator.Core.Architecture;

namespace Configurator.Application.Architecture;

/// <summary>
/// Builds the runtime class catalog from the connected database.
/// Classes are labels; DBName/actual tables/columns come from the project database.
/// </summary>
public sealed class ProjectDatabaseCatalogService
{
    private static readonly Dictionary<string, string[]> FallbackTables = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Valve"] = new[] { "Valves" },
        ["DSV"] = new[] { "Valves2S", "DSV" },
        ["Motor"] = new[] { "Motors" },
        ["DI"] = new[] { "DiscretIN" },
        ["DO"] = new[] { "DiscretOUT" },
        ["AI"] = new[] { "AnaIN" },
        ["AO"] = new[] { "AnaOUT" },
        ["PID"] = new[] { "Reglers" },
        ["Status"] = new[] { "Statuses" },
        ["Program"] = new[] { "Prog" },
        ["Step"] = new[] { "Prog_Step" },
        ["Matrix"] = new[] { "Matrix_List", "Prog_Matrix" }
    };

    private readonly string _connectionString;
    private readonly DatabaseArchitectureAnalyzer _analyzer;

    public ProjectDatabaseCatalogService(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _analyzer = new DatabaseArchitectureAnalyzer(_connectionString);
    }

    public async Task<ProjectDatabaseCatalog> LoadAsync(CancellationToken cancellationToken = default)
    {
        var schema = await _analyzer.AnalyzeAsync(cancellationToken);
        var classes = await LoadClassLabelsAsync(schema, cancellationToken);
        return new ProjectDatabaseCatalog(schema, classes);
    }

    private async Task<List<ClassDefinition>> LoadClassLabelsAsync(DatabaseSchema schema, CancellationToken ct)
    {
        var result = new List<ClassDefinition>();
        var labels = await TryReadClassesTableAsync(schema, ct);

        foreach (var label in labels)
        {
            var table = ResolveTable(schema, label.Name, label.DbName);
            result.Add(BuildDefinition(label.Name, label.ClassNumber, table));
        }

        foreach (var system in SystemClassCatalog.CreateDefaults())
        {
            if (result.Any(x => x.ClassNumber == system.ClassNumber || x.Name.Equals(system.Name, StringComparison.OrdinalIgnoreCase))) continue;
            var table = ResolveTable(schema, system.Name, null);
            if (table != null) result.Add(BuildDefinition(system.Name, system.ClassNumber, table));
        }

        return result
            .OrderBy(x => x.ClassNumber > 0 ? x.ClassNumber : int.MaxValue)
            .ThenBy(x => x.Name)
            .ToList();
    }

    private async Task<List<ClassLabelRow>> TryReadClassesTableAsync(DatabaseSchema schema, CancellationToken ct)
    {
        var table = schema.Tables.FirstOrDefault(t => t.Name.Equals("Classes", StringComparison.OrdinalIgnoreCase));
        if (table == null) return new List<ClassLabelRow>();

        try
        {
            var db = new DatabaseService(_connectionString);
            var columns = table.Columns.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var order = columns.Contains("Record") ? new[] { "Record" } : table.Columns.Take(1).Select(c => c.Name).ToArray();
            var data = await db.GetTableDataPageAsync(table.FullName, 1, 5000, order, null, ct);
            return data.Rows.Cast<DataRow>().Select(row => new ClassLabelRow
            {
                ClassNumber = ReadInt(row, "Record"),
                Name = ReadString(row, "Name", "ClassName", "NameL1") ?? $"Class {ReadInt(row, "Record")}",
                DbName = ReadString(row, "DBName", "TableName", "DBTable")
            }).Where(x => x.ClassNumber > 0 || !string.IsNullOrWhiteSpace(x.Name)).ToList();
        }
        catch
        {
            return new List<ClassLabelRow>();
        }
    }

    private static DatabaseTable ResolveTable(DatabaseSchema schema, string className, string dbName)
    {
        if (!string.IsNullOrWhiteSpace(dbName))
        {
            var exact = schema.Tables.FirstOrDefault(t => t.Name.Equals(dbName, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
        }

        if (FallbackTables.TryGetValue(className, out var candidates))
            foreach (var candidate in candidates)
            {
                var table = schema.Tables.FirstOrDefault(t => t.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase));
                if (table != null) return table;
            }

        return schema.Tables.FirstOrDefault(t => t.Name.Equals(className, StringComparison.OrdinalIgnoreCase));
    }

    private static ClassDefinition BuildDefinition(string name, int classNumber, DatabaseTable table)
    {
        var definition = new ClassDefinition
        {
            Id = "db:" + classNumber + ":" + name,
            Name = name,
            ClassNumber = classNumber,
            Category = name.Equals("Program", StringComparison.OrdinalIgnoreCase) || name.Equals("Step", StringComparison.OrdinalIgnoreCase) || name.Equals("Matrix", StringComparison.OrdinalIgnoreCase) ? "Process" : "Object",
            IsSystem = true,
            Available = table != null,
            Description = "Метка класса, полученная из подключенной БД."
        };

        if (table == null) return definition;
        definition.StorageMappings.Add(new StorageMapping { Role = "primary", TableName = table.FullName, KeyColumns = table.Keys.FirstOrDefault(k => k.IsPrimary)?.Columns ?? new List<string>() });
        foreach (var column in table.Columns)
            definition.Fields.Add(new FieldDefinition
            {
                Id = table.FullName + ":" + column.Name,
                Name = column.Name,
                DataType = column.DataType,
                Nullable = column.IsNullable,
                Description = column.IsComputed ? "Computed" : "Фактическая колонка подключенной БД.",
                Group = "Database"
            });
        return definition;
    }

    private static int ReadInt(DataRow row, string column)
        => row.Table.Columns.Contains(column) && row[column] != DBNull.Value && int.TryParse(Convert.ToString(row[column]), out var value) ? value : 0;

    private static string ReadString(DataRow row, params string[] columns)
    {
        foreach (var column in columns)
            if (row.Table.Columns.Contains(column) && row[column] != DBNull.Value && !string.IsNullOrWhiteSpace(Convert.ToString(row[column])))
                return Convert.ToString(row[column]);
        return null;
    }

    private sealed class ClassLabelRow
    {
        public int ClassNumber { get; init; }
        public string Name { get; init; }
        public string DbName { get; init; }
    }
}

public sealed class ProjectDatabaseCatalog
{
    public ProjectDatabaseCatalog(DatabaseSchema schema, IReadOnlyList<ClassDefinition> classes)
    {
        Schema = schema;
        Classes = classes;
    }

    public DatabaseSchema Schema { get; }
    public IReadOnlyList<ClassDefinition> Classes { get; }
}

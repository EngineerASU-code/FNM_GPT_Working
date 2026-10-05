using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Configurator.Core.Architecture;
using Microsoft.Data.SqlClient;

namespace Configurator;

/// <summary>Builds a project-specific relation graph. It never creates or alters SQL relations.</summary>
public sealed class DatabaseArchitectureAnalyzer
{
    private readonly string _connectionString;
    public DatabaseArchitectureAnalyzer(string connectionString) => _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));

    public async Task<DatabaseSchema> AnalyzeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var schema = new DatabaseSchema { DatabaseName = connection.Database, ServerName = connection.DataSource, AnalyzedAtUtc = DateTimeOffset.UtcNow };
        await LoadTablesAsync(connection, schema, cancellationToken);
        await LoadKeysAsync(connection, schema, cancellationToken);
        await LoadForeignKeysAsync(connection, schema, cancellationToken);
        AddCompositeEntityKeyRelations(schema);
        AddScalarConventionRelations(schema);
        AddPolymorphicRelations(schema);
        return schema;
    }

    private static async Task LoadTablesAsync(SqlConnection connection, DatabaseSchema schema, CancellationToken ct)
    {
        const string sql = @"
SELECT s.name AS SchemaName,t.name AS TableName,c.name AS ColumnName,ty.name AS DataType,
       c.max_length AS MaxLength,c.is_nullable AS IsNullable,c.is_identity AS IsIdentity,
       c.is_computed AS IsComputed,c.column_id AS ColumnId
FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id
WHERE t.is_ms_shipped=0 ORDER BY s.name,t.name,c.column_id;";
        var map = new Dictionary<string, DatabaseTable>(StringComparer.OrdinalIgnoreCase);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var schemaName = Convert.ToString(reader["SchemaName"]) ?? "dbo";
            var tableName = Convert.ToString(reader["TableName"]) ?? "";
            var fullName = $"{schemaName}.{tableName}";
            if (!map.TryGetValue(fullName, out var table))
            {
                table = new DatabaseTable { SchemaName = schemaName, Name = tableName, IsComplexEntity = IsComplexName(tableName) };
                map[fullName] = table; schema.Tables.Add(table);
            }
            table.Columns.Add(new DatabaseColumn
            {
                Name = Convert.ToString(reader["ColumnName"]) ?? "",
                DataType = Convert.ToString(reader["DataType"]) ?? "",
                MaxLength = Convert.ToInt32(reader["MaxLength"]),
                IsNullable = Convert.ToBoolean(reader["IsNullable"]),
                IsIdentity = Convert.ToBoolean(reader["IsIdentity"]),
                IsComputed = Convert.ToBoolean(reader["IsComputed"])
            });
        }
    }

    private static async Task LoadKeysAsync(SqlConnection connection, DatabaseSchema schema, CancellationToken ct)
    {
        const string sql = @"
SELECT s.name AS SchemaName,t.name AS TableName,i.name AS IndexName,i.is_primary_key AS IsPrimary,
       i.is_unique AS IsUnique,c.name AS ColumnName,ic.key_ordinal AS KeyOrdinal
FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
JOIN sys.tables t ON t.object_id=i.object_id JOIN sys.schemas s ON s.schema_id=t.schema_id
WHERE t.is_ms_shipped=0 AND (i.is_primary_key=1 OR i.is_unique=1) AND ic.is_included_column=0
ORDER BY s.name,t.name,i.index_id,ic.key_ordinal;";
        var map = new Dictionary<string, DatabaseKey>(StringComparer.OrdinalIgnoreCase);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var tableName = $"{Convert.ToString(reader["SchemaName"])}.{Convert.ToString(reader["TableName"])}";
            var indexName = Convert.ToString(reader["IndexName"]) ?? "";
            var id = tableName + "\u001f" + indexName;
            if (!map.TryGetValue(id, out var key))
            {
                key = new DatabaseKey { Name = indexName, IsPrimary = Convert.ToBoolean(reader["IsPrimary"]), IsUnique = Convert.ToBoolean(reader["IsUnique"]) };
                map[id] = key; schema.FindTable(tableName)?.Keys.Add(key);
            }
            key.Columns.Add(Convert.ToString(reader["ColumnName"]) ?? "");
        }
    }

    private static async Task LoadForeignKeysAsync(SqlConnection connection, DatabaseSchema schema, CancellationToken ct)
    {
        const string sql = @"
SELECT fk.name AS ConstraintName,ss.name AS SourceSchema,st.name AS SourceTable,sc.name AS SourceColumn,
       ts.name AS TargetSchema,tt.name AS TargetTable,tc.name AS TargetColumn,fkc.constraint_column_id AS KeyOrdinal
FROM sys.foreign_key_columns fkc JOIN sys.foreign_keys fk ON fk.object_id=fkc.constraint_object_id
JOIN sys.tables st ON st.object_id=fkc.parent_object_id JOIN sys.schemas ss ON ss.schema_id=st.schema_id
JOIN sys.columns sc ON sc.object_id=fkc.parent_object_id AND sc.column_id=fkc.parent_column_id
JOIN sys.tables tt ON tt.object_id=fkc.referenced_object_id JOIN sys.schemas ts ON ts.schema_id=tt.schema_id
JOIN sys.columns tc ON tc.object_id=fkc.referenced_object_id AND tc.column_id=fkc.referenced_column_id
ORDER BY fk.name,fkc.constraint_column_id;";
        var map = new Dictionary<string, DatabaseRelation>(StringComparer.OrdinalIgnoreCase);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var name = Convert.ToString(reader["ConstraintName"]) ?? "";
            if (!map.TryGetValue(name, out var relation))
            {
                relation = new DatabaseRelation
                {
                    SourceTable = $"{reader["SourceSchema"]}.{reader["SourceTable"]}",
                    TargetTable = $"{reader["TargetSchema"]}.{reader["TargetTable"]}",
                    Kind = DatabaseRelationKind.PhysicalForeignKey,
                    Confidence = RelationConfidence.Confirmed,
                    RuleId = name,
                    Comment = "Physical SQL Server FK; authoritative."
                };
                map[name] = relation; schema.Relations.Add(relation);
            }
            relation.SourceColumns.Add(Convert.ToString(reader["SourceColumn"]) ?? "");
            relation.TargetColumns.Add(Convert.ToString(reader["TargetColumn"]) ?? "");
        }
    }

    private static void AddCompositeEntityKeyRelations(DatabaseSchema schema)
    {
        var key = new[] { "PLC", "PLC_Class_Number", "Record" };
        foreach (var source in schema.Tables.Where(t => HasColumns(t, key)))
        foreach (var target in schema.Tables.Where(t => !ReferenceEquals(t, source) && HasColumns(t, key) && HasUniqueKey(t, key)))
            schema.Relations.Add(new DatabaseRelation
            {
                SourceTable = source.FullName, TargetTable = target.FullName,
                Kind = DatabaseRelationKind.CompositeEntityKey, Confidence = RelationConfidence.High,
                RuleId = RelationRuleCatalog.EntityKey,
                Comment = "Same PLC + PLC_Class_Number + Record key. No SQL FK is created."
            }.WithColumns(key, key));
    }

    private static void AddScalarConventionRelations(DatabaseSchema schema)
    {
        foreach (var source in schema.Tables)
        {
            AddScalar(schema, source, "PLC", "PLC", "Record", DatabaseRelationKind.ConventionalLookup, RelationConfidence.High, RelationRuleCatalog.PlcRecord, "PLC value resolves to PLC.Record.");
            AddScalar(schema, source, "Class", "Classes", "Record", DatabaseRelationKind.ClassLookup, RelationConfidence.High, RelationRuleCatalog.ClassRecord, "Class value resolves to Classes.Record.");
            AddArea(schema, source); AddUnit(schema, source); AddType(schema, source);
        }
    }

    private static void AddArea(DatabaseSchema schema, DatabaseTable source)
    {
        if (!HasColumn(source, "Area")) return;
        var target = schema.Tables.FirstOrDefault(t => t.Name.Equals("Areas", StringComparison.OrdinalIgnoreCase) && HasColumn(t, "AreaName"));
        if (target == null) return;
        schema.Relations.Add(new DatabaseRelation
        {
            SourceTable = source.FullName, TargetTable = target.FullName, Kind = DatabaseRelationKind.AreaNameLookup,
            Confidence = RelationConfidence.High, RuleId = RelationRuleCatalog.AreaName,
            Comment = "Area stores business value AreaName, not Areas.Record."
        }.WithColumns(new[] { "Area" }, new[] { "AreaName" }));
    }

    private static void AddUnit(DatabaseSchema schema, DatabaseTable source)
    {
        var column = source.Columns.FirstOrDefault(c => c.Name.Equals("Unit", StringComparison.OrdinalIgnoreCase) || c.Name.Equals("Units", StringComparison.OrdinalIgnoreCase));
        var target = schema.Tables.FirstOrDefault(t => t.Name.Equals("Units", StringComparison.OrdinalIgnoreCase) && HasColumn(t, "Unit_ID"));
        if (column == null || target == null) return;
        schema.Relations.Add(new DatabaseRelation
        {
            SourceTable = source.FullName, TargetTable = target.FullName, Kind = DatabaseRelationKind.UnitLookup,
            Confidence = RelationConfidence.Medium, RuleId = RelationRuleCatalog.UnitId,
            Comment = "Convention-based unit reference; unresolved values remain validation warnings."
        }.WithColumns(new[] { column.Name }, new[] { "Unit_ID" }));
    }

    private static void AddType(DatabaseSchema schema, DatabaseTable source)
    {
        if (!HasColumn(source, "Type")) return;
        var candidates = schema.Tables.Where(t => (t.Name.Equals(source.Name + "_Type", StringComparison.OrdinalIgnoreCase) || t.Name.Equals(source.Name + "_Types", StringComparison.OrdinalIgnoreCase)) && HasColumn(t, "Record")).ToList();
        if (candidates.Count != 1) return;
        schema.Relations.Add(new DatabaseRelation
        {
            SourceTable = source.FullName, TargetTable = candidates[0].FullName, Kind = DatabaseRelationKind.ConventionalLookup,
            Confidence = RelationConfidence.Medium, RuleId = RelationRuleCatalog.TypeLookup,
            Comment = "Type lookup inferred from table naming; validate against actual values."
        }.WithColumns(new[] { "Type" }, new[] { "Record" }));
    }

    private static void AddPolymorphicRelations(DatabaseSchema schema)
    {
        var classes = schema.Tables.FirstOrDefault(t => t.Name.Equals("Classes", StringComparison.OrdinalIgnoreCase));
        if (classes == null || !HasColumns(classes, new[] { "Record", "DBName" })) return;
        foreach (var table in schema.Tables.Where(t => HasColumn(t, "Device_Global_Class") && HasColumns(t, new[] { "Device_PLC", "Device_PLC_Class_Number", "Device_Record" })))
            schema.Relations.Add(new DatabaseRelation
            {
                SourceTable = table.FullName, TargetTable = classes.FullName, Kind = DatabaseRelationKind.PolymorphicClassReference,
                Confidence = RelationConfidence.High, RuleId = RelationRuleCatalog.PolymorphicClass,
                Comment = "Device_Global_Class resolves to Classes.Record; Classes.DBName selects concrete target table."
            }.WithColumns(new[] { "Device_Global_Class" }, new[] { "Record" }));
    }

    private static bool IsComplexName(string tableName) => tableName.StartsWith("Prog", StringComparison.OrdinalIgnoreCase) || tableName.StartsWith("Matrix", StringComparison.OrdinalIgnoreCase);
    private static bool HasColumn(DatabaseTable table, string name) => table.Columns.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    private static bool HasColumns(DatabaseTable table, IEnumerable<string> names) => names.All(n => HasColumn(table, n));
    private static bool HasUniqueKey(DatabaseTable table, IEnumerable<string> columns) => table.Keys.Any(k => k.IsUnique && k.Columns.SequenceEqual(columns, StringComparer.OrdinalIgnoreCase));
    private static void AddScalar(DatabaseSchema schema, DatabaseTable source, string sourceColumn, string targetName, string targetColumn, DatabaseRelationKind kind, RelationConfidence confidence, string ruleId, string comment)
    {
        if (!HasColumn(source, sourceColumn)) return;
        var targets = schema.Tables.Where(t => t.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase) && HasColumn(t, targetColumn)).ToList();
        if (targets.Count != 1) return;
        schema.Relations.Add(new DatabaseRelation { SourceTable = source.FullName, TargetTable = targets[0].FullName, Kind = kind, Confidence = confidence, RuleId = ruleId, Comment = comment }.WithColumns(new[] { sourceColumn }, new[] { targetColumn }));
    }
}

internal static class DatabaseRelationExtensions
{
    public static DatabaseRelation WithColumns(this DatabaseRelation relation, IEnumerable<string> source, IEnumerable<string> target)
    {
        relation.SourceColumns.AddRange(source); relation.TargetColumns.AddRange(target); return relation;
    }
}

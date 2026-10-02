using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Configurator.Core.Architecture;

namespace Configurator.Infrastructure.Database;

public sealed class ClassArchitectureReader
{
    private readonly Configurator.DatabaseService _database;

    public ClassArchitectureReader(string connectionString)
    {
        _database = new Configurator.DatabaseService(connectionString);
    }

    public async Task<List<FieldDefinition>> LoadFieldsAsync(string tableName, string className = "", CancellationToken ct = default)
    {
        var columns = await _database.GetTableColumnsAsync(tableName, ct);
        return columns.Select(c => new FieldDefinition
        {
            Name = c.Name,
            DataType = c.DataType,
            Size = c.MaxLength < 0 ? 0 : c.MaxLength,
            Nullable = c.IsNullable,
            DefaultValue = c.HasDefault ? "DEFAULT" : "",
            Description = c.IsIdentity ? "IDENTITY" : c.IsComputed ? "COMPUTED" : "",
            Group = FieldGroupCatalog.Resolve(className, c.Name)
        }).ToList();
    }

    public async Task<DataTable> ReadRowsAsync(string tableName, int take = 500, CancellationToken ct = default)
    {
        var columns = await _database.GetTableColumnsAsync(tableName, ct);
        if (columns.Count == 0) return new DataTable();
        var order = columns.Take(3).Select(c => c.Name).ToList();
        return await _database.GetTableDataPageAsync(tableName, 1, Math.Clamp(take, 1, 2000), order, null, ct);
    }

    public async Task<bool> ExistsAsync(string tableName, CancellationToken ct = default)
    {
        try
        {
            var fields = await LoadFieldsAsync(tableName, "", ct);
            return fields.Count > 0;
        }
        catch { return false; }
    }

    public static string PickTypeName(DataRow row)
    {
        foreach (var name in new[] { "TypeName", "Name", "Type", "Code", "Record", "ID", "Id" })
        {
            if (!row.Table.Columns.Contains(name)) continue;
            string value = row[name] == DBNull.Value ? "" : Convert.ToString(row[name], CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return "Тип";
    }
}

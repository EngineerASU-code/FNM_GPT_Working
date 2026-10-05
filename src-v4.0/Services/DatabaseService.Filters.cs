using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Configurator;

public partial class DatabaseService
{
    public async Task<List<string>> GetDistinctColumnValuesAsync(string tableName, string columnName, CancellationToken ct = default)
    {
        var result = new List<string>();
        var columns = await GetTableColumnsAsync(tableName, ct);
        var actual = columns.FirstOrDefault(c => c.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase));
        if (actual == null) return result;
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        string sql = $"SELECT DISTINCT {QuoteIdentifier(actual.Name)} FROM {QuoteTableName(tableName)} WHERE {QuoteIdentifier(actual.Name)} IS NOT NULL ORDER BY {QuoteIdentifier(actual.Name)}";
        using var command = new SqlCommand(sql, connection);
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            if (!reader.IsDBNull(0)) result.Add(Convert.ToString(reader.GetValue(0)) ?? "");
        return result;
    }

}

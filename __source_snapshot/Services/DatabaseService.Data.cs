using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Configurator
{
    public partial class DatabaseService
    {
            // === P1-11: Async ===
            public async Task<DataTable> GetTableListAsync(CancellationToken ct = default)
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(ct);
    
                const string query = @"
                    SELECT TABLE_SCHEMA, TABLE_NAME
                    FROM INFORMATION_SCHEMA.TABLES
                    WHERE TABLE_TYPE = 'BASE TABLE'
                    ORDER BY TABLE_NAME";
    
                using var command = new SqlCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync(ct);
    
                var table = new DataTable();
                table.Load(reader);
                return table;
            }
    

            public async Task<List<string>> GetTypeLookupTablesAsync(CancellationToken ct = default)
            {
                var result = new List<string>();
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(ct);

                const string query = @"
                    SELECT TABLE_SCHEMA, TABLE_NAME
                    FROM INFORMATION_SCHEMA.TABLES
                    WHERE TABLE_TYPE = 'BASE TABLE'
                      AND TABLE_SCHEMA = 'dbo'
                      AND (TABLE_NAME LIKE '%Type' OR TABLE_NAME LIKE '%Types')
                    ORDER BY TABLE_NAME";

                using var command = new SqlCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                    result.Add($"{reader.GetString(0)}.{reader.GetString(1)}");

                return result;
            }

            // === P1-11: Async ===
            public async Task<DataTable> GetTableDataPageAsync(string tableName, int pageNumber, int pageSize, IReadOnlyList<string> orderColumns, TableFilter filter = null, CancellationToken ct = default)
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(ct);
    
                if (pageNumber < 1) pageNumber = 1;
                if (pageSize < 1) pageSize = 100;
                long offset = ((long)pageNumber - 1L) * pageSize;
                var order = orderColumns != null && orderColumns.Count > 0
                    ? string.Join(", ", orderColumns.Select(QuoteIdentifier))
                    : "(SELECT 0)";
    
                var where = BuildFilterClause(filter, out var filterValue);
                var query = $@"SELECT *
    FROM {QuoteTableName(tableName)}
    {where}
    ORDER BY {order}
    OFFSET {offset} ROWS FETCH NEXT {pageSize} ROWS ONLY";
    
                using var command = new SqlCommand(query, connection);
                if (filterValue != null) command.Parameters.AddWithValue("@filterValue", filterValue);
                command.CommandTimeout = 60;
                using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
                var pageData = new DataTable();
                pageData.Load(reader);
                return pageData;
            }
    
            public async Task<int> GetTableRowCountAsync(string tableName, TableFilter filter = null, CancellationToken ct = default)
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(ct);
                var where = BuildFilterClause(filter, out var filterValue);
                using var command = new SqlCommand($"SELECT COUNT_BIG(*) FROM {QuoteTableName(tableName)} {where}", connection);
                if (filterValue != null) command.Parameters.AddWithValue("@filterValue", filterValue);
                command.CommandTimeout = 60;
                long count = Convert.ToInt64(await command.ExecuteScalarAsync(ct));
                return count > int.MaxValue ? int.MaxValue : (int)count;
            }
    
            private string BuildFilterClause(TableFilter filter, out string parameterValue)
        {
            parameterValue = null;
            if (filter == null || !filter.IsActive) return string.Empty;

            string column = QuoteIdentifier(filter.Column);
            string op = filter.Operator ?? "";

            // Русские значения используются новым UI; английские оставлены
            // для совместимости с фильтрами из старых конфигураций.
            switch (op)
            {
                case "Пусто (NULL)":
                case "Is Null":
                    return $"WHERE {column} IS NULL";
                case "Не пусто (NOT NULL)":
                case "Is Not Null":
                    return $"WHERE {column} IS NOT NULL";
                case "Равно":
                case "Equals":
                    parameterValue = filter.Value;
                    return $"WHERE {column} = @filterValue";
                case "Не равно":
                case "Not Equals":
                    parameterValue = filter.Value;
                    return $"WHERE {column} <> @filterValue";
                case "Содержит":
                case "Contains":
                    parameterValue = "%" + filter.Value + "%";
                    return $"WHERE CONVERT(nvarchar(max), {column}) LIKE @filterValue";
                case "Не содержит":
                case "Not Contains":
                    parameterValue = "%" + filter.Value + "%";
                    return $"WHERE CONVERT(nvarchar(max), {column}) NOT LIKE @filterValue";
                case "Начинается с":
                case "Starts With":
                    parameterValue = filter.Value + "%";
                    return $"WHERE CONVERT(nvarchar(max), {column}) LIKE @filterValue";
                case "Заканчивается на":
                case "Ends With":
                    parameterValue = "%" + filter.Value;
                    return $"WHERE CONVERT(nvarchar(max), {column}) LIKE @filterValue";
                case "Больше":
                case "Greater":
                    parameterValue = filter.Value;
                    return $"WHERE {column} > @filterValue";
                case "Больше или равно":
                case "Greater Or Equal":
                    parameterValue = filter.Value;
                    return $"WHERE {column} >= @filterValue";
                case "Меньше":
                case "Less":
                    parameterValue = filter.Value;
                    return $"WHERE {column} < @filterValue";
                case "Меньше или равно":
                case "Less Or Equal":
                    parameterValue = filter.Value;
                    return $"WHERE {column} <= @filterValue";
                default:
                    parameterValue = "%" + filter.Value + "%";
                    return $"WHERE CONVERT(nvarchar(max), {column}) LIKE @filterValue";
            }
        }

        public async Task<PagedResult> GetTableDataPagedAsync(string tableName, int pageNumber, int pageSize, CancellationToken ct = default)
            {
                var orderColumns = await GetRowIdentityColumnsAsync(tableName, ct);
                if (orderColumns.Count == 0)
                {
                    var columns = await GetTableColumnsAsync(tableName, ct);
                    if (columns.Count > 0) orderColumns.Add(columns[0].Name);
                }
    
                var dataTask = GetTableDataPageAsync(tableName, pageNumber, pageSize, orderColumns, null, ct);
                var countTask = GetTableRowCountAsync(tableName, null, ct);
                await Task.WhenAll(dataTask, countTask);
                return new PagedResult { Data = dataTask.Result, TotalCount = countTask.Result };
            }
    
            private async Task<List<string>> GetRowIdentityColumnsAsync(string tableName, CancellationToken ct)
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(ct);
                var (schema, table) = ParseTableName(tableName);
                return await GetRowIdentityColumnsInternalAsync(connection, schema, table, ct);
            }
            public async Task<DataTable> GetAllTableRowsAsync(string tableName, CancellationToken ct = default)
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(ct);
                using var command = new SqlCommand($"SELECT * FROM {QuoteTableName(tableName)}", connection);
                command.CommandTimeout = 0;
                using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
                var table = new DataTable();
                table.Load(reader);
                return table;
            }

            public async Task<bool> RowExistsByValuesAsync(string tableName, Dictionary<string, object> values, CancellationToken ct = default)
            {
                if (values == null || values.Count == 0) return false;
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(ct);
                var clauses = new List<string>();
                int index = 0;
                foreach (var pair in values)
                {
                    if (pair.Value == null || pair.Value == DBNull.Value) clauses.Add($"{QuoteIdentifier(pair.Key)} IS NULL");
                    else clauses.Add($"{QuoteIdentifier(pair.Key)} = @p{index++}");
                }
                using var command = new SqlCommand($"SELECT TOP (1) 1 FROM {QuoteTableName(tableName)} WHERE {string.Join(" AND ", clauses)}", connection);
                index = 0;
                foreach (var pair in values)
                    if (pair.Value != null && pair.Value != DBNull.Value) command.Parameters.AddWithValue($"@p{index++}", pair.Value);
                var result = await command.ExecuteScalarAsync(ct);
                return result != null && result != DBNull.Value;
            }

    }
}

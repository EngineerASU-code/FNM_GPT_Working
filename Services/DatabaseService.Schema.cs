using System;
using System.Collections.Generic;
using System.Globalization;
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
            public async Task<List<ColumnInfo>> GetTableColumnsAsync(string tableName, CancellationToken ct = default)
            {
                var columns = new List<ColumnInfo>();
    
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(ct);
    
                var (schema, table) = ParseTableName(tableName);
    
                var query = @"
                    SELECT 
                        c.name AS COLUMN_NAME,
                        t.name AS DATA_TYPE,
                        CASE WHEN c.is_nullable = 1 THEN 'YES' ELSE 'NO' END AS IS_NULLABLE,
                        c.is_identity AS IsIdentity,
                        c.is_computed AS IsComputed,
                        c.max_length AS MaxLength,
                        c.precision AS Precision,
                        c.scale AS Scale,
                        CASE WHEN dc.object_id IS NOT NULL THEN 1 ELSE 0 END AS HasDefault
                    FROM sys.columns c
                    JOIN sys.types t ON c.user_type_id = t.user_type_id
                    JOIN sys.tables tb ON c.object_id = tb.object_id
                    JOIN sys.schemas s ON tb.schema_id = s.schema_id
                    LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
                    WHERE s.name = @schema AND tb.name = @table
                    ORDER BY c.column_id";
    
                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@schema", schema);
                command.Parameters.AddWithValue("@table", table);
    
                using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    columns.Add(new ColumnInfo
                    {
                        Name = reader["COLUMN_NAME"].ToString(),
                        DataType = reader["DATA_TYPE"].ToString(),
                        IsNullable = reader["IS_NULLABLE"].ToString() == "YES",
                        IsIdentity = Convert.ToInt32(reader["IsIdentity"]) == 1,
                        IsComputed = Convert.ToInt32(reader["IsComputed"]) == 1,
                        MaxLength = reader["MaxLength"] != DBNull.Value ? Convert.ToInt32(reader["MaxLength"]) : 0,
                        Precision = reader["Precision"] != DBNull.Value ? Convert.ToByte(reader["Precision"]) : (byte)0,
                        Scale = reader["Scale"] != DBNull.Value ? Convert.ToByte(reader["Scale"]) : (byte)0,
                        HasDefault = Convert.ToInt32(reader["HasDefault"]) == 1
                    });
                }
    
                return columns;
            }
    
            // === P1-11: Async ===
            public async Task<List<string>> GetPrimaryKeyColumnsAsync(string tableName, CancellationToken ct = default)
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(ct);
    
                var (schema, table) = ParseTableName(tableName);
                return await GetRowIdentityColumnsInternalAsync(connection, schema, table, ct);
            }
    
            private static async Task<List<string>> GetRowIdentityColumnsInternalAsync(SqlConnection connection, string schema, string table, CancellationToken ct = default)
            {
                // 1) Всегда предпочитаем первичный ключ.
                var primaryKeyQuery = @"
                    SELECT c.name AS COLUMN_NAME
                    FROM sys.indexes i
                    INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                    INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
                    INNER JOIN sys.tables t ON i.object_id = t.object_id
                    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                    WHERE s.name = @schema AND t.name = @table
                      AND i.is_primary_key = 1
                    ORDER BY ic.key_ordinal";
    
                var primaryKey = new List<string>();
                using (var command = new SqlCommand(primaryKeyQuery, connection))
                {
                    command.Parameters.AddWithValue("@schema", schema);
                    command.Parameters.AddWithValue("@table", table);
                    using var reader = await command.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct))
                        primaryKey.Add(reader["COLUMN_NAME"].ToString());
                }
    
                if (primaryKey.Count > 0)
                    return primaryKey;
    
                // 2) Если PK нет, используем первый уникальный индекс, состоящий
                // только из NOT NULL ключевых колонок. Это позволяет безопасно
                // редактировать реальные таблицы без PK, но с UNIQUE идентификатором.
                var uniqueQuery = @"
                    SELECT i.index_id, ic.key_ordinal, c.name AS COLUMN_NAME, c.is_nullable
                    FROM sys.indexes i
                    INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                    INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
                    INNER JOIN sys.tables t ON i.object_id = t.object_id
                    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                    WHERE s.name = @schema AND t.name = @table
                      AND i.is_unique = 1
                      AND i.is_primary_key = 0
                      AND i.is_disabled = 0
                      AND i.is_hypothetical = 0
                      AND i.has_filter = 0
                      AND ic.is_included_column = 0
                    ORDER BY i.index_id, ic.key_ordinal";
    
                var candidates = new Dictionary<int, List<(int Ordinal, string Name, bool Nullable)>>();
                using (var command = new SqlCommand(uniqueQuery, connection))
                {
                    command.Parameters.AddWithValue("@schema", schema);
                    command.Parameters.AddWithValue("@table", table);
                    using var reader = await command.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct))
                    {
                        int indexId = Convert.ToInt32(reader["index_id"]);
                        if (!candidates.TryGetValue(indexId, out var list))
                        {
                            list = new List<(int, string, bool)>();
                            candidates[indexId] = list;
                        }
                        list.Add((Convert.ToInt32(reader["key_ordinal"]), reader["COLUMN_NAME"].ToString(), Convert.ToBoolean(reader["is_nullable"])));
                    }
                }
    
                foreach (var candidate in candidates.OrderBy(x => x.Key))
                {
                    var columns = candidate.Value.OrderBy(x => x.Ordinal).ToList();
                    if (columns.Count > 0 && columns.All(x => !x.Nullable))
                        return columns.Select(x => x.Name).ToList();
                }
    
                return new List<string>();
            }
    
            public async Task<string> GetPrimaryKeyColumnAsync(string tableName, CancellationToken ct = default)
            {
                var keys = await GetPrimaryKeyColumnsAsync(tableName, ct);
                return keys.Count > 0 ? keys[0] : null;
            }
    
            // === P1-11: Async ===
            public async Task<int> GetMaxRecordValueAsync(string tableName, string recordColumn, CancellationToken ct = default)
            {
                if (string.IsNullOrWhiteSpace(recordColumn))
                    throw new ArgumentException("Имя столбца Record не задано.", nameof(recordColumn));

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(ct);

                var quotedName = QuoteTableName(tableName);
                var quotedRecord = QuoteIdentifier(recordColumn);
                var sql = $"SELECT {quotedRecord} FROM {quotedName} WHERE {quotedRecord} IS NOT NULL";
                using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
                using var reader = await command.ExecuteReaderAsync(ct);

                long maxValue = 0;
                while (await reader.ReadAsync(ct))
                {
                    var raw = reader.GetValue(0);
                    if (raw == null || raw == DBNull.Value) continue;
                    if (TryReadIntegralValue(raw, out long number) && number > maxValue)
                        maxValue = number;
                }

                if (maxValue >= int.MaxValue)
                    throw new InvalidOperationException($"Невозможно создать следующий {recordColumn}: достигнут предел Int32.");

                return (int)maxValue;
            }

            private static bool TryReadIntegralValue(object raw, out long value)
            {
                value = 0;
                try
                {
                    if (raw == null || raw == DBNull.Value) return false;
                    switch (raw)
                    {
                        case byte v: value = v; return true;
                        case sbyte v: value = v; return true;
                        case short v: value = v; return true;
                        case ushort v: value = v; return true;
                        case int v: value = v; return true;
                        case uint v: value = v; return true;
                        case long v: value = v; return true;
                        case ulong v:
                            if (v > long.MaxValue) return false;
                            value = (long)v; return true;
                        case decimal v:
                            if (v != decimal.Truncate(v) || v > long.MaxValue || v < long.MinValue) return false;
                            value = (long)v; return true;
                        case double v:
                            if (double.IsNaN(v) || double.IsInfinity(v) || v != Math.Truncate(v) || v > long.MaxValue || v < long.MinValue) return false;
                            value = (long)v; return true;
                        case float v:
                            if (float.IsNaN(v) || float.IsInfinity(v) || v != MathF.Truncate(v) || v > long.MaxValue || v < long.MinValue) return false;
                            value = (long)v; return true;
                        default:
                            return long.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
                    }
                }
                catch
                {
                    value = 0;
                    return false;
                }
            }

            public async Task<int> GetNextScopedRecordValueAsync(string tableName, string recordColumn, string scopeColumn, object scopeValue, CancellationToken ct = default)
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(ct);

                var quotedName = QuoteTableName(tableName);
                var quotedRecord = QuoteIdentifier(recordColumn);
                var quotedScope = QuoteIdentifier(scopeColumn);
                string scopePredicate = scopeValue == null || scopeValue == DBNull.Value
                    ? $"{quotedScope} IS NULL"
                    : $"{quotedScope} = @scope";

                var meta = ParseTableName(tableName);
                const string typeSql = @"SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS
                                         WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table AND COLUMN_NAME = @column";
                using (var typeCommand = new SqlCommand(typeSql, connection))
                {
                    typeCommand.Parameters.AddWithValue("@schema", meta.schema);
                    typeCommand.Parameters.AddWithValue("@table", meta.table);
                    typeCommand.Parameters.AddWithValue("@column", recordColumn);
                    var dataType = Convert.ToString(await typeCommand.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture)?.ToLowerInvariant();
                    if (dataType is "tinyint" or "smallint" or "int" or "bigint" or "decimal" or "numeric" or "money" or "smallmoney" or "float" or "real")
                    {
                        var maxSql = $"SELECT MAX(CONVERT(decimal(38,0), {quotedRecord})) FROM {quotedName} WHERE {scopePredicate}";
                        using var maxCommand = new SqlCommand(maxSql, connection);
                        if (scopeValue != null && scopeValue != DBNull.Value) maxCommand.Parameters.AddWithValue("@scope", scopeValue);
                        var scalar = await maxCommand.ExecuteScalarAsync(ct);
                        if (scalar == null || scalar == DBNull.Value) return 1;
                        long max = Convert.ToInt64(scalar, CultureInfo.InvariantCulture);
                        if (max >= int.MaxValue) throw new InvalidOperationException($"Невозможно создать следующий {recordColumn}: достигнут предел Int32.");
                        return (int)max + 1;
                    }
                }

                using var command = new SqlCommand($"SELECT {quotedRecord} FROM {quotedName} WHERE {scopePredicate}", connection);
                if (scopeValue != null && scopeValue != DBNull.Value) command.Parameters.AddWithValue("@scope", scopeValue);
                long maxValue = 0;
                using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var raw = reader.GetValue(0);
                    if (raw == null || raw == DBNull.Value) continue;
                    if (TryReadIntegralValue(raw, out long number) && number > maxValue) maxValue = number;
                }
                if (maxValue >= int.MaxValue) throw new InvalidOperationException($"Невозможно создать следующий {recordColumn}: достигнут предел Int32.");
                return (int)maxValue + 1;
            }
            public async Task<List<LookupOption>> GetLookupOptionsAsync(string tableName, CancellationToken ct = default)
            {
                var result = new List<LookupOption>();
                var columns = await GetTableColumnsAsync(tableName, ct);
                if (columns.Count == 0) return result;

                string key = columns.Select(x => x.Name).FirstOrDefault(n => n.Equals("Type", StringComparison.OrdinalIgnoreCase))
                    ?? columns.Select(x => x.Name).FirstOrDefault(n => n.Equals("Record", StringComparison.OrdinalIgnoreCase))
                    ?? columns.Select(x => x.Name).FirstOrDefault(n => n.Equals("Id", StringComparison.OrdinalIgnoreCase))
                    ?? columns[0].Name;
                string name = columns.Select(x => x.Name).FirstOrDefault(n => n.Equals("Name", StringComparison.OrdinalIgnoreCase))
                    ?? columns.Select(x => x.Name).FirstOrDefault(n => n.Equals("NameL1", StringComparison.OrdinalIgnoreCase))
                    ?? columns.Select(x => x.Name).FirstOrDefault(n => n.Equals("Description", StringComparison.OrdinalIgnoreCase))
                    ?? columns.Select(x => x.Name).FirstOrDefault(n => n.Equals("DescriptionL1", StringComparison.OrdinalIgnoreCase));
                string nameL2 = columns.Select(x => x.Name).FirstOrDefault(n => n.Equals("NameL2", StringComparison.OrdinalIgnoreCase));

                var (schema, table) = ParseTableName(tableName);
                string qSchema = QuoteIdentifier(schema);
                string qTable = QuoteIdentifier(table);
                string qKey = QuoteIdentifier(key);
                string selectName = string.IsNullOrWhiteSpace(name) ? "" : $", {QuoteIdentifier(name)} AS [__LookupName]";
                string selectNameL2 = string.IsNullOrWhiteSpace(nameL2) ? "" : $", {QuoteIdentifier(nameL2)} AS [__LookupNameL2]";

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(ct);
                using var command = new SqlCommand($"SELECT TOP (2000) {qKey} AS [__LookupKey]{selectName}{selectNameL2} FROM {qSchema}.{qTable} ORDER BY {qKey}", connection);
                using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    if (reader["__LookupKey"] == DBNull.Value) continue;
                    string keyText = Convert.ToString(reader["__LookupKey"]) ?? "";
                    if (string.IsNullOrWhiteSpace(keyText)) continue;
                    string title = "";
                    if (!string.IsNullOrWhiteSpace(name) && reader["__LookupName"] != DBNull.Value) title = Convert.ToString(reader["__LookupName"]) ?? "";
                    if (!string.IsNullOrWhiteSpace(nameL2) && reader["__LookupNameL2"] != DBNull.Value)
                    {
                        string l2 = Convert.ToString(reader["__LookupNameL2"]) ?? "";
                        title = string.IsNullOrWhiteSpace(title) ? l2 : $"{title} · {l2}";
                    }
                    result.Add(new LookupOption { Key = keyText, Display = string.IsNullOrWhiteSpace(title) ? keyText : $"{keyText} — {title}" });
                }
                return result;
            }

    }
}

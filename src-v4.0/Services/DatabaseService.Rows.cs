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
            public async Task InsertRowAsync(string tableName, Dictionary<string, object> values, CancellationToken ct = default)
            {
                if (values == null || values.Count == 0)
                    throw new InvalidOperationException("Нет данных для вставки.");
    
                // Полностью пустые строки запрещены на уровне сервиса даже если
                // SQL-схема технически позволяет вставить набор NULL.
                // Это защищает БД от случайных "пустых" записей независимо от UI.
                bool hasMeaningfulValue = values.Values.Any(v =>
                    v != null && v != DBNull.Value &&
                    (v is not string text || !string.IsNullOrWhiteSpace(text)));
                if (!hasMeaningfulValue)
                    throw new InvalidOperationException("Нельзя добавить полностью пустую строку. Заполните хотя бы одно поле.");
    
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(ct);
    
                var quotedName = QuoteTableName(tableName);
                var columns = values.Keys.ToList();
                var columnSql = string.Join(", ", columns.Select(QuoteIdentifier));
                var paramSql = string.Join(", ", columns.Select((_, i) => $"@p{i}"));
                var query = $"INSERT INTO {quotedName} ({columnSql}) VALUES ({paramSql})";
    
                using var command = new SqlCommand(query, connection);
                for (int i = 0; i < columns.Count; i++)
                {
                    var value = values[columns[i]];
                    command.Parameters.AddWithValue($"@p{i}", value ?? DBNull.Value);
                }
    
                await command.ExecuteNonQueryAsync(ct);
            }
    
            public async Task UpdateRowAsync(string tableName, List<string> keyColumns, Dictionary<string, object> keyValues, Dictionary<string, object> values, CancellationToken ct = default)
            {
                if (keyColumns == null || keyColumns.Count == 0)
                    throw new InvalidOperationException("Невозможно обновить запись без первичного ключа.");
                if (keyValues == null || keyColumns.Any(k => !keyValues.ContainsKey(k)))
                    throw new InvalidOperationException("Не удалось определить значения первичного ключа.");
                if (values == null || values.Count == 0)
                    throw new InvalidOperationException("Нет данных для обновления.");
    
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(ct);
                using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
    
                try
                {
                    var quotedName = QuoteTableName(tableName);
                    var columns = values.Keys.ToList();
                    var setClause = string.Join(", ", columns.Select((c, i) => $"{QuoteIdentifier(c)} = @p{i}"));
                    var whereClause = string.Join(" AND ", keyColumns.Select((c, i) => $"{QuoteIdentifier(c)} = @key{i}"));
                    var query = $"UPDATE {quotedName} SET {setClause} WHERE {whereClause}";
    
                    using var command = new SqlCommand(query, connection, transaction);
                    for (int i = 0; i < columns.Count; i++)
                        command.Parameters.AddWithValue($"@p{i}", values[columns[i]] ?? DBNull.Value);
                    for (int i = 0; i < keyColumns.Count; i++)
                        command.Parameters.AddWithValue($"@key{i}", keyValues[keyColumns[i]] ?? DBNull.Value);
    
                    int affected = await command.ExecuteNonQueryAsync(ct);
                    if (affected == 0)
                        throw new InvalidOperationException("Запись не найдена — возможно, она была изменена или удалена другим пользователем.");
                    if (affected > 1)
                        throw new InvalidOperationException($"Условие ключа затронуло {affected} записей. Изменение отменено.");
    
                    await transaction.CommitAsync(ct);
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    throw;
                }
            }
    
            public async Task<int> DeleteRowsAsync(string tableName, List<string> keyColumns, List<Dictionary<string, object>> keyValuesList, CancellationToken ct = default)
            {
                if (keyValuesList == null || keyValuesList.Count == 0)
                    return 0;
    
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(ct);
    
                var quotedName = QuoteTableName(tableName);
                int totalDeleted = 0;
    
                using var transaction = await connection.BeginTransactionAsync(ct);
                try
                {
                    var whereParts = new List<string>();
                    for (int i = 0; i < keyColumns.Count; i++)
                    {
                        whereParts.Add($"{QuoteIdentifier(keyColumns[i])} = @key{i}");
                    }
                    var whereClause = string.Join(" AND ", whereParts);
                    var query = $"DELETE FROM {quotedName} WHERE {whereClause}";
    
                    foreach (var keyValues in keyValuesList)
                    {
                        using var command = new SqlCommand(query, connection, (SqlTransaction)transaction);
                        for (int i = 0; i < keyColumns.Count; i++)
                        {
                            command.Parameters.AddWithValue($"@key{i}", keyValues[keyColumns[i]] ?? DBNull.Value);
                        }
                        totalDeleted += await command.ExecuteNonQueryAsync(ct);
                    }
    
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
    
                return totalDeleted;
            }
    
            public async Task UpdateRowByValuesAsync(string tableName, Dictionary<string, object> originalValues, Dictionary<string, object> values, CancellationToken ct = default)
            {
                if (originalValues == null || originalValues.Count == 0)
                    throw new InvalidOperationException("Невозможно определить исходную строку.");
                if (values == null || values.Count == 0)
                    throw new InvalidOperationException("Нет данных для обновления.");
    
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(ct);
                using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
    
                try
                {
                    var quotedName = QuoteTableName(tableName);
                    var setParts = new List<string>();
                    var whereParts = new List<string>();
                    int valueParameter = 0;
                    int originalParameter = 0;
    
                    foreach (var item in values)
                        setParts.Add($"{QuoteIdentifier(item.Key)} = @v{valueParameter++}");
    
                    // NULL сравниваем через IS NULL, а не через = @param.
                    // Это исправляет UPDATE строк без PK, содержащих NULL.
                    foreach (var item in originalValues)
                    {
                        if (item.Value == null || item.Value == DBNull.Value)
                            whereParts.Add($"{QuoteIdentifier(item.Key)} IS NULL");
                        else
                            whereParts.Add($"{QuoteIdentifier(item.Key)} = @o{originalParameter++}");
                    }
    
                    var query = $"UPDATE TOP (1) {quotedName} SET {string.Join(", ", setParts)} WHERE {string.Join(" AND ", whereParts)}";
    
                    using var command = new SqlCommand(query, connection, transaction);
                    valueParameter = 0;
                    foreach (var item in values)
                        command.Parameters.AddWithValue($"@v{valueParameter++}", item.Value ?? DBNull.Value);
    
                    originalParameter = 0;
                    foreach (var item in originalValues)
                    {
                        if (item.Value != null && item.Value != DBNull.Value)
                            command.Parameters.AddWithValue($"@o{originalParameter++}", item.Value);
                    }
    
                    int affected = await command.ExecuteNonQueryAsync(ct);
                    if (affected == 0)
                        throw new InvalidOperationException("Исходная строка больше не найдена. Обновление отменено.");
    
                    await transaction.CommitAsync(ct);
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    throw;
                }
            }
    
            public async Task<int> DeleteRowsByValuesAsync(string tableName, List<Dictionary<string, object>> rows, CancellationToken ct = default)
            {
                if (rows == null || rows.Count == 0) return 0;
    
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(ct);
                using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
                int totalDeleted = 0;
    
                try
                {
                    foreach (var row in rows)
                    {
                        if (row == null || row.Count == 0) continue;
    
                        var whereParts = new List<string>();
                        int parameterIndex = 0;
                        foreach (var item in row)
                        {
                            if (item.Value == null || item.Value == DBNull.Value)
                                whereParts.Add($"{QuoteIdentifier(item.Key)} IS NULL");
                            else
                                whereParts.Add($"{QuoteIdentifier(item.Key)} = @p{parameterIndex++}");
                        }
    
                        var query = $"DELETE TOP (1) FROM {QuoteTableName(tableName)} WHERE {string.Join(" AND ", whereParts)}";
                        using var command = new SqlCommand(query, connection, transaction);
    
                        parameterIndex = 0;
                        foreach (var item in row)
                        {
                            if (item.Value != null && item.Value != DBNull.Value)
                                command.Parameters.AddWithValue($"@p{parameterIndex++}", item.Value);
                        }
    
                        totalDeleted += await command.ExecuteNonQueryAsync(ct);
                    }
    
                    await transaction.CommitAsync(ct);
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    throw;
                }
    
                return totalDeleted;
            }
    
            // === P1-11: Async ===
    }
}

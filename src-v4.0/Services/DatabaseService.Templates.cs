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
            public async Task<List<TemplateColumn>> GetTemplateColumnsAsync(string tableName, CancellationToken ct = default)
            {
                var result = new List<TemplateColumn>();
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(ct);
                var (schema, table) = ParseTableName(tableName);
    
                const string sql = @"
    SELECT c.name AS ColumnName,
           t.name AS DataType,
           c.max_length AS MaxLength,
           c.precision AS [Precision],
           c.scale AS Scale,
           c.is_nullable AS IsNullable,
           c.is_identity AS IsIdentity,
           c.is_computed AS IsComputed,
           dc.definition AS DefaultDefinition,
           CASE WHEN pk.column_id IS NULL THEN 0 ELSE 1 END AS IsPrimaryKey
    FROM sys.columns c
    JOIN sys.tables tb ON tb.object_id = c.object_id
    JOIN sys.schemas s ON s.schema_id = tb.schema_id
    JOIN sys.types t ON t.user_type_id = c.user_type_id
    LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
    LEFT JOIN (
        SELECT ic.object_id, ic.column_id
        FROM sys.index_columns ic
        JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
        WHERE i.is_primary_key = 1
    ) pk ON pk.object_id = c.object_id AND pk.column_id = c.column_id
    WHERE s.name = @schema AND tb.name = @table
    ORDER BY c.column_id";
    
                using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@schema", schema);
                command.Parameters.AddWithValue("@table", table);
                using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    bool computed = Convert.ToBoolean(reader["IsComputed"]);
                    string type = reader["DataType"].ToString() ?? "int";
                    result.Add(new TemplateColumn
                    {
                        Name = reader["ColumnName"].ToString() ?? "",
                        OriginalName = reader["ColumnName"].ToString() ?? "",
                        DataType = type,
                        MaxLength = Convert.ToInt32(reader["MaxLength"]),
                        Precision = Convert.ToByte(reader["Precision"]),
                        Scale = Convert.ToByte(reader["Scale"]),
                        IsNullable = Convert.ToBoolean(reader["IsNullable"]),
                        IsIdentity = Convert.ToBoolean(reader["IsIdentity"]),
                        IsPrimaryKey = Convert.ToBoolean(reader["IsPrimaryKey"]),
                        IsComputed = computed,
                        HasDefault = reader["DefaultDefinition"] != DBNull.Value,
                        DefaultDefinition = reader["DefaultDefinition"] == DBNull.Value ? "" : reader["DefaultDefinition"].ToString() ?? "",
                        IsExisting = true
                    });
                }
                return result;
            }
    
            public async Task<bool> TableExistsAsync(string tableName, CancellationToken ct = default)
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(ct);
                var (schema, table) = ParseTableName(tableName);
                const string sql = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE s.name=@schema AND t.name=@table) THEN 1 ELSE 0 END";
                using var command = new SqlCommand(sql, connection);
                command.Parameters.AddWithValue("@schema", schema);
                command.Parameters.AddWithValue("@table", table);
                return Convert.ToInt32(await command.ExecuteScalarAsync(ct)) == 1;
            }
    
            public async Task CreateTemplateTableAsync(string tableName, List<TemplateColumn> columns, CancellationToken ct = default)
            {
                if (columns == null || columns.Count == 0)
                    throw new InvalidOperationException("Класс должен содержать хотя бы одно поле.");
    
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(ct);
                var quotedName = QuoteTableName(tableName);
                var definitions = columns.Select(BuildColumnDefinition).ToList();
                var pk = columns.Where(c => c.IsPrimaryKey).Select(c => QuoteIdentifier(c.Name)).ToList();
                if (pk.Count > 0)
                    definitions.Add($"CONSTRAINT {QuoteIdentifier("PK_" + ParseTableName(tableName).table)} PRIMARY KEY ({string.Join(", ", pk)})");
    
                var sql = $"CREATE TABLE {quotedName} ({string.Join(", ", definitions)})";
                using var command = new SqlCommand(sql, connection);
                await command.ExecuteNonQueryAsync(ct);
            }
    
            public async Task SyncTemplateColumnsAsync(string tableName, List<TemplateColumn> desired, CancellationToken ct = default)
            {
                if (desired == null || desired.Count == 0)
                    throw new InvalidOperationException("Таблица должна содержать хотя бы одно поле.");

                if (desired.Any(c => !c.IsExisting && c.IsComputed))
                    throw new InvalidOperationException("Создание новых вычисляемых полей через редактор пока не поддерживается.");

                var existing = await GetTemplateColumnsAsync(tableName, ct);
                var existingByName = existing.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
                var desiredByName = desired.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

                var currentPk = existing.Where(c => c.IsPrimaryKey)
                    .Select(c => c.Name).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
                var desiredPk = desired.Where(c => c.IsPrimaryKey)
                    .Select(c => c.Name).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();

                if (desiredPk.Any(name => !desiredByName.ContainsKey(name)))
                    throw new InvalidOperationException("Первичный ключ содержит неизвестное поле.");

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(ct);
                using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);

                try
                {
                    // Если состав PK меняется, сначала удаляем старое ограничение.
                    // Новое создаём после всех ALTER/ADD/DROP, чтобы изменения
                    // структуры выполнялись атомарно в одной транзакции.
                    if (!currentPk.SequenceEqual(desiredPk, StringComparer.OrdinalIgnoreCase))
                    {
                        string pkName = await GetPrimaryKeyConstraintNameAsync(connection, transaction, tableName, ct);
                        if (!string.IsNullOrWhiteSpace(pkName))
                        {
                            string dropPk = $"ALTER TABLE {QuoteTableName(tableName)} DROP CONSTRAINT {QuoteIdentifier(pkName)}";
                            using var dropPkCommand = new SqlCommand(dropPk, connection, transaction);
                            await dropPkCommand.ExecuteNonQueryAsync(ct);
                        }
                    }

                    // Переименования выполняются до остальных операций.
                    foreach (var col in desired.Where(c => c.IsExisting && !string.IsNullOrWhiteSpace(c.OriginalName)))
                    {
                        if (!string.Equals(col.OriginalName, col.Name, StringComparison.OrdinalIgnoreCase) &&
                            existingByName.ContainsKey(col.OriginalName))
                        {
                            string renameSql =
                                $"EXEC sp_rename N'{EscapeSqlLiteral(ParseTableName(tableName).schema)}." +
                                $"{EscapeSqlLiteral(ParseTableName(tableName).table)}." +
                                $"{EscapeSqlLiteral(col.OriginalName)}', N'{EscapeSqlLiteral(col.Name)}', 'COLUMN'";

                            using var rename = new SqlCommand(renameSql, connection, transaction);
                            await rename.ExecuteNonQueryAsync(ct);
                        }
                    }

                    var refreshed = await GetTemplateColumnsAsyncOnConnection(connection, transaction, tableName, ct);
                    existingByName = refreshed.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

                    foreach (var col in desired)
                    {
                        if (!existingByName.TryGetValue(col.Name, out var old))
                        {
                            if (col.IsComputed)
                                throw new InvalidOperationException($"Нельзя добавить вычисляемое поле {col.Name} без выражения.");

                            string addSql = $"ALTER TABLE {QuoteTableName(tableName)} ADD {BuildColumnDefinition(col)}";
                            using var add = new SqlCommand(addSql, connection, transaction);
                            await add.ExecuteNonQueryAsync(ct);
                            continue;
                        }

                        if (old.IsIdentity != col.IsIdentity)
                            throw new InvalidOperationException(
                                $"Нельзя изменить Identity у существующего поля {col.Name}. SQL Server не поддерживает это через ALTER COLUMN.");

                        if (old.IsComputed)
                        {
                            // Вычисляемые поля не меняем: их выражение хранится в БД.
                            continue;
                        }

                        if (!old.IsIdentity && !ColumnDefinitionEquals(old, col))
                        {
                            string alterType = BuildTypeDefinition(col);
                            string nullable = col.IsNullable ? "NULL" : "NOT NULL";
                            string alterSql =
                                $"ALTER TABLE {QuoteTableName(tableName)} ALTER COLUMN {QuoteIdentifier(col.Name)} {alterType} {nullable}";

                            using var alter = new SqlCommand(alterSql, connection, transaction);
                            await alter.ExecuteNonQueryAsync(ct);
                        }

                        if (!DefaultEquals(old.DefaultDefinition, col.DefaultDefinition))
                        {
                            string constraintName = await GetDefaultConstraintNameAsync(
                                connection, transaction, tableName, col.Name, ct);

                            if (!string.IsNullOrWhiteSpace(constraintName))
                            {
                                string dropSql =
                                    $"ALTER TABLE {QuoteTableName(tableName)} DROP CONSTRAINT {QuoteIdentifier(constraintName)}";
                                using var drop = new SqlCommand(dropSql, connection, transaction);
                                await drop.ExecuteNonQueryAsync(ct);
                            }

                            if (!string.IsNullOrWhiteSpace(col.DefaultDefinition))
                            {
                                string addDefault =
                                    $"ALTER TABLE {QuoteTableName(tableName)} ADD CONSTRAINT " +
                                    $"{QuoteIdentifier("DF_" + ParseTableName(tableName).table + "_" + col.Name)} " +
                                    $"DEFAULT {col.DefaultDefinition} FOR {QuoteIdentifier(col.Name)}";

                                using var addDefaultCmd = new SqlCommand(addDefault, connection, transaction);
                                await addDefaultCmd.ExecuteNonQueryAsync(ct);
                            }
                        }
                    }

                    foreach (var old in refreshed)
                    {
                        if (!desiredByName.ContainsKey(old.Name))
                        {
                            if (old.IsPrimaryKey && desiredPk.Length == 0)
                            {
                                // PK уже снят выше.
                            }

                            if (old.IsComputed)
                            {
                                string dropComputed =
                                    $"ALTER TABLE {QuoteTableName(tableName)} DROP COLUMN {QuoteIdentifier(old.Name)}";
                                using var drop = new SqlCommand(dropComputed, connection, transaction);
                                await drop.ExecuteNonQueryAsync(ct);
                                continue;
                            }

                            string dropSql =
                                $"ALTER TABLE {QuoteTableName(tableName)} DROP COLUMN {QuoteIdentifier(old.Name)}";
                            using var dropColumn = new SqlCommand(dropSql, connection, transaction);
                            await dropColumn.ExecuteNonQueryAsync(ct);
                        }
                    }

                    if (desiredPk.Length > 0)
                    {
                        foreach (var pkColumn in desired.Where(c => c.IsPrimaryKey))
                        {
                            if (pkColumn.IsNullable)
                                throw new InvalidOperationException(
                                    $"Поле PK «{pkColumn.Name}» должно быть NOT NULL.");
                        }

                        string constraintName = "PK_" + ParseTableName(tableName).table;
                        string addPk =
                            $"ALTER TABLE {QuoteTableName(tableName)} ADD CONSTRAINT " +
                            $"{QuoteIdentifier(constraintName)} PRIMARY KEY ({string.Join(", ", desiredPk.Select(QuoteIdentifier))})";

                        using var addPkCommand = new SqlCommand(addPk, connection, transaction);
                        await addPkCommand.ExecuteNonQueryAsync(ct);
                    }

                    await transaction.CommitAsync(ct);
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    throw;
                }
            }

            public async Task RenameTableAsync(string oldTableName, string newTableName, CancellationToken ct = default)
            {
                var oldParts = ParseTableName(oldTableName);
                var newParts = ParseTableName(newTableName);

                if (!string.Equals(oldParts.schema, newParts.schema, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Переименование между разными схемами не поддерживается.");

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(ct);

                string sql = $"EXEC sp_rename N'{EscapeSqlLiteral(oldParts.schema)}.{EscapeSqlLiteral(oldParts.table)}', N'{EscapeSqlLiteral(newParts.table)}', 'OBJECT'";
                using var command = new SqlCommand(sql, connection);
                await command.ExecuteNonQueryAsync(ct);
            }

            public async Task DropTableAsync(string tableName, CancellationToken ct = default)
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(ct);
                using var command = new SqlCommand($"DROP TABLE {QuoteTableName(tableName)}", connection);
                await command.ExecuteNonQueryAsync(ct);
            }
private static string BuildColumnDefinition(TemplateColumn col)
            {
                string identity = col.IsIdentity ? " IDENTITY(1,1)" : "";
                string nullable = col.IsNullable ? " NULL" : " NOT NULL";
                string defaultSql = string.IsNullOrWhiteSpace(col.DefaultDefinition) ? "" : $" DEFAULT {col.DefaultDefinition}";
                return $"{QuoteStatic(col.Name)} {BuildTypeDefinition(col)}{identity}{defaultSql}{nullable}";
            }
    
            private static string BuildTypeDefinition(TemplateColumn col)
            {
                string type = col.DataType.ToLowerInvariant();
                if (type is "varchar" or "nvarchar" or "char" or "nchar" or "varbinary")
                    return $"{type}({(col.MaxLength < 0 ? "MAX" : (type.StartsWith("n") ? Math.Max(1, col.MaxLength / 2).ToString() : Math.Max(1, col.MaxLength).ToString()))})";
                if (type is "decimal" or "numeric")
                    return $"{type}({Math.Max(1, (int)col.Precision)},{Math.Min((int)col.Scale, (int)col.Precision)})";
                return type;
            }
private static bool ColumnDefinitionEquals(TemplateColumn a, TemplateColumn b)
            {
                return string.Equals(a.DataType, b.DataType, StringComparison.OrdinalIgnoreCase) &&
                       a.MaxLength == b.MaxLength && a.Precision == b.Precision && a.Scale == b.Scale &&
                       a.IsNullable == b.IsNullable;
            }
    
            private static bool DefaultEquals(string a, string b) =>
                string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
    
            private static async Task<List<TemplateColumn>> GetTemplateColumnsAsyncOnConnection(SqlConnection connection, SqlTransaction transaction, string tableName, CancellationToken ct)
            {
                var result = new List<TemplateColumn>();
                var (schema, table) = tableName.Contains('.') ? (tableName.Split('.', 2)[0], tableName.Split('.', 2)[1]) : ("dbo", tableName);
                const string sql = @"
    SELECT c.name AS ColumnName, t.name AS DataType, c.max_length AS MaxLength,
           c.precision AS [Precision], c.scale AS Scale, c.is_nullable AS IsNullable,
           c.is_identity AS IsIdentity, c.is_computed AS IsComputed, dc.definition AS DefaultDefinition,
           CASE WHEN pk.column_id IS NULL THEN 0 ELSE 1 END AS IsPrimaryKey
    FROM sys.columns c
    JOIN sys.tables tb ON tb.object_id=c.object_id
    JOIN sys.schemas s ON s.schema_id=tb.schema_id
    JOIN sys.types t ON t.user_type_id=c.user_type_id
    LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id
    LEFT JOIN (SELECT ic.object_id, ic.column_id FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id=ic.object_id AND i.index_id=ic.index_id WHERE i.is_primary_key=1) pk
    ON pk.object_id=c.object_id AND pk.column_id=c.column_id
    WHERE s.name=@schema AND tb.name=@table ORDER BY c.column_id";
                using var command = new SqlCommand(sql, connection, transaction);
                command.Parameters.AddWithValue("@schema", schema);
                command.Parameters.AddWithValue("@table", table);
                using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    string name = reader["ColumnName"].ToString() ?? "";
                    result.Add(new TemplateColumn
                    {
                        Name = name, OriginalName = name, DataType = reader["DataType"].ToString() ?? "int",
                        MaxLength = Convert.ToInt32(reader["MaxLength"]), Precision = Convert.ToByte(reader["Precision"]), Scale = Convert.ToByte(reader["Scale"]),
                        IsNullable = Convert.ToBoolean(reader["IsNullable"]), IsIdentity = Convert.ToBoolean(reader["IsIdentity"]),
                        IsPrimaryKey = Convert.ToBoolean(reader["IsPrimaryKey"]),
                    IsComputed = Convert.ToBoolean(reader["IsComputed"]),
                    HasDefault = reader["DefaultDefinition"] != DBNull.Value,
                        DefaultDefinition = reader["DefaultDefinition"] == DBNull.Value ? "" : reader["DefaultDefinition"].ToString() ?? "", IsExisting = true
                    });
                }
                return result;
            }
    
            private static async Task<string> GetPrimaryKeyConstraintNameAsync(
                SqlConnection connection, SqlTransaction transaction, string tableName, CancellationToken ct)
            {
                var (schema, table) = ParseTableName(tableName);
                const string sql = @"
SELECT kc.name
FROM sys.key_constraints kc
JOIN sys.tables t ON t.object_id = kc.parent_object_id
JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE kc.type = 'PK' AND s.name = @schema AND t.name = @table";

                using var command = new SqlCommand(sql, connection, transaction);
                command.Parameters.AddWithValue("@schema", schema);
                command.Parameters.AddWithValue("@table", table);
                return (await command.ExecuteScalarAsync(ct))?.ToString() ?? "";
            }

            private static async Task<string> GetDefaultConstraintNameAsync(SqlConnection connection, SqlTransaction transaction, string tableName, string columnName, CancellationToken ct)
            {
                var parts = tableName.Split('.', 2);
                string schema = parts.Length > 1 ? parts[0] : "dbo";
                string table = parts.Length > 1 ? parts[1] : parts[0];
                const string sql = @"SELECT dc.name FROM sys.default_constraints dc JOIN sys.columns c ON c.default_object_id=dc.object_id JOIN sys.tables t ON t.object_id=c.object_id JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE s.name=@schema AND t.name=@table AND c.name=@column";
                using var command = new SqlCommand(sql, connection, transaction);
                command.Parameters.AddWithValue("@schema", schema);
                command.Parameters.AddWithValue("@table", table);
                command.Parameters.AddWithValue("@column", columnName);
                return (await command.ExecuteScalarAsync(ct))?.ToString() ?? "";
            }
    
    }
}

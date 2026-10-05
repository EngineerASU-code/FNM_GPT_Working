using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Configurator.Core.Architecture;

namespace Configurator.Infrastructure.Database;

public sealed class ClassRegistryService
{
    private readonly string _connectionString;

    public ClassRegistryService(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task EnsureRegistryAsync(CancellationToken ct = default)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        const string existsSql = """
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM sys.tables t
                INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
                WHERE s.name = N'dbo' AND t.name = N'Classes'
            ) THEN 1 ELSE 0 END
            """;

        using var exists = new SqlCommand(existsSql, connection);
        bool existsResult = Convert.ToInt32(await exists.ExecuteScalarAsync(ct)) == 1;

        if (!existsResult)
        {
            const string createSql = """
                CREATE TABLE [dbo].[Classes] (
                    [Record] INT NOT NULL PRIMARY KEY,
                    [Name] NVARCHAR(128) NOT NULL,
                    [Description] NVARCHAR(500) NULL,
                    [Available] BIT NOT NULL CONSTRAINT [DF_Classes_Available] DEFAULT (1),
                    [TableName] NVARCHAR(256) NULL
                )
                """;
            using var create = new SqlCommand(createSql, connection);
            await create.ExecuteNonQueryAsync(ct);
            return;
        }

        var columns = await GetColumnsAsync(connection, ct);
        if (!columns.Contains("Record") && !columns.Contains("ClassNumber"))
            throw new InvalidOperationException(
                "dbo.Classes существует, но в ней нет колонки Record или ClassNumber для номера класса.");

        if (!columns.Contains("Name"))
            throw new InvalidOperationException("dbo.Classes существует, но в ней нет колонки Name.");
    }

    public async Task<int> GetNextClassNumberAsync(CancellationToken ct = default)
    {
        await EnsureRegistryAsync(ct);

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        string numberColumn = await ResolveNumberColumnAsync(connection, ct);
        using var command = new SqlCommand(
            $"SELECT ISNULL(MAX([{numberColumn}]), 0) + 1 FROM [dbo].[Classes]",
            connection);

        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    public async Task<Dictionary<int, bool>> GetAvailabilityAsync(CancellationToken ct = default)
    {
        await EnsureRegistryAsync(ct);
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        string numberColumn = await ResolveNumberColumnAsync(connection, ct);
        var columns = await GetColumnsAsync(connection, ct);
        if (!columns.Contains("Available")) return new Dictionary<int, bool>();
        var result = new Dictionary<int, bool>();
        using var command = new SqlCommand($"SELECT [{numberColumn}], [Available] FROM [dbo].[Classes]", connection);
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (reader.IsDBNull(0)) continue;
            int number = Convert.ToInt32(reader.GetValue(0));
            bool available = reader.IsDBNull(1) || Convert.ToBoolean(reader.GetValue(1));
            result[number] = available;
        }
        return result;
    }

    public async Task UpsertAsync(ClassDefinition definition, CancellationToken ct = default)
    {
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        if (definition.ClassNumber <= 0) throw new InvalidOperationException("У класса должен быть положительный Class Number.");

        await EnsureRegistryAsync(ct);

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        string numberColumn = await ResolveNumberColumnAsync(connection, ct);
        var columns = await GetColumnsAsync(connection, ct);

        string sql = $"""
            IF EXISTS (SELECT 1 FROM [dbo].[Classes] WHERE [{numberColumn}] = @Number)
                UPDATE [dbo].[Classes]
                   SET [Name] = @Name
                      {(columns.Contains("Description") ? ", [Description] = @Description" : "")}
                      {(columns.Contains("Available") ? ", [Available] = @Available" : "")}
                      {(columns.Contains("TableName") ? ", [TableName] = @TableName" : "")}
                 WHERE [{numberColumn}] = @Number
            ELSE
                INSERT INTO [dbo].[Classes] ([{numberColumn}], [Name]
                    {(columns.Contains("Description") ? ", [Description]" : "")}
                    {(columns.Contains("Available") ? ", [Available]" : "")}
                    {(columns.Contains("TableName") ? ", [TableName]" : "")})
                VALUES (@Number, @Name
                    {(columns.Contains("Description") ? ", @Description" : "")}
                    {(columns.Contains("Available") ? ", @Available" : "")}
                    {(columns.Contains("TableName") ? ", @TableName" : "")})
            """;

        using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Number", SqlDbType.Int).Value = definition.ClassNumber;
        command.Parameters.Add("@Name", SqlDbType.NVarChar, 128).Value = definition.Name ?? "";

        if (columns.Contains("Description"))
            command.Parameters.Add("@Description", SqlDbType.NVarChar, 500).Value =
                (object)definition.Description ?? DBNull.Value;

        if (columns.Contains("Available"))
            command.Parameters.Add("@Available", SqlDbType.Bit).Value = definition.Available;

        if (columns.Contains("TableName"))
            command.Parameters.Add("@TableName", SqlDbType.NVarChar, 256).Value =
                (object)(definition.PrimaryStorage?.TableName) ?? DBNull.Value;

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task RemoveAsync(ClassDefinition definition, CancellationToken ct = default)
    {
        if (definition == null || definition.ClassNumber <= 0) return;
        await EnsureRegistryAsync(ct);

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        string numberColumn = await ResolveNumberColumnAsync(connection, ct);
        using var command = new SqlCommand(
            $"DELETE FROM [dbo].[Classes] WHERE [{numberColumn}] = @Number",
            connection);
        command.Parameters.Add("@Number", SqlDbType.Int).Value = definition.ClassNumber;
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<HashSet<string>> GetColumnsAsync(SqlConnection connection, CancellationToken ct)
    {
        const string sql = """
            SELECT c.name
            FROM sys.columns c
            INNER JOIN sys.tables t ON t.object_id = c.object_id
            INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = N'dbo' AND t.name = N'Classes'
            """;

        using var command = new SqlCommand(sql, connection);
        using var reader = await command.ExecuteReaderAsync(ct);

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(ct))
            result.Add(Convert.ToString(reader[0]) ?? "");
        return result;
    }

    private static async Task<string> ResolveNumberColumnAsync(SqlConnection connection, CancellationToken ct)
    {
        var columns = await GetColumnsAsync(connection, ct);
        if (columns.Contains("Record")) return "Record";
        if (columns.Contains("ClassNumber")) return "ClassNumber";
        throw new InvalidOperationException("dbo.Classes не содержит Record/ClassNumber.");
    }
}

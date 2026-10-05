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
            public async Task TestConnectionAsync(CancellationToken ct = default)
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(ct);
            }
    
            // === P1-11: Async ===

    public async Task BackupDatabaseAsync(string databaseName, string backupPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
            throw new ArgumentException("База данных не задана.", nameof(databaseName));
        if (string.IsNullOrWhiteSpace(backupPath))
            throw new ArgumentException("Путь резервной копии не задан.", nameof(backupPath));

        string destination = System.IO.Path.GetFullPath(backupPath);
        string destinationDirectory = System.IO.Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
            throw new InvalidOperationException("Не удалось определить каталог экспорта.");
        System.IO.Directory.CreateDirectory(destinationDirectory);

        var builder = new SqlConnectionStringBuilder(_connectionString) { InitialCatalog = "master" };
        using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(ct);

        string defaultBackupDirectory = await GetSqlServerDefaultBackupDirectoryAsync(connection, ct);
        if (string.IsNullOrWhiteSpace(defaultBackupDirectory))
            throw new InvalidOperationException("SQL Server не сообщил стандартный каталог резервных копий. Выберите доступную SQL Server сетевую папку для экспорта.");

        string stagingFile = System.IO.Path.Combine(
            defaultBackupDirectory,
            $"Configurator_{SanitizeFileName(databaseName)}_{Guid.NewGuid():N}.bak");

        string sql = $"BACKUP DATABASE {QuoteIdentifier(databaseName)} TO DISK = N'{EscapeSqlLiteral(stagingFile)}' WITH COPY_ONLY, INIT, STATS = 10";
        try
        {
            using (var command = new SqlCommand(sql, connection) { CommandTimeout = 0 })
                await command.ExecuteNonQueryAsync(ct);

            await CopyServerFileToClientAsync(connection, stagingFile, destination, ct);
        }
        catch (SqlException ex) when (ex.Number == 3201 || ex.Number == 5)
        {
            throw new InvalidOperationException(
                "SQL Server не смог создать резервную копию в своём каталоге backup. Проверьте права службы SQL Server.", ex);
        }
        catch (SqlException ex) when (ex.Message.Contains("OPENROWSET", StringComparison.OrdinalIgnoreCase) ||
                                       ex.Message.Contains("Ad Hoc Distributed Queries", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "SQL Server создал резервную копию, но не разрешил приложению прочитать файл через OPENROWSET. В этом случае укажите путь экспорта, доступный службе SQL Server, или включите поддержку BULK OPENROWSET для учетной записи подключения.", ex);
        }
        finally
        {
            // Файл находится в каталоге SQL Server. Обычный пользователь Windows
            // может не иметь права удалить его, поэтому не пытаемся удалять его
            // локальным File.Delete и не запрашиваем права администратора.
        }
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Database";

        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars);
    }

    private static async Task<string> GetSqlServerDefaultBackupDirectoryAsync(SqlConnection connection, CancellationToken ct)
    {
        const string sql = "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000))";
        using var command = new SqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync(ct);
        return value == null || value == DBNull.Value ? "" : Convert.ToString(value) ?? "";
    }

    private static async Task CopyServerFileToClientAsync(SqlConnection connection, string serverPath, string destinationPath, CancellationToken ct)
    {
        string query = $"SELECT BulkColumn FROM OPENROWSET(BULK N'{EscapeSqlLiteral(serverPath)}', SINGLE_BLOB) AS BackupFile";
        using var command = new SqlCommand(query, connection) { CommandTimeout = 0 };
        using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
        if (!await reader.ReadAsync(ct))
            throw new InvalidOperationException("SQL Server не вернул содержимое резервной копии.");

        await using var output = new System.IO.FileStream(
            destinationPath,
            System.IO.FileMode.Create,
            System.IO.FileAccess.Write,
            System.IO.FileShare.None,
            1024 * 1024,
            System.IO.FileOptions.Asynchronous | System.IO.FileOptions.SequentialScan);

        var buffer = new byte[1024 * 1024];
        long offset = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            long read = reader.GetBytes(0, offset, buffer, 0, buffer.Length);
            if (read <= 0) break;
            await output.WriteAsync(buffer.AsMemory(0, checked((int)read)), ct);
            offset += read;
        }
        await output.FlushAsync(ct);
    }

    public async Task<int> ClearTableAsync(string tableName, CancellationToken ct = default)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        using var command = new SqlCommand($"DELETE FROM {QuoteTableName(tableName)}", connection);
        return await command.ExecuteNonQueryAsync(ct);
    }

            public async Task<List<string>> GetDatabaseListAsync(CancellationToken ct = default)
            {
                var databases = new List<string>();
    
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(ct);
    
                var query = @"
                    SELECT name
                    FROM sys.databases
                    WHERE state_desc = 'ONLINE' AND database_id > 4
                    ORDER BY name";
    
                using var command = new SqlCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync(ct);
    
                while (await reader.ReadAsync(ct))
                {
                    databases.Add(reader.GetString(0));
                }
    
                return databases;
            }
    }
}

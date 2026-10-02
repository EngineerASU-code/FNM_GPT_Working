using System;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Configurator
{
    public partial class DatabaseService
    {
        public async Task<(DataTable Result, int AffectedRows)> ExecuteFreeSqlAsync(
            string sql, string expectedDatabase = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(sql))
                throw new InvalidOperationException("SQL-запрос пуст.");

            // Свободный ввод не должен незаметно переключать контекст на другую БД.
            // Пользователь выбирает БД в UI, поэтому USE запрещён.
            var statements = sql.Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (statements.Any(s => s.TrimStart().StartsWith("USE ", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException(
                    "Команда USE запрещена в свободном вводе. Выберите нужную базу данных в списке сверху.");

            // Не позволяем случайно обратиться к другой БД через трёх- или
            // четырёхчастное имя [Database].[schema].[table].
            const string crossDatabasePattern =
                @"(?<![A-Za-z0-9_])(?:\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_$#@]*)\s*\.\s*" +
                @"(?:\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_$#@]*)\s*\.\s*" +
                @"(?:\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_$#@]*)";

            if (Regex.IsMatch(sql, crossDatabasePattern, RegexOptions.IgnoreCase))
                throw new InvalidOperationException(
                    "Свободный ввод работает только в выбранной базе данных. " +
                    "Обращение к другой БД через трёхчастные имена запрещено.");

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            if (!string.IsNullOrWhiteSpace(expectedDatabase) &&
                !string.Equals(connection.Database, expectedDatabase, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Не удалось установить контекст базы «{expectedDatabase}». " +
                    $"Текущий контекст SQL Server: «{connection.Database}».");
            }

            using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };

            string normalized = sql.TrimStart();
            bool expectsResult =
                normalized.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("WITH", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("EXEC", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("EXECUTE", StringComparison.OrdinalIgnoreCase);

            if (!expectsResult)
            {
                int affected = await command.ExecuteNonQueryAsync(ct);
                return (null, affected);
            }

            using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
            var table = new DataTable();
            table.Load(reader);
            return (table, -1);
        }
    }
}
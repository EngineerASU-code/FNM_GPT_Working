using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Configurator;

/// <summary>
/// Allocates Record values according to the Configurator architecture.
/// The first unused positive Record is selected, not MAX(Record)+1.
/// The architectural upper bound is 65535, additionally limited by the SQL column type.
/// </summary>
public sealed class RecordAllocationService
{
    public const int ArchitecturalMinRecord = 1;
    public const int ArchitecturalMaxRecord = 65535;

    private readonly string _connectionString;

    public RecordAllocationService(string connectionString)
        => _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));

    public async Task<RecordAllocationResult> FindFirstFreeAsync(
        string tableName,
        int? plc,
        int? plcClassNumber,
        int? excludeRecord = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Не задана таблица.", nameof(tableName));

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var recordType = await GetRecordTypeAsync(connection, tableName, cancellationToken);
        int maxRecord = Math.Min(ArchitecturalMaxRecord, GetSqlIntegralMax(recordType));
        if (maxRecord < ArchitecturalMinRecord)
            throw new InvalidOperationException($"Поле Record в таблице {tableName} имеет неподдерживаемый диапазон.");

        var columns = await GetColumnNamesAsync(connection, tableName, cancellationToken);
        if (!columns.Contains("Record", StringComparer.OrdinalIgnoreCase))
            return new RecordAllocationResult(null, maxRecord, Array.Empty<int>(), "В таблице отсутствует поле Record.");

        var where = new List<string>();
        await using var command = new SqlCommand { Connection = connection };
        AddContextFilter(command, where, columns, "PLC", plc, "plc");
        AddContextFilter(command, where, columns, "PLC_Class_Number", plcClassNumber, "classNumber");

        command.CommandText = $"SELECT [Record] FROM {QuoteTable(tableName)}" +
                              (where.Count == 0 ? "" : " WHERE " + string.Join(" AND ", where)) +
                              " ORDER BY [Record];";

        var used = new HashSet<int>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(0)) continue;
            if (!int.TryParse(Convert.ToString(reader.GetValue(0)), out var record)) continue;
            if (record >= ArchitecturalMinRecord && record <= maxRecord && record != excludeRecord)
                used.Add(record);
        }

        var first = Enumerable.Range(ArchitecturalMinRecord, maxRecord)
            .FirstOrDefault(x => !used.Contains(x));
        int? firstFree = first == 0 ? null : first;

        var summary = Enumerable.Range(ArchitecturalMinRecord, maxRecord)
            .Where(x => !used.Contains(x))
            .Take(12)
            .ToArray();

        return new RecordAllocationResult(firstFree, maxRecord, summary,
            firstFree.HasValue ? "Первый свободный Record найден." : "Свободных Record в допустимом диапазоне нет.");
    }

    public async Task<RecordValidationResult> ValidateAsync(
        string tableName,
        int record,
        int? plc,
        int? plcClassNumber,
        IReadOnlyDictionary<string, object> currentIdentity = null,
        CancellationToken cancellationToken = default)
    {
        if (record < ArchitecturalMinRecord || record > ArchitecturalMaxRecord)
            return RecordValidationResult.Invalid($"Record должен находиться в диапазоне {ArchitecturalMinRecord}..{ArchitecturalMaxRecord}.");

        var allocation = await FindFirstFreeAsync(tableName, plc, plcClassNumber, record, cancellationToken);
        if (record > allocation.MaxRecord)
            return RecordValidationResult.Invalid($"Record {record} выходит за границы архитектуры таблицы (максимум {allocation.MaxRecord}).");

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var columns = await GetColumnNamesAsync(connection, tableName, cancellationToken);
        var where = new List<string> { "[Record] = @record" };
        await using var command = new SqlCommand { Connection = connection };
        command.Parameters.AddWithValue("@record", record);
        AddContextFilter(command, where, columns, "PLC", plc, "plc");
        AddContextFilter(command, where, columns, "PLC_Class_Number", plcClassNumber, "classNumber");

        if (currentIdentity != null)
        {
            foreach (var pair in currentIdentity)
            {
                if (!columns.Contains(pair.Key, StringComparer.OrdinalIgnoreCase)) continue;
                if (pair.Key.Equals("Record", StringComparison.OrdinalIgnoreCase)) continue;
                var parameter = "@identity_" + pair.Key.Replace(" ", "_");
                where.Add($"[{pair.Key.Replace("]", "]]" )}] <> {parameter}");
                command.Parameters.AddWithValue(parameter, pair.Value ?? DBNull.Value);
            }
        }

        command.CommandText = $"SELECT TOP 1 1 FROM {QuoteTable(tableName)} WHERE {string.Join(" AND ", where)};";
        var exists = await command.ExecuteScalarAsync(cancellationToken) != null;
        return exists
            ? RecordValidationResult.Invalid($"Record {record} уже занят в текущем PLC/классе.")
            : RecordValidationResult.Valid();
    }

    private static void AddContextFilter(SqlCommand command, List<string> where, IReadOnlyCollection<string> columns, string column, int? value, string suffix)
    {
        if (!value.HasValue || !columns.Contains(column, StringComparer.OrdinalIgnoreCase)) return;
        var parameter = "@" + suffix;
        where.Add($"[{column}] = {parameter}");
        command.Parameters.AddWithValue(parameter, value.Value);
    }

    private static async Task<string> GetRecordTypeAsync(SqlConnection connection, string tableName, CancellationToken ct)
    {
        var parts = SplitTableName(tableName);
        const string sql = @"SELECT ty.name FROM sys.tables t
JOIN sys.schemas s ON s.schema_id=t.schema_id
JOIN sys.columns c ON c.object_id=t.object_id
JOIN sys.types ty ON ty.user_type_id=c.user_type_id
WHERE s.name=@schema AND t.name=@table AND c.name='Record';";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@schema", parts.Schema);
        command.Parameters.AddWithValue("@table", parts.Table);
        return Convert.ToString(await command.ExecuteScalarAsync(ct)) ?? "int";
    }

    private static int GetSqlIntegralMax(string dataType) => dataType.ToLowerInvariant() switch
    {
        "tinyint" => 255,
        "smallint" => 32767,
        "int" => ArchitecturalMaxRecord,
        "bigint" => ArchitecturalMaxRecord,
        _ => 0
    };

    private static async Task<HashSet<string>> GetColumnNamesAsync(SqlConnection connection, string tableName, CancellationToken ct)
    {
        var parts = SplitTableName(tableName);
        const string sql = @"SELECT c.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id JOIN sys.columns c ON c.object_id=t.object_id WHERE s.name=@schema AND t.name=@table;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@schema", parts.Schema);
        command.Parameters.AddWithValue("@table", parts.Table);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(Convert.ToString(reader[0]) ?? "");
        return result;
    }

    private static (string Schema, string Table) SplitTableName(string name)
    {
        var parts = name.Split('.', 2);
        return parts.Length == 2 ? (parts[0], parts[1]) : ("dbo", parts[0]);
    }

    private static string QuoteTable(string fullName)
    {
        var parts = SplitTableName(fullName);
        return $"[{parts.Schema.Replace("]", "]]" )}].[{parts.Table.Replace("]", "]]" )}]";
    }
}

public sealed record RecordAllocationResult(int? FirstFreeRecord, int MaxRecord, IReadOnlyList<int> SuggestedRecords, string Message);

public sealed record RecordValidationResult(bool IsValid, string Message)
{
    public static RecordValidationResult Valid() => new(true, "Record допустим и свободен.");
    public static RecordValidationResult Invalid(string message) => new(false, message);
}

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Configurator.Core.Architecture;
using Microsoft.Data.SqlClient;

namespace Configurator;

/// <summary>Destructive-operation guard: plan → replace → validate → delete, all inside one transaction.</summary>
public sealed class DeletionProtectionService
{
    private readonly string _connectionString;
    private readonly DatabaseSchema _schema;
    public DeletionProtectionService(string connectionString, DatabaseSchema schema) { _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString)); _schema = schema ?? throw new ArgumentNullException(nameof(schema)); }

    public async Task<DeletionPlan> BuildPlanAsync(DeletionRequest request, CancellationToken cancellationToken = default)
    {
        var plan = new DeletionPlan { Request = request };
        foreach (var relation in _schema.Relations.Where(r => r.TargetTable.Equals(request.TableName, StringComparison.OrdinalIgnoreCase) && r.IsUsableForDeletionProtection))
        {
            if (relation.SourceTable.Equals(request.TableName, StringComparison.OrdinalIgnoreCase)) continue;
            plan.Dependencies.AddRange(await FindDependentsAsync(relation, request.Key, cancellationToken));
        }
        return plan;
    }

    public async Task ExecuteAsync(DeletionPlan plan, IReadOnlyList<ReferenceReplacement> replacements, Func<SqlConnection, SqlTransaction, CancellationToken, Task<bool>> validateAfterReplacement, CancellationToken cancellationToken = default)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        if (validateAfterReplacement == null) throw new ArgumentNullException(nameof(validateAfterReplacement));
        if (!plan.CanCommit) throw new InvalidOperationException("Удаление заблокировано: план содержит ошибки или неподдерживаемые зависимости.");
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var replacement in replacements)
            {
                if (replacement.KeyColumns.Count == 0) throw new InvalidOperationException($"Для зависимости {replacement.SourceTable} не найден безопасный ключ строки. Удаление остановлено.");
                await ApplyReplacementAsync(connection, transaction, replacement, cancellationToken);
            }
            if (!await validateAfterReplacement(connection, transaction, cancellationToken)) throw new InvalidOperationException("Проверка после переинициализации не пройдена. Изменения откатываются.");
            await DeleteTargetAsync(connection, transaction, plan.Request, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }

    private async Task<List<DependencyHit>> FindDependentsAsync(DatabaseRelation relation, IReadOnlyDictionary<string, object> targetKey, CancellationToken ct)
    {
        if (relation.TargetColumns.Count == 0 || relation.TargetColumns.Count != relation.SourceColumns.Count) return new List<DependencyHit>();
        await using var connection = new SqlConnection(_connectionString); await connection.OpenAsync(ct);
        var where = new List<string>(); await using var command = new SqlCommand { Connection = connection };
        for (var i = 0; i < relation.TargetColumns.Count; i++)
        {
            if (!targetKey.TryGetValue(relation.TargetColumns[i], out var value)) return new List<DependencyHit>();
            var p = "@p" + i; where.Add($"{QuoteColumn(relation.SourceTable, relation.SourceColumns[i])} = {p}"); command.Parameters.AddWithValue(p, value ?? DBNull.Value);
        }
        command.CommandText = $"SELECT * FROM {QuoteTable(relation.SourceTable)} WHERE {string.Join(" AND ", where)}";
        var hits = new List<DependencyHit>(); await using var reader = await command.ExecuteReaderAsync(ct);
        var schemaTable = reader.GetSchemaTable();
        var available = schemaTable?.Rows.Cast<DataRow>().Select(r => Convert.ToString(r["ColumnName"])).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sourceTable = _schema.FindTable(relation.SourceTable);
        var primary = sourceTable?.Keys.FirstOrDefault(k => k.IsPrimary)?.Columns ?? new List<string>();
        var fallback = primary.Count > 0 ? primary : new[] { "PLC", "PLC_Class_Number", "Record" }.Where(available.Contains).ToList();
        while (await reader.ReadAsync(ct))
        {
            var sourceKey = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var keyColumn in fallback)
                if (available.Contains(keyColumn)) sourceKey[keyColumn] = reader[keyColumn] == DBNull.Value ? null : reader[keyColumn];
            hits.Add(new DependencyHit { SourceTable = relation.SourceTable, SourceKey = sourceKey, SourceColumnDescription = string.Join(", ", relation.SourceColumns), TargetTable = relation.TargetTable, Relation = relation });
        }
        return hits;
    }

    private static async Task ApplyReplacementAsync(SqlConnection connection, SqlTransaction transaction, ReferenceReplacement replacement, CancellationToken ct)
    {
        if (replacement.SourceColumns.Count != replacement.NewValues.Count || replacement.KeyColumns.Count != replacement.KeyValues.Count) throw new ArgumentException("Количество полей и значений в замене не совпадает.");
        var set = new List<string>(); var where = new List<string>(); await using var command = new SqlCommand { Connection = connection, Transaction = transaction };
        for (var i = 0; i < replacement.SourceColumns.Count; i++) { var p = "@new" + i; set.Add($"{QuoteColumn(replacement.SourceTable, replacement.SourceColumns[i])} = {p}"); command.Parameters.AddWithValue(p, replacement.NewValues[i] ?? DBNull.Value); }
        for (var i = 0; i < replacement.KeyColumns.Count; i++) { var p = "@key" + i; where.Add($"{QuoteColumn(replacement.SourceTable, replacement.KeyColumns[i])} = {p}"); command.Parameters.AddWithValue(p, replacement.KeyValues[i] ?? DBNull.Value); }
        command.CommandText = $"UPDATE {QuoteTable(replacement.SourceTable)} SET {string.Join(", ", set)} WHERE {string.Join(" AND ", where)}";
        int affected = await command.ExecuteNonQueryAsync(ct); if (affected != 1) throw new InvalidOperationException($"Замена зависимости в {replacement.SourceTable} затронула {affected} строк. Транзакция откатывается.");
    }

    private static async Task DeleteTargetAsync(SqlConnection connection, SqlTransaction transaction, DeletionRequest request, CancellationToken ct)
    {
        await using var command = new SqlCommand { Connection = connection, Transaction = transaction }; var where = new List<string>(); var i = 0;
        foreach (var pair in request.Key) { var p = "@key" + i++; where.Add($"{QuoteColumn(request.TableName, pair.Key)} = {p}"); command.Parameters.AddWithValue(p, pair.Value ?? DBNull.Value); }
        command.CommandText = $"DELETE FROM {QuoteTable(request.TableName)} WHERE {string.Join(" AND ", where)}";
        int affected = await command.ExecuteNonQueryAsync(ct); if (affected != 1) throw new InvalidOperationException($"Удаление затронуло {affected} строк. Транзакция откатывается.");
    }

    private static string QuoteTable(string fullName) { var parts = fullName.Split('.', 2); var schema = parts.Length == 2 ? parts[0] : "dbo"; var table = parts.Length == 2 ? parts[1] : parts[0]; return $"[{schema.Replace("]", "]]" )}].[{table.Replace("]", "]]" )}]"; }
    private static string QuoteColumn(string table, string column) => $"{QuoteTable(table)}.[{column.Replace("]", "]]" )}]";
}

public sealed class ReferenceReplacement
{
    public string SourceTable { get; init; } = "";
    public IReadOnlyList<string> SourceColumns { get; init; } = Array.Empty<string>();
    public IReadOnlyList<object> NewValues { get; init; } = Array.Empty<object>();
    public IReadOnlyList<string> KeyColumns { get; init; } = Array.Empty<string>();
    public IReadOnlyList<object> KeyValues { get; init; } = Array.Empty<object>();
}

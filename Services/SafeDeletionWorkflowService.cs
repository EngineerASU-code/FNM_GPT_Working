using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Configurator.Core.Architecture;
using Microsoft.Data.SqlClient;

namespace Configurator;

/// <summary>UI-independent deletion workflow: discover dependencies, request replacements, validate inside the transaction, then delete.</summary>
public sealed class SafeDeletionWorkflowService
{
    private readonly string _connectionString;

    public SafeDeletionWorkflowService(string connectionString) => _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));

    public async Task<bool> TryDeleteAsync(string tableName, IReadOnlyDictionary<string, object> row, Window owner, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tableName) || row == null || row.Count == 0) return false;
        var schema = await new DatabaseArchitectureAnalyzer(_connectionString).AnalyzeAsync(ct);
        var request = BuildRequest(schema, tableName, row);
        if (request == null)
        {
            MessageBox.Show("Для этой строки не удалось определить безопасный ключ удаления. Удаление остановлено, чтобы не затронуть лишние записи.", "Безопасное удаление", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var protection = new DeletionProtectionService(_connectionString, schema);
        var plan = await protection.BuildPlanAsync(request, ct);
        if (plan.Dependencies.Count == 0)
        {
            if (MessageBox.Show($"Удалить запись из {tableName}?\n\nЗависимостей не найдено.", "Удаление", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return false;
            await protection.ExecuteAsync(plan, Array.Empty<ReferenceReplacement>(), (_, _, _) => Task.FromResult(true), ct);
            return true;
        }

        var unsupported = plan.Dependencies.Where(x => x.Relation.SourceColumns.Count != 1).ToList();
        if (unsupported.Count > 0)
        {
            MessageBox.Show("Найдена составная зависимость, для которой пока нельзя безопасно сформировать одиночную замену. Удаление заблокировано. Это защита от частичного изменения Program/Matrix и других сложных связей.", "Удаление заблокировано", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var dialog = new DependencyReplacementWindow(plan, owner);
        if (dialog.ShowDialog() != true || !dialog.Confirmed) return false;

        var replacements = new List<ReferenceReplacement>();
        foreach (var group in plan.Dependencies.GroupBy(GroupKey, StringComparer.OrdinalIgnoreCase))
        {
            var item = dialog.Items.FirstOrDefault(x => x.SourceTable.Equals(group.First().SourceTable, StringComparison.OrdinalIgnoreCase) && x.RuleId.Equals(group.First().Relation.RuleId, StringComparison.OrdinalIgnoreCase));
            if (item == null || string.IsNullOrWhiteSpace(item.NewValue)) return false;
            var sourceColumn = group.First().Relation.SourceColumns[0];
            object converted = await ConvertValueAsync(group.First().SourceTable, sourceColumn, item.NewValue, ct);
            foreach (var hit in group)
                replacements.Add(new ReferenceReplacement
                {
                    SourceTable = hit.SourceTable,
                    SourceColumns = hit.Relation.SourceColumns.ToArray(),
                    NewValues = new[] { converted },
                    KeyColumns = hit.SourceKey.Keys.ToArray(),
                    KeyValues = hit.SourceKey.Values.ToArray()
                });
        }

        await protection.ExecuteAsync(plan, replacements, (connection, transaction, token) => ValidateAsync(schema, plan, replacements, connection, transaction, token), ct);
        return true;
    }

    private static string GroupKey(DependencyHit hit) => hit.SourceTable + "|" + hit.Relation.RuleId + "|" + string.Join(",", hit.Relation.SourceColumns);

    private static DeletionRequest BuildRequest(DatabaseSchema schema, string tableName, IReadOnlyDictionary<string, object> row)
    {
        var table = schema.FindTable(tableName) ?? schema.Tables.FirstOrDefault(t => t.Name.Equals(tableName.Split('.').Last(), StringComparison.OrdinalIgnoreCase));
        if (table == null) return null;
        var key = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        // Area is referenced by its business value, Areas.AreaName, not Areas.Record.
        if (table.Name.Equals("Areas", StringComparison.OrdinalIgnoreCase) && row.ContainsKey("AreaName"))
            key["AreaName"] = row["AreaName"];
        else
        {
            var primary = table.Keys.FirstOrDefault(k => k.IsPrimary)?.Columns ?? new List<string>();
            foreach (var column in primary)
                if (row.ContainsKey(column)) key[column] = row[column];
            if (key.Count == 0)
            {
                foreach (var column in new[] { "PLC", "PLC_Class_Number", "Record" })
                    if (row.ContainsKey(column)) key[column] = row[column];
            }
        }
        return key.Count == 0 ? null : new DeletionRequest { TableName = table.FullName, Key = key };
    }

    private static async Task<object> ConvertValueAsync(string tableName, string columnName, string value, CancellationToken ct)
    {
        var connectionString = string.Empty;
        // Source type conversion is intentionally conservative: SQL Server accepts the textual representation
        // for normal numeric/string columns, while the validation query below remains authoritative.
        if (int.TryParse(value, out int intValue)) return intValue;
        if (long.TryParse(value, out long longValue)) return longValue;
        if (Guid.TryParse(value, out Guid guid)) return guid;
        return value;
    }

    private static async Task<bool> ValidateAsync(DatabaseSchema schema, DeletionPlan plan, IReadOnlyList<ReferenceReplacement> replacements, SqlConnection connection, SqlTransaction transaction, CancellationToken ct)
    {
        foreach (var replacement in replacements)
        {
            var relation = plan.Dependencies.FirstOrDefault(x => x.SourceTable.Equals(replacement.SourceTable, StringComparison.OrdinalIgnoreCase) && x.Relation.SourceColumns.SequenceEqual(replacement.SourceColumns, StringComparer.OrdinalIgnoreCase))?.Relation;
            if (relation == null) return false;
            if (relation.TargetColumns.Count != 1 || relation.SourceColumns.Count != 1) return false;

            await using (var targetCommand = new SqlCommand($"SELECT TOP 1 1 FROM {QuoteTable(relation.TargetTable)} WHERE [{relation.TargetColumns[0]}] = @value", connection, transaction))
            {
                targetCommand.Parameters.AddWithValue("@value", replacement.NewValues[0] ?? DBNull.Value);
                if (await targetCommand.ExecuteScalarAsync(ct) == null)
                    throw new InvalidOperationException($"Новое значение «{replacement.NewValues[0]}» отсутствует в {relation.TargetTable}.{relation.TargetColumns[0]}.");
            }
        }

        foreach (var relationGroup in plan.Dependencies.GroupBy(x => x.Relation.RuleId, StringComparer.OrdinalIgnoreCase))
        {
            var relation = relationGroup.First().Relation;
            var where = new List<string>();
            await using var command = new SqlCommand { Connection = connection, Transaction = transaction };
            for (int i = 0; i < relation.TargetColumns.Count; i++)
            {
                var targetValue = plan.Request.Key.TryGetValue(relation.TargetColumns[i], out var value) ? value : null;
                if (targetValue == null) return false;
                string parameter = "@old" + i;
                where.Add($"[{relation.SourceColumns[i]}] = {parameter}");
                command.Parameters.AddWithValue(parameter, targetValue);
            }
            command.CommandText = $"SELECT COUNT_BIG(*) FROM {QuoteTable(relation.SourceTable)} WHERE {string.Join(" AND ", where)}";
            long remaining = Convert.ToInt64(await command.ExecuteScalarAsync(ct));
            if (remaining > 0) throw new InvalidOperationException($"После замены остались ссылки {relation.SourceTable}.{string.Join(",", relation.SourceColumns)} → удаляемая запись. Транзакция откатывается.");
        }
        return true;
    }

    private static string QuoteTable(string fullName)
    {
        var parts = fullName.Split('.', 2); string schema = parts.Length == 2 ? parts[0] : "dbo"; string table = parts.Length == 2 ? parts[1] : parts[0];
        return $"[{schema.Replace("]", "]]" )}].[{table.Replace("]", "]]" )}]";
    }
}

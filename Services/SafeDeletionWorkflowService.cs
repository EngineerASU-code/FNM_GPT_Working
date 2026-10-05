using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Configurator.Core.Architecture;
using Microsoft.Data.SqlClient;

namespace Configurator;

/// <summary>Plan → replacement dialog → transactional validation → delete. No SQL relationship is created or changed.</summary>
public sealed class SafeDeletionWorkflowService
{
    private readonly string _connectionString;
    public SafeDeletionWorkflowService(string connectionString) => _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));

    public async Task<bool> TryDeleteAsync(string tableName, IReadOnlyDictionary<string, object> row, Window owner, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tableName) || row == null || row.Count == 0) return false;
        var schema = await new DatabaseArchitectureAnalyzer(_connectionString).AnalyzeAsync(ct);
        var request = BuildRequest(schema, tableName, row);
        if (request == null) { MessageBox.Show("Для этой строки не удалось определить безопасный ключ удаления. Удаление остановлено.", "Безопасное удаление", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
        var protection = new DeletionProtectionService(_connectionString, schema);
        var plan = await protection.BuildPlanAsync(request, ct);
        if (plan.Dependencies.Count == 0)
        {
            if (MessageBox.Show($"Удалить запись из {tableName}?\n\nЗависимостей не найдено.", "Удаление", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return false;
            await protection.ExecuteAsync(plan, Array.Empty<ReferenceReplacement>(), (_, _, _) => Task.FromResult(true), ct);
            return true;
        }
        if (plan.Dependencies.Any(x => x.Relation.SourceColumns.Count != 1 || x.SourceKey.Count == 0))
        {
            MessageBox.Show("Найдена зависимость, для которой нельзя однозначно определить строку-источник или она использует составной ключ. Удаление заблокировано, чтобы не изменить Program/Matrix или несколько строк.", "Удаление заблокировано", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        var dialog = new DependencyReplacementWindow(plan, owner);
        if (dialog.ShowDialog() != true || !dialog.Confirmed) return false;

        var replacements = new List<ReferenceReplacement>();
        foreach (var group in plan.Dependencies.GroupBy(GroupKey, StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            var item = dialog.Items.FirstOrDefault(x => x.SourceTable.Equals(first.SourceTable, StringComparison.OrdinalIgnoreCase) && x.RuleId.Equals(first.Relation.RuleId, StringComparison.OrdinalIgnoreCase));
            if (item == null || string.IsNullOrWhiteSpace(item.NewValue)) return false;
            object converted = await ConvertValueAsync(first.SourceTable, first.Relation.SourceColumns[0], item.NewValue, ct);
            foreach (var hit in group)
                replacements.Add(new ReferenceReplacement { SourceTable = hit.SourceTable, SourceColumns = hit.Relation.SourceColumns.ToArray(), NewValues = new[] { converted }, KeyColumns = hit.SourceKey.Keys.ToArray(), KeyValues = hit.SourceKey.Values.ToArray() });
        }

        await protection.ExecuteAsync(plan, replacements, (connection, transaction, token) => ValidateAsync(plan, replacements, connection, transaction, token), ct);
        return true;
    }

    private static string GroupKey(DependencyHit hit) => hit.SourceTable + "|" + hit.Relation.RuleId + "|" + string.Join(",", hit.Relation.SourceColumns);

    private static DeletionRequest BuildRequest(DatabaseSchema schema, string tableName, IReadOnlyDictionary<string, object> row)
    {
        var table = schema.FindTable(tableName) ?? schema.Tables.FirstOrDefault(t => t.Name.Equals(tableName.Split('.').Last(), StringComparison.OrdinalIgnoreCase));
        if (table == null) return null;
        var key = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (table.Name.Equals("Areas", StringComparison.OrdinalIgnoreCase) && row.ContainsKey("AreaName")) key["AreaName"] = row["AreaName"];
        else
        {
            var primary = table.Keys.FirstOrDefault(k => k.IsPrimary)?.Columns ?? new List<string>();
            foreach (var column in primary) if (row.ContainsKey(column)) key[column] = row[column];
            if (key.Count == 0) foreach (var column in new[] { "PLC", "PLC_Class_Number", "Record" }) if (row.ContainsKey(column)) key[column] = row[column];
        }
        return key.Count == 0 ? null : new DeletionRequest { TableName = table.FullName, Key = key };
    }

    private async Task<object> ConvertValueAsync(string tableName, string columnName, string value, CancellationToken ct)
    {
        var column = (await new DatabaseService(_connectionString).GetTableColumnsAsync(tableName, ct)).FirstOrDefault(x => x.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase));
        if (column == null) return value;
        return column.DataType.ToLowerInvariant() switch
        {
            "tinyint" => byte.Parse(value, CultureInfo.InvariantCulture), "smallint" => short.Parse(value, CultureInfo.InvariantCulture), "int" => int.Parse(value, CultureInfo.InvariantCulture), "bigint" => long.Parse(value, CultureInfo.InvariantCulture),
            "decimal" or "numeric" or "money" or "smallmoney" => decimal.Parse(value, CultureInfo.InvariantCulture), "float" => double.Parse(value, CultureInfo.InvariantCulture), "real" => float.Parse(value, CultureInfo.InvariantCulture),
            "bit" => value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("да", StringComparison.OrdinalIgnoreCase), "uniqueidentifier" => Guid.Parse(value),
            _ => value
        };
    }

    private static async Task<bool> ValidateAsync(DeletionPlan plan, IReadOnlyList<ReferenceReplacement> replacements, SqlConnection connection, SqlTransaction transaction, CancellationToken ct)
    {
        foreach (var replacement in replacements)
        {
            var relation = plan.Dependencies.FirstOrDefault(x => x.SourceTable.Equals(replacement.SourceTable, StringComparison.OrdinalIgnoreCase) && x.Relation.SourceColumns.SequenceEqual(replacement.SourceColumns, StringComparer.OrdinalIgnoreCase))?.Relation;
            if (relation == null || relation.TargetColumns.Count != 1 || relation.SourceColumns.Count != 1) return false;
            await using var targetCommand = new SqlCommand($"SELECT TOP 1 1 FROM {QuoteTable(relation.TargetTable)} WHERE [{relation.TargetColumns[0]}] = @value", connection, transaction);
            targetCommand.Parameters.AddWithValue("@value", replacement.NewValues[0] ?? DBNull.Value);
            if (await targetCommand.ExecuteScalarAsync(ct) == null) throw new InvalidOperationException($"Новое значение «{replacement.NewValues[0]}» отсутствует в {relation.TargetTable}.{relation.TargetColumns[0]}.");
        }
        foreach (var relationGroup in plan.Dependencies.GroupBy(x => x.Relation.RuleId, StringComparer.OrdinalIgnoreCase))
        {
            var relation = relationGroup.First().Relation; var where = new List<string>(); await using var command = new SqlCommand { Connection = connection, Transaction = transaction };
            for (int i = 0; i < relation.TargetColumns.Count; i++)
            {
                if (!plan.Request.Key.TryGetValue(relation.TargetColumns[i], out var targetValue)) return false;
                string parameter = "@old" + i; where.Add($"[{relation.SourceColumns[i]}] = {parameter}"); command.Parameters.AddWithValue(parameter, targetValue ?? DBNull.Value);
            }
            command.CommandText = $"SELECT COUNT_BIG(*) FROM {QuoteTable(relation.SourceTable)} WHERE {string.Join(" AND ", where)}";
            if (Convert.ToInt64(await command.ExecuteScalarAsync(ct)) > 0) throw new InvalidOperationException($"После замены остались ссылки {relation.SourceTable}.{string.Join(",", relation.SourceColumns)}. Транзакция откатывается.");
        }
        return true;
    }

    private static string QuoteTable(string fullName) { var parts = fullName.Split('.', 2); string schema = parts.Length == 2 ? parts[0] : "dbo"; string table = parts.Length == 2 ? parts[1] : parts[0]; return $"[{schema.Replace("]", "]]" )}].[{table.Replace("]", "]]" )}]"; }
}

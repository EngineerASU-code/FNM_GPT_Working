using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Configurator;

public sealed partial class ProgramTemplateRepository
{
        private async Task<int> GetNextScopedRecordAsync(string tableName, string recordColumn, Dictionary<string, object> scope, CancellationToken ct)
        {
            var columns = await GetColumnsAsync(tableName, ct);
            string actualRecord = GetActualColumn(columns, recordColumn);
            if (string.IsNullOrEmpty(actualRecord))
                throw new InvalidOperationException($"В {tableName} отсутствует поле {recordColumn}.");

            var predicates = new List<string>();
            var parameters = new List<(string Name, object Value)>();
            int index = 0;
            foreach (var pair in scope)
            {
                string actual = GetActualColumn(columns, pair.Key);
                if (string.IsNullOrEmpty(actual)) continue;
                if (pair.Value == null || pair.Value == DBNull.Value)
                    predicates.Add($"{QuoteIdentifier(actual)} IS NULL");
                else
                {
                    string parameter = "@scope" + index++;
                    predicates.Add($"{QuoteIdentifier(actual)} = {parameter}");
                    parameters.Add((parameter, pair.Value));
                }
            }

            string where = predicates.Count == 0 ? "" : " WHERE " + string.Join(" AND ", predicates);
            string sql = $"SELECT {QuoteIdentifier(actualRecord)} FROM {QuoteTable(tableName)}{where}";
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);
            using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value);

            int max = 0;
            using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (reader.IsDBNull(0)) continue;
                if (int.TryParse(Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                    max = Math.Max(max, number);
            }
            return max + 1;
        }

        private static Dictionary<string, object> BuildSimpleScope(string[] names, object[] values)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Math.Min(names.Length, values.Length); i++)
                result[names[i]] = values[i] ?? DBNull.Value;
            return result;
        }

        private static Dictionary<string, object> BuildScopeFromSource(IReadOnlyList<ColumnInfo> columns, Dictionary<string, object> source, params string[] names)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in names)
            {
                string actual = GetActualColumn(columns, name);
                if (!string.IsNullOrEmpty(actual) && TryGetValue(source, actual, out var value))
                    result[actual] = value;
            }
            return result;
        }

        private static bool TryGetValue(Dictionary<string, object> values, string key, out object value)
        {
            if (values.TryGetValue(key, out value)) return true;

            foreach (var pair in values)
            {
                if (pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        private static void BuildContextPredicates(
            IReadOnlyList<ColumnInfo> columns,
            ProgramContext context,
            out List<string> predicates,
            out List<(string Name, object Value)> parameters)
        {
            predicates = new List<string>();
            parameters = new List<(string Name, object Value)>();
            if (context == null) return;

            AddContextPredicate(columns, predicates, parameters, "PLC", context.PlcValue, "ctx_plc");
            AddContextPredicate(columns, predicates, parameters, "PLC_Class_Prog_Number", context.ClassNumberValue, "ctx_class");
            if (!HasColumn(columns, "PLC_Class_Prog_Number"))
                AddContextPredicate(columns, predicates, parameters, "PLC_Class_Number", context.ClassNumberValue, "ctx_class");
            AddContextPredicate(columns, predicates, parameters, "Prog_Record", context.RecordValue, "ctx_prog");
            AddContextPredicate(columns, predicates, parameters, "Area", context.AreaValue, "ctx_area");
        }

        private static void AddContextPredicate(
            IReadOnlyList<ColumnInfo> columns,
            List<string> predicates,
            List<(string Name, object Value)> parameters,
            string requestedColumn,
            object value,
            string parameterBase)
        {
            if (value == null || !HasColumn(columns, requestedColumn)) return;
            string actual = GetActualColumn(columns, requestedColumn);
            if (value == DBNull.Value)
                predicates.Add($"{QuoteIdentifier(actual)} IS NULL");
            else
            {
                string parameter = "@" + parameterBase;
                int suffix = 1;
                while (parameters.Any(x => x.Name.Equals(parameter, StringComparison.OrdinalIgnoreCase)))
                    parameter = "@" + parameterBase + suffix++;
                predicates.Add($"{QuoteIdentifier(actual)} = {parameter}");
                parameters.Add((parameter, value));
            }
        }

        private async Task<DataTable> QueryDataTableAsync(
            string tableName,
            IReadOnlyList<string> predicates,
            IReadOnlyList<(string Name, object Value)> parameters,
            string order,
            int take,
            CancellationToken ct)
        {
            string where = predicates.Count == 0 ? "" : " WHERE " + string.Join(" AND ", predicates);
            string safeOrder = string.IsNullOrWhiteSpace(order) ? "(SELECT NULL)" : order;
            string sql = $"SELECT TOP ({Math.Clamp(take, 1, 5000)}) * FROM {QuoteTable(tableName)}{where} ORDER BY {safeOrder}";

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);
            using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);

            using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
            var table = new DataTable();
            table.Load(reader);
            return table;
        }

        private static string BuildOrder(IReadOnlyList<ColumnInfo> columns, params string[] preferred)
        {
            var selected = new List<string>();
            foreach (string requested in preferred)
            {
                string actual = GetActualColumn(columns, requested);
                if (!string.IsNullOrEmpty(actual) && selected.All(x => !x.Equals(actual, StringComparison.OrdinalIgnoreCase)))
                    selected.Add(actual);
            }
            if (selected.Count == 0 && columns.Count > 0)
                selected.Add(columns[0].Name);
            return string.Join(", ", selected.Select(QuoteIdentifier));
        }

        private static bool HasColumn(IReadOnlyList<ColumnInfo> columns, string name)
            => columns.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        private static string GetActualColumn(IReadOnlyList<ColumnInfo> columns, string name)
            => columns.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Name ?? "";

        private static object ToDbValue(object value)
        {
            if (value == null || value == DBNull.Value) return DBNull.Value;
            if (value is int) return value;
            if (int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
                return result;
            return value;
        }

        private static string QuoteTable(string tableName)
        {
            var parts = (tableName ?? "dbo").Split('.', 2);
            string schema = parts.Length == 2 ? parts[0] : "dbo";
            string table = parts.Length == 2 ? parts[1] : parts[0];
            return $"{QuoteIdentifier(schema)}.{QuoteIdentifier(table)}";
        }

        private static string QuoteIdentifier(string value)
            => "[" + (value ?? "").Replace("]", "]]", StringComparison.Ordinal) + "]";

        private static string GetString(DataRow row, string column)
        {
            if (!row.Table.Columns.Contains(column) || row[column] == DBNull.Value) return null;
            return Convert.ToString(row[column], CultureInfo.InvariantCulture);
        }

        private ProgramTemplateItem MapProgram(DataRow row)
        {
            var item = new ProgramTemplateItem();
            foreach (DataColumn column in row.Table.Columns)
                item.Values[column.ColumnName] = row[column];
            item.Name = GetString(row, "Name_L1") ?? GetString(row, "Name") ?? $"Программа {GetString(row, "Record")}";
            item.PLC = GetString(row, "PLC") ?? "";
            item.Area = GetString(row, "Area") ?? "";
            item.ClassNumber = GetString(row, "PLC_Class_Number") ?? GetString(row, "PLC_Class_Prog_Number") ?? "";
            item.Record = GetString(row, "Record") ?? "";
            return item;
        }

        private ProgramTemplateItem MapStep(DataRow row)
        {
            var item = new ProgramTemplateItem();
            foreach (DataColumn column in row.Table.Columns)
                item.Values[column.ColumnName] = row[column];
            item.Name = GetString(row, "Name_L1") ?? GetString(row, "Name") ?? $"Шаг {GetString(row, "Record")}";
            item.PLC = GetString(row, "PLC") ?? "";
            item.Area = GetString(row, "Area") ?? "";
            item.ClassNumber = GetString(row, "PLC_Class_Number") ?? GetString(row, "PLC_Class_Prog_Number") ?? "";
            item.Record = GetString(row, "Record") ?? "";
            return item;
        }
    }

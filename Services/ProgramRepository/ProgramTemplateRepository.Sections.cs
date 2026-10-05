using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Configurator;

public sealed partial class ProgramTemplateRepository
{
        public async Task<DataTable> GetSectionDataAsync(
            ProgramSectionDefinition section,
            ProgramContext context,
            string selectedStepNumber = null,
            int take = 1000,
            CancellationToken ct = default)
        {
            if (section == null)
                throw new ArgumentNullException(nameof(section));

            if (section.Key == "steps")
                return await GetFilteredTableAsync(StepTable, context, null, take, ct);

            string tableName = section.TableName;
            var columns = await GetColumnsAsync(tableName, ct);
            if (columns.Count == 0)
                return new DataTable();

            BuildContextPredicates(columns, context, out var predicates, out var parameters);

            if (section.Key == "programParams" && HasColumn(columns, "Step_Number"))
                predicates.Add($"{QuoteIdentifier(GetActualColumn(columns, "Step_Number"))} = 0");

            if (section.Key == "stepParams" && HasColumn(columns, "Step_Number"))
            {
                if (!string.IsNullOrWhiteSpace(selectedStepNumber) && int.TryParse(selectedStepNumber, NumberStyles.Integer, CultureInfo.InvariantCulture, out int selected))
                {
                    predicates.Add($"{QuoteIdentifier(GetActualColumn(columns, "Step_Number"))} = @selectedStep");
                    parameters.Add(("@selectedStep", selected));
                }
                else
                {
                    predicates.Add($"{QuoteIdentifier(GetActualColumn(columns, "Step_Number"))} > 0");
                }
            }

            string order = BuildOrder(columns,
                "PLC", "PLC_Class_Prog_Number", "PLC_Class_Number", "Prog_Record", "Seq_Record", "Step_Number", "Record", "Recipe_Record");
            return await QueryDataTableAsync(tableName, predicates, parameters, order, Math.Clamp(take, 1, 5000), ct);
        }

        private async Task<DataTable> GetFilteredTableAsync(string tableName, ProgramContext context, string stepNumber, int take, CancellationToken ct)
        {
            var columns = await GetColumnsAsync(tableName, ct);
            if (columns.Count == 0) return new DataTable();
            BuildContextPredicates(columns, context, out var predicates, out var parameters);
            if (!string.IsNullOrWhiteSpace(stepNumber) && HasColumn(columns, "Step_Number") && int.TryParse(stepNumber, out var number))
            {
                predicates.Add($"{QuoteIdentifier(GetActualColumn(columns, "Step_Number"))} = @selectedStep");
                parameters.Add(("@selectedStep", number));
            }
            string order = BuildOrder(columns, "PLC", "PLC_Class_Number", "Prog_Record", "Seq_Record", "Step_Number", "Record");
            return await QueryDataTableAsync(tableName, predicates, parameters, order, Math.Clamp(take, 1, 5000), ct);
        }

        public async Task<DataTable> GetTableDataAsync(string tableName, int take = 1000, CancellationToken ct = default)
        {
            var columns = await GetColumnsAsync(tableName, ct);
            if (columns.Count == 0) return new DataTable();
            string order = BuildOrder(columns, "PLC", "PLC_Class_Number", "PLC_Class_Prog_Number", "Prog_Record", "Seq_Record", "Step_Number", "Record");
            return await QueryDataTableAsync(tableName, Array.Empty<string>(), Array.Empty<(string, object)>(), order, Math.Clamp(take, 1, 5000), ct);
        }

        public async Task<List<ColumnInfo>> GetColumnsAsync(string tableName, CancellationToken ct = default)
        {
            if (_columnsCache.TryGetValue(tableName, out var cached))
                return cached;

            var db = new DatabaseService(_connectionString);
            var columns = await db.GetTableColumnsAsync(tableName, ct);
            _columnsCache[tableName] = columns;
            _tableExistsCache[tableName] = columns.Count > 0;
            return columns;
        }

        public async Task<bool> TableExistsAsync(string tableName, CancellationToken ct = default)
        {
            if (_tableExistsCache.TryGetValue(tableName, out bool exists)) return exists;
            return (await GetColumnsAsync(tableName, ct)).Count > 0;
        }

        public async Task<int> GetNextProgramRecordAsync(object plc, object classNumber, CancellationToken ct = default)
            => await GetNextScopedRecordAsync(ProgramTable, "Record", BuildSimpleScope(new[] { "PLC", "PLC_Class_Number" }, new[] { plc, classNumber }), ct);

        public async Task<int> GetNextStepNumberAsync(ProgramContext context, CancellationToken ct = default)
        {
            var columns = await GetColumnsAsync(StepTable, ct);
            string stepColumn = GetActualColumn(columns, "Step_Number");
            if (string.IsNullOrWhiteSpace(stepColumn))
                stepColumn = GetActualColumn(columns, "Record");
            if (string.IsNullOrWhiteSpace(stepColumn))
                throw new InvalidOperationException($"В {StepTable} отсутствует Step_Number/Record.");

            var scope = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in new[] { "PLC", "PLC_Class_Number", "Prog_Record", "Seq_Record" })
            {
                object value = context == null ? null : key.Equals("PLC", StringComparison.OrdinalIgnoreCase) ? context.PlcValue :
                    key.Equals("Prog_Record", StringComparison.OrdinalIgnoreCase) ? context.RecordValue :
                    key.Equals("PLC_Class_Number", StringComparison.OrdinalIgnoreCase) ? context.ClassNumberValue : null;
                if (value != null && value != DBNull.Value && HasColumn(columns, key))
                    scope[key] = value;
            }
            return await GetNextScopedRecordAsync(StepTable, stepColumn, scope, ct);
        }

        /// <summary>
        /// Полное дублирование программы. Использует штатную процедуру БД Prog_Duplicate,
        /// которая переносит Prog, Seq, StepTransition, Recipe, StepParam, Recipes,
        /// SoftKey, Msg, Queue и QueueSelections одним логическим пакетом.
        /// </summary>
}

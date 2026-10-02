using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Configurator;

public sealed partial class ProgramTemplateRepository
{
        public async Task DuplicateProgramAsync(ProgramContext context, int newRecord, CancellationToken ct = default)
        {
            if (context?.PlcValue == null || context.ClassNumberValue == null || context.RecordValue == null)
                throw new InvalidOperationException("Не удалось определить PLC, номер класса и Record программы.");

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);
            using var command = new SqlCommand("dbo.Prog_Duplicate", connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 180
            };
            command.Parameters.Add("@PLC", SqlDbType.Int).Value = ToDbValue(context.PlcValue);
            command.Parameters.Add("@PLC_Class_Prog_Number", SqlDbType.Int).Value = ToDbValue(context.ClassNumberValue);
            command.Parameters.Add("@Prog_Number", SqlDbType.Int).Value = ToDbValue(context.RecordValue);
            command.Parameters.Add("@PLC_New", SqlDbType.Int).Value = ToDbValue(context.PlcValue);
            command.Parameters.Add("@PLC_Class_Prog_Number_New", SqlDbType.Int).Value = ToDbValue(context.ClassNumberValue);
            command.Parameters.Add("@Prog_Number_New", SqlDbType.Int).Value = newRecord;
            await command.ExecuteNonQueryAsync(ct);
        }

        /// <summary>
        /// Дублирование шага как класса/строки. Связанные параметры и переходы в данной
        /// версии остаются общими для класса шага, поэтому мы не создаём ложных копий.
        /// </summary>
        public async Task<int> DuplicateStepAsync(Dictionary<string, object> source, CancellationToken ct = default)
        {
            if (source == null || source.Count == 0)
                throw new InvalidOperationException("Не удалось определить исходный шаг.");

            var columns = await GetColumnsAsync(StepTable, ct);
            var copyColumns = columns.Where(c => !c.IsIdentity && !c.IsComputed && !c.IsReadOnly).Select(c => c.Name).ToList();
            string recordName = GetActualColumn(columns, "Record");
            if (string.IsNullOrEmpty(recordName))
                throw new InvalidOperationException("В dbo.Prog_Step отсутствует Record — дублирование невозможно.");

            var scope = BuildScopeFromSource(columns, source, "PLC", "PLC_Class_Number", "Prog_Record", "Seq_Record");
            int newRecord = await GetNextScopedRecordAsync(StepTable, recordName, scope, ct);

            var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in copyColumns)
            {
                if (name.Equals(recordName, StringComparison.OrdinalIgnoreCase))
                    values[name] = newRecord;
                else if (TryGetValue(source, name, out var value))
                    values[name] = value ?? DBNull.Value;
            }

            if (values.Count == 0)
                throw new InvalidOperationException("Не найдено ни одного значения для дублирования шага.");

            var db = new DatabaseService(_connectionString);
            await db.InsertRowAsync(StepTable, values, ct);
            return newRecord;
        }

        public async Task<int> DeleteProgramAsync(ProgramContext context, CancellationToken ct = default)
        {
            if (context?.PlcValue == null || context.ClassNumberValue == null || context.RecordValue == null)
                throw new InvalidOperationException("Не удалось определить программу для удаления.");

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);
            using var command = new SqlCommand(@"
DELETE FROM [dbo].[Prog]
WHERE [PLC] = @PLC AND [PLC_Class_Number] = @Class AND [Record] = @Record;", connection)
            {
                CommandTimeout = 120
            };
            command.Parameters.Add("@PLC", SqlDbType.Int).Value = ToDbValue(context.PlcValue);
            command.Parameters.Add("@Class", SqlDbType.Int).Value = ToDbValue(context.ClassNumberValue);
            command.Parameters.Add("@Record", SqlDbType.Int).Value = ToDbValue(context.RecordValue);
            return await command.ExecuteNonQueryAsync(ct);
        }

        public async Task<int> DeleteStepAsync(Dictionary<string, object> source, CancellationToken ct = default)
        {
            if (source == null || source.Count == 0)
                return 0;

            var columns = await GetColumnsAsync(StepTable, ct);
            var scope = BuildScopeFromSource(columns, source, "PLC", "PLC_Class_Number", "Prog_Record", "Seq_Record", "Record");
            if (scope.Count == 0)
                throw new InvalidOperationException("Не удалось определить идентификатор шага.");

            var predicates = new List<string>();
            var parameters = new List<(string Name, object Value)>();
            int i = 0;
            foreach (var pair in scope)
            {
                string actualName = GetActualColumn(columns, pair.Key);
                if (string.IsNullOrWhiteSpace(actualName)) continue;
                if (pair.Value == null || pair.Value == DBNull.Value)
                    predicates.Add($"{QuoteIdentifier(actualName)} IS NULL");
                else
                {
                    string parameter = "@d" + i++;
                    predicates.Add($"{QuoteIdentifier(actualName)} = {parameter}");
                    parameters.Add((parameter, pair.Value));
                }
            }

            string sql = $"DELETE FROM {QuoteTable(StepTable)} WHERE {string.Join(" AND ", predicates)}";
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);
            using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value);
            return await command.ExecuteNonQueryAsync(ct);
        }

}

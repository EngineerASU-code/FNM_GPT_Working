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
        public async Task<HashSet<string>> GetExistingTablesAsync(CancellationToken ct = default)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);
            const string sql = @"
                SELECT TABLE_SCHEMA, TABLE_NAME
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_TYPE = 'BASE TABLE';";
            using var command = new SqlCommand(sql, connection) { CommandTimeout = 30 };
            using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                result.Add($"{reader.GetString(0)}.{reader.GetString(1)}");
            foreach (var table in result)
                _tableExistsCache[table] = true;
            return result;
        }

        public async Task<List<ProgramTemplateItem>> GetProgramsAsync(int take = 500, CancellationToken ct = default)
            => await GetCatalogAsync(ProgramTable, take, "PLC", "PLC_Class_Number", "Record", ct, MapProgram);

        public async Task<List<ProgramTemplateItem>> GetStepsAsync(int take = 1000, CancellationToken ct = default)
            => await GetCatalogAsync(StepTable, take, "PLC", "PLC_Class_Number", "Prog_Record", "Seq_Record", "Record", ct, MapStep);

        private async Task<List<ProgramTemplateItem>> GetCatalogAsync(
            string tableName, int take, string order1, string order2, string order3, CancellationToken ct,
            Func<DataRow, ProgramTemplateItem> mapper)
        {
            return await GetCatalogAsync(tableName, take, new[] { order1, order2, order3 }, ct, mapper);
        }

        private async Task<List<ProgramTemplateItem>> GetCatalogAsync(
            string tableName, int take, string order1, string order2, string order3, string order4, string order5,
            CancellationToken ct, Func<DataRow, ProgramTemplateItem> mapper)
        {
            return await GetCatalogAsync(tableName, take, new[] { order1, order2, order3, order4, order5 }, ct, mapper);
        }

        private async Task<List<ProgramTemplateItem>> GetCatalogAsync(
            string tableName, int take, IReadOnlyList<string> orderColumns, CancellationToken ct,
            Func<DataRow, ProgramTemplateItem> mapper)
        {
            var columns = await GetColumnsAsync(tableName, ct);
            var predicates = new List<string>();
            if (HasColumn(columns, "Record"))
                predicates.Add($"{QuoteIdentifier(GetActualColumn(columns, "Record"))} <> 0");

            var table = await QueryDataTableAsync(
                tableName,
                predicates,
                Array.Empty<(string Name, object Value)>(),
                BuildOrder(columns, orderColumns.ToArray()),
                Math.Clamp(take, 1, 5000),
                ct);

            return table.Rows.Cast<DataRow>().Select(mapper).ToList();
        }

        /// <summary>
        /// Загружает фактические строки выбранной таблицы в пределах контекста программы.
        /// Для Prog_StepParam автоматически разделяет параметры программы и шага.
        /// </summary>
}

using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Configurator
{
    public partial class TableViewModel
    {
        private readonly DatabaseService _db;

        public string CurrentTable { get; private set; } = "";
        public List<ColumnInfo> Columns { get; private set; }
        public List<string> KeyColumns { get; private set; } = new();
        public string KeyColumn => KeyColumns.Count > 0 ? KeyColumns[0] : "";
        public bool UsesValueIdentity { get; private set; }
        public bool HasValidKey => KeyColumns.Count > 0 || UsesValueIdentity;

        public int CurrentPage { get; set; } = 1;
        public int PageSize { get; } = 100;
        public int TotalCount { get; set; }
        public TableFilter Filter { get; private set; }
        public DataTable CurrentPageData { get; private set; }

        public Dictionary<string, string> CurrentRowValues { get; private set; } = new();
        public Dictionary<string, object> CurrentKeyValues { get; private set; } = new();
        public Dictionary<string, object> CurrentIdentityValues { get; private set; } = new();
        public Dictionary<string, object> CurrentRawRowValues { get; private set; } = new();
        private bool _metadataLoaded;
        private readonly Dictionary<string, (List<ColumnInfo> Columns, List<string> Keys)> _metadataCache =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _countCache =
            new(StringComparer.Ordinal);

        public object CurrentKeyValue => KeyColumns.Count > 0 && CurrentKeyValues.ContainsKey(KeyColumns[0])
            ? CurrentKeyValues[KeyColumns[0]]
            : null;

        public int TotalPages => TotalCount == 0 ? 1 : (TotalCount + PageSize - 1) / PageSize;

        public TableViewModel(string connectionString)
        {
            _db = new DatabaseService(connectionString);
        }

        // === P1-11: Async ===
        public async Task<DataTable> GetTableListAsync(CancellationToken ct = default)
        {
            return await _db.GetTableListAsync(ct);
        }

        // === P1-11: Async ===
        public async Task SelectTableAsync(string tableName, CancellationToken ct = default)
        {
            if (string.Equals(CurrentTable, tableName, StringComparison.OrdinalIgnoreCase) && _metadataLoaded)
            {
                CurrentPage = 1;
                return;
            }

            CurrentTable = tableName;
            CurrentPage = 1;

            if (_metadataCache.TryGetValue(tableName, out var cached))
            {
                Columns = cached.Columns;
                KeyColumns = cached.Keys;
                UsesValueIdentity = KeyColumns.Count == 0;
                _metadataLoaded = true;
                return;
            }

            // Метаданные двух независимых запросов получаем параллельно только при первом открытии таблицы.
            var columnsTask = _db.GetTableColumnsAsync(tableName, ct);
            var keysTask = _db.GetPrimaryKeyColumnsAsync(tableName, ct);
            await Task.WhenAll(columnsTask, keysTask);
            Columns = columnsTask.Result;
            KeyColumns = keysTask.Result;
            UsesValueIdentity = KeyColumns.Count == 0;
            _metadataCache[tableName] = (Columns, KeyColumns);
            _metadataLoaded = true;
        }

        // === P1-11: Async ===
        public Task<DataTable> LoadPageAsync(CancellationToken ct = default)
            => LoadPageWithFilterAsync(Filter, ct);

        public Task<DataTable> LoadPageIgnoringFilterAsync(CancellationToken ct = default)
            => LoadPageWithFilterAsync(null, ct);

        private async Task<DataTable> LoadPageWithFilterAsync(TableFilter filter, CancellationToken ct)
        {
            var orderColumns = KeyColumns.Count > 0
                ? KeyColumns
                : (Columns?.Count > 0 ? new List<string> { Columns[0].Name } : new List<string>());

            var data = await _db.GetTableDataPageAsync(CurrentTable, CurrentPage, PageSize, orderColumns, filter, ct);
            CurrentPageData = data;
            return data;
        }

        /// <summary>
        /// После CRUD перезагружается только текущая страница, без COUNT(*).
        /// Это сохраняет скорость интерфейса и позволяет восстановить курсор.
        /// </summary>
        public async Task<DataTable> ReloadCurrentPageAsync(CancellationToken ct = default)
        {
            return await LoadPageAsync(ct);
        }

        public void NotifyInserted(int count = 1)
        {
            if (count > 0) TotalCount += count;
        }

        public async Task RefreshTotalCountAsync(CancellationToken ct = default)
        {
            InvalidateCountCache();
            TotalCount = await GetTotalCountAsync(ct);
        }

        public void NotifyDeleted(int count)
        {
            if (count <= 0) return;
            TotalCount = Math.Max(0, TotalCount - count);
        }

        public Task<int> GetTotalCountAsync(CancellationToken ct = default)
            => GetTotalCountWithFilterAsync(Filter, ct);

        public Task<int> GetTotalCountIgnoringFilterAsync(CancellationToken ct = default)
            => GetTotalCountWithFilterAsync(null, ct);

        private async Task<int> GetTotalCountWithFilterAsync(TableFilter filter, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(CurrentTable)) return 0;

            string cacheKey = filter == null || !filter.IsActive
                ? CurrentTable + "|ALL"
                : CurrentTable + "|" + filter.Column + "|" + filter.Operator + "|" + filter.Value;
            if (_countCache.TryGetValue(cacheKey, out int cached))
            {
                TotalCount = cached;
                return cached;
            }

            int count = await _db.GetTableRowCountAsync(CurrentTable, filter, ct);
            _countCache[cacheKey] = count;
            TotalCount = count;
            return count;
        }

        private string BuildCountCacheKey()
        {
            if (Filter == null || !Filter.IsActive)
                return CurrentTable + "|ALL";
            return CurrentTable + "|" + Filter.Column + "|" + Filter.Operator + "|" + Filter.Value;
        }

        private void InvalidateCountCache() => _countCache.Clear();

        public async Task<PagedResult> LoadPagedDataAsync(CancellationToken ct = default)
        {
            var dataTask = LoadPageAsync(ct);
            var countTask = GetTotalCountAsync(ct);
            await Task.WhenAll(dataTask, countTask);
            return new PagedResult { Data = dataTask.Result, TotalCount = countTask.Result };
        }

        public void SetCurrentRow(DataRowView row)
        {
            CurrentRowValues.Clear();
            CurrentKeyValues.Clear();
            CurrentIdentityValues.Clear();
            CurrentRawRowValues.Clear();

            if (Columns == null) return;

            foreach (var col in Columns)
            {
                if (row.DataView.Table.Columns.Contains(col.Name))
                {
                    var rawValue = row[col.Name];
                    CurrentRawRowValues[col.Name] = rawValue;
                    CurrentRowValues[col.Name] = rawValue == DBNull.Value ? "" : rawValue?.ToString() ?? "";
                }
                else
                {
                    CurrentRowValues[col.Name] = "";
                    CurrentRawRowValues[col.Name] = DBNull.Value;
                }
            }

            foreach (var keyCol in KeyColumns)
            {
                if (row.DataView.Table.Columns.Contains(keyCol))
                    CurrentKeyValues[keyCol] = row[keyCol];
            }

            if (UsesValueIdentity)
            {
                foreach (var col in Columns.Where(c => !c.IsComputed && c.DataType != "timestamp" && c.DataType != "rowversion"))
                {
                    if (row.DataView.Table.Columns.Contains(col.Name))
                        CurrentIdentityValues[col.Name] = row[col.Name];
                }
            }
        }


        public void SetFilter(TableFilter filter)
        {
            Filter = filter?.IsActive == true ? filter : null;
            CurrentPage = 1;
        }

        public void ClearFilter()
        {
            Filter = null;
            CurrentPage = 1;
        }

        public void ClearCurrentRow()
        {
            CurrentRowValues.Clear();
            CurrentKeyValues.Clear();
            CurrentIdentityValues.Clear();
            CurrentRawRowValues.Clear();
        }

        // === P1-11: Async ===
        public async Task<int> GetNextRecordValueAsync(CancellationToken ct = default)
        {
            if (KeyColumns.Count != 1)
                throw new System.Exception("Автогенерация ключа поддерживается только для одиночного целочисленного ключа.");

            return await _db.GetMaxRecordValueAsync(CurrentTable, KeyColumns[0], ct) + 1;
        }

        public string RecordColumn => Columns?.FirstOrDefault(c => c.Name.Equals("Record", StringComparison.OrdinalIgnoreCase))?.Name ?? KeyColumn;

        public string PlcColumn => Columns?.FirstOrDefault(c => c.Name.Equals("PLC", StringComparison.OrdinalIgnoreCase))?.Name ?? "";

        public bool HasScopedRecord => !string.IsNullOrWhiteSpace(RecordColumn) && !string.IsNullOrWhiteSpace(PlcColumn);

        public bool CanAutoGenerateKey
        {
            get
            {
                if (KeyColumns.Count != 1) return false;
                var keyCol = Columns?.FirstOrDefault(c => c.Name == KeyColumns[0]);
                return keyCol != null && !keyCol.IsIdentity && keyCol.DataType == "int";
            }
        }

        // === P1-11: Async ===
        public async Task InsertAsync(Dictionary<string, object> values, CancellationToken ct = default)
        {
            await _db.InsertRowAsync(CurrentTable, values, ct);
            InvalidateCountCache();
        }

        public async Task<Dictionary<string, object>> DuplicateCurrentRowAsync(CancellationToken ct = default)
        {
            // Если между расчётом Record и INSERT другой клиент успел занять
            // это значение, пересчитываем его несколько раз. Это особенно важно
            // для составного PLC + Record ключа.
            const int maxAttempts = 3;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var values = await BuildDuplicateValuesAsync(ct);
                try
                {
                    await InsertAsync(values, ct);
                    return values;
                }
                catch (SqlException ex) when ((ex.Number == 2601 || ex.Number == 2627) && attempt < maxAttempts)
                {
                    // Duplicate key. Перечитываем MAX(Record) и пробуем ещё раз.
                }
            }

            throw new InvalidOperationException("Не удалось создать копию: значение ключа занято другой записью.");
        }

        // === P1-11: Async ===
        public async Task UpdateAsync(Dictionary<string, object> values, CancellationToken ct = default)
        {
            if (!HasValidKey)
                throw new System.Exception("Не удалось определить идентификатор строки.");

            if (UsesValueIdentity)
            {
                var editableValues = values.ToDictionary(v => v.Key, v => v.Value);
                await _db.UpdateRowByValuesAsync(CurrentTable, CurrentIdentityValues, editableValues, ct);
                InvalidateCountCache();
                return;
            }

            var nonKeyValues = values
                .Where(v => !KeyColumns.Contains(v.Key))
                .ToDictionary(v => v.Key, v => v.Value);

            await _db.UpdateRowAsync(CurrentTable, KeyColumns, CurrentKeyValues, nonKeyValues, ct);
            InvalidateCountCache();
        }

        public async Task<int> DeleteAsync(List<Dictionary<string, object>> keyValuesList, CancellationToken ct = default)
        {
            if (!HasValidKey)
                throw new System.Exception("Не удалось определить идентификатор строки.");

            int deleted;
            if (UsesValueIdentity)
                deleted = await _db.DeleteRowsByValuesAsync(CurrentTable, keyValuesList, ct);
            else
                deleted = await _db.DeleteRowsAsync(CurrentTable, KeyColumns, keyValuesList, ct);

            if (deleted > 0) InvalidateCountCache();
            return deleted;
        }


    }
}

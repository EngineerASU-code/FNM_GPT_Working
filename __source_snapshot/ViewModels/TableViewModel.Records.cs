using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Configurator;

public partial class TableViewModel
{
            public Dictionary<string, object> BuildIdentityValues(Dictionary<string, object> values)
            {
                var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                if (values == null) return result;

                string[] logical = { "PLC", "Record", "Area" };
                foreach (var name in logical)
                    if (values.Keys.Any(k => k.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    {
                        var key = values.Keys.First(k => k.Equals(name, StringComparison.OrdinalIgnoreCase));
                        result[key] = values[key];
                    }
                if (result.Count >= Math.Min(2, logical.Count(x => Columns?.Any(c => c.Name.Equals(x, StringComparison.OrdinalIgnoreCase)) == true)))
                    return result;

                if (KeyColumns.Count > 0)
                {
                    result.Clear();
                    foreach (var key in KeyColumns)
                        if (values.Keys.Any(k => k.Equals(key, StringComparison.OrdinalIgnoreCase)))
                        {
                            var actual = values.Keys.First(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
                            result[actual] = values[actual];
                        }
                    if (result.Count > 0) return result;
                }

                result.Clear();
                foreach (var col in Columns ?? new List<ColumnInfo>())
                    if (!col.IsReadOnly && values.Keys.Any(k => k.Equals(col.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        var actual = values.Keys.First(k => k.Equals(col.Name, StringComparison.OrdinalIgnoreCase));
                        result[actual] = values[actual];
                    }
                return result;
            }

            public Dictionary<string, object> BuildIdentityValues(System.Data.DataRowView row)
            {
                var result = new Dictionary<string, object>();
                var source = row.DataView.Table;

                if (UsesValueIdentity)
                {
                    foreach (var col in Columns.Where(c => !c.IsComputed && c.DataType != "timestamp" && c.DataType != "rowversion"))
                        if (source.Columns.Contains(col.Name)) result[col.Name] = row[col.Name];
                }
                else
                {
                    foreach (var key in KeyColumns)
                        if (source.Columns.Contains(key)) result[key] = row[key];
                }
                return result;
            }

            public async Task<Dictionary<string, object>> BuildDuplicateValuesAsync(CancellationToken ct = default)
            {
                if (Columns == null || Columns.Count == 0)
                    throw new InvalidOperationException("Не удалось определить столбцы таблицы.");

                var values = new Dictionary<string, object>();

                string plcColumn = Columns.FirstOrDefault(c => c.Name.Equals("PLC", StringComparison.OrdinalIgnoreCase))?.Name;
                string recordColumn = Columns.FirstOrDefault(c => c.Name.Equals("Record", StringComparison.OrdinalIgnoreCase))?.Name;

                // Для PLC используем исходное значение DataRow, а не текст из поля справа.
                // Это принципиально важно для PLC = NULL: пустая строка и SQL NULL — разные значения.
                object plcValue = null;
                bool hasPlcValue = false;
                if (!string.IsNullOrEmpty(plcColumn) && CurrentRawRowValues.TryGetValue(plcColumn, out var rawPlc))
                {
                    plcValue = rawPlc ?? DBNull.Value;
                    hasPlcValue = true;
                }

                int? nextRecord = null;
                if (!string.IsNullOrEmpty(recordColumn))
                {
                    if (!string.IsNullOrEmpty(plcColumn) && hasPlcValue)
                    {
                        // PLC + Record — составная логика.
                        // PLC 1 / Record 1 и PLC 2 / Record 1 не конфликтуют.
                        nextRecord = await _db.GetNextScopedRecordValueAsync(
                            CurrentTable, recordColumn, plcColumn, plcValue, ct);
                    }
                    else
                    {
                        // Если PLC нет, Record всё равно должен получить новое значение,
                        // если это не identity/computed поле.
                        var recordInfo = Columns.First(c => c.Name.Equals(recordColumn, StringComparison.OrdinalIgnoreCase));
                        if (!recordInfo.IsIdentity && !recordInfo.IsReadOnly)
                            nextRecord = await _db.GetMaxRecordValueAsync(CurrentTable, recordColumn, ct) + 1;
                    }
                }

                foreach (var col in Columns)
                {
                    if (col.IsReadOnly || col.IsIdentity)
                        continue;

                    // Record всегда генерируем заново. Старый Record копировать нельзя:
                    // это именно тот случай, из-за которого возникал NULL/duplicate key.
                    if (!string.IsNullOrEmpty(recordColumn) &&
                        col.Name.Equals(recordColumn, StringComparison.OrdinalIgnoreCase) &&
                        nextRecord.HasValue)
                    {
                        values[col.Name] = nextRecord.Value;
                        continue;
                    }

                    // Ключевые поля: для составного PLC + Record ключа PLC нужно
                    // скопировать из исходной строки, а Record уже сгенерирован выше.
                    // Нельзя просто пропустить все KeyColumns — иначе INSERT получает
                    // NULL в обязательном PLC/Record поле.
                    if (KeyColumns.Contains(col.Name))
                    {
                        if (col.Name.Equals(recordColumn, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (KeyColumns.Count == 1 && CanAutoGenerateKey && col.Name.Equals(KeyColumn, StringComparison.OrdinalIgnoreCase))
                        {
                            values[col.Name] = await GetNextRecordValueAsync(ct);
                            continue;
                        }

                        if (CurrentRawRowValues.TryGetValue(col.Name, out var keyRaw))
                        {
                            if (keyRaw == null || keyRaw == DBNull.Value)
                            {
                                if (!col.IsNullable && !col.HasDefault)
                                    throw new InvalidOperationException(
                                        $"Невозможно дублировать строку: ключевое поле {col.Name} содержит NULL.");

                                if (!col.HasDefault)
                                    values[col.Name] = DBNull.Value;
                            }
                            else
                            {
                                values[col.Name] = keyRaw;
                            }
                        }
                        continue;
                    }

                    if (CurrentRawRowValues.TryGetValue(col.Name, out var rawValue))
                    {
                        // При дублировании сохраняем NULL как NULL, а не превращаем его
                        // в пустую строку. Это особенно важно для nullable колонок.
                        if (rawValue == null || rawValue == DBNull.Value)
                        {
                            if (col.HasDefault)
                                continue;

                            values[col.Name] = DBNull.Value;
                        }
                        else
                        {
                            values[col.Name] = rawValue;
                        }
                        continue;
                    }

                    // Резервный путь для старых строк/таблиц.
                    if (CurrentRowValues.TryGetValue(col.Name, out var text) && !string.IsNullOrWhiteSpace(text))
                        values[col.Name] = ConvertTextToDbValue(text, col);
                    else if (col.HasDefault)
                        continue;
                    else if (col.IsNullable)
                        values[col.Name] = DBNull.Value;
                }

                // Если Record обязателен, но мы почему-то не смогли его определить,
                // лучше остановить дублирование с понятной ошибкой, чем отправить NULL в INSERT.
                if (!string.IsNullOrEmpty(recordColumn))
                {
                    var recordInfo = Columns.First(c => c.Name.Equals(recordColumn, StringComparison.OrdinalIgnoreCase));
                    if (!recordInfo.IsIdentity && !recordInfo.IsReadOnly && !recordInfo.IsNullable &&
                        !values.ContainsKey(recordColumn))
                    {
                        throw new InvalidOperationException(
                            $"Не удалось автоматически определить новое значение поля {recordColumn}. Дублирование отменено.");
                    }
                }

                return values;
            }

            private static object ConvertTextToDbValue(string text, ColumnInfo col)
            {
                var dt = col.DataType.ToLowerInvariant();
                if (dt == "int") return int.Parse(text);
                if (dt == "bigint") return long.Parse(text);
                if (dt == "smallint") return short.Parse(text);
                if (dt == "tinyint") return byte.Parse(text);
                if (dt == "decimal" || dt == "numeric" || dt == "money" || dt == "smallmoney") return decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
                if (dt == "float") return double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
                if (dt == "real") return float.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
                if (dt == "date" || dt == "datetime" || dt == "datetime2" || dt == "smalldatetime") return DateTime.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
                if (dt == "datetimeoffset") return DateTimeOffset.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
                if (dt == "time") return TimeSpan.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
                if (dt == "bit") return text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase) || text.Equals("да", StringComparison.OrdinalIgnoreCase);
                if (dt == "uniqueidentifier") return Guid.Parse(text);
                return text;
            }
}

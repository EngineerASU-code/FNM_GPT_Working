using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Configurator
{
    public partial class DatabaseService
    {
        public async Task<List<FreeRecordGroup>> FindFreeRecordsByPlcAsync(string tableName, string plcFilter = null, CancellationToken ct = default)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            var (schema, table) = ParseTableName(tableName);
            var columns = await GetTableColumnsAsync(tableName, ct);
            var plcColumn = columns.Select(x => x.Name).FirstOrDefault(n => n.Equals("PLC", StringComparison.OrdinalIgnoreCase));
            var recordColumn = columns.Select(x => x.Name).FirstOrDefault(n => n.Equals("Record", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(plcColumn) || string.IsNullOrWhiteSpace(recordColumn))
                throw new InvalidOperationException($"В {tableName} отсутствует столбец PLC или Record.");

            var query = $@"SELECT {QuoteIdentifier(plcColumn)} AS PlcValue,
                                  {QuoteIdentifier(recordColumn)} AS RecordValue
                           FROM {QuoteIdentifier(schema)}.{QuoteIdentifier(table)}
                           WHERE {QuoteIdentifier(plcColumn)} IS NOT NULL
                             AND {QuoteIdentifier(recordColumn)} IS NOT NULL";
            using var command = new SqlCommand(query, connection) { CommandTimeout = 120 };
            if (!string.IsNullOrWhiteSpace(plcFilter))
            {
                query += $" AND CONVERT(nvarchar(128), {QuoteIdentifier(plcColumn)}) = @Plc";
                command.CommandText = query;
                command.Parameters.AddWithValue("@Plc", plcFilter);
            }
            using var reader = await command.ExecuteReaderAsync(ct);

            var occupiedByPlc = new Dictionary<string, SortedSet<int>>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync(ct))
            {
                if (reader.IsDBNull(0) || reader.IsDBNull(1)) continue;
                var plc = Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(plc)) continue;
                if (!TryReadIntegralValue(reader.GetValue(1), out int record)) continue;
                if (!occupiedByPlc.TryGetValue(plc, out var records))
                {
                    records = new SortedSet<int>();
                    occupiedByPlc[plc] = records;
                }
                records.Add(record);
            }

            var result = new List<FreeRecordGroup>();
            foreach (var pair in occupiedByPlc.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                var records = pair.Value;
                if (records.Count == 0) continue;
                int min = records.Min;
                int max = records.Max;
                var free = new List<int>();
                int previous = min;
                foreach (var current in records)
                {
                    if (current > previous + 1)
                        for (int value = previous + 1; value < current; value++) free.Add(value);
                    previous = current;
                }
                if (free.Count > 0)
                    result.Add(new FreeRecordGroup { Plc = pair.Key, MinRecord = min, MaxRecord = max, FreeRecords = free });
            }
            return result;
        }

        private static bool TryReadIntegralValue(object raw, out int value)
        {
            value = 0;
            if (raw == null || raw == DBNull.Value) return false;
            try
            {
                switch (raw)
                {
                    case byte v: value = v; return true;
                    case sbyte v: value = v; return true;
                    case short v: value = v; return true;
                    case ushort v: value = v; return true;
                    case int v: value = v; return true;
                    case uint v: value = v <= int.MaxValue ? (int)v : 0; return v <= int.MaxValue;
                    case long v: value = (int)v; return v >= int.MinValue && v <= int.MaxValue;
                    case ulong v: value = (int)v; return v <= int.MaxValue;
                    case decimal v: value = (int)v; return decimal.Truncate(v) == v && v >= int.MinValue && v <= int.MaxValue;
                    case double v: value = (int)v; return !double.IsNaN(v) && !double.IsInfinity(v) && Math.Truncate(v) == v && v >= int.MinValue && v <= int.MaxValue;
                    case float v: value = (int)v; return !float.IsNaN(v) && !float.IsInfinity(v) && MathF.Truncate(v) == v && v >= int.MinValue && v <= int.MaxValue;
                    default: return int.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
                }
            }
            catch { value = 0; return false; }
        }


        public async Task<List<DuplicateRecordGroup>> FindDuplicateRecordsByPlcAsync(string tableName, string plcFilter = null, CancellationToken ct = default)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);
            var (schema, table) = ParseTableName(tableName);
            var columns = await GetTableColumnsAsync(tableName, ct);
            var plcColumn = columns.Select(x => x.Name).FirstOrDefault(n => n.Equals("PLC", StringComparison.OrdinalIgnoreCase));
            var recordColumn = columns.Select(x => x.Name).FirstOrDefault(n => n.Equals("Record", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(plcColumn) || string.IsNullOrWhiteSpace(recordColumn))
                throw new InvalidOperationException($"В {tableName} отсутствует столбец PLC или Record.");
            var query = $@"SELECT {QuoteIdentifier(plcColumn)}, {QuoteIdentifier(recordColumn)}
                           FROM {QuoteIdentifier(schema)}.{QuoteIdentifier(table)}
                           WHERE {QuoteIdentifier(plcColumn)} IS NOT NULL AND {QuoteIdentifier(recordColumn)} IS NOT NULL";
            using var command = new SqlCommand(query, connection) { CommandTimeout = 120 };
            if (!string.IsNullOrWhiteSpace(plcFilter))
            {
                command.CommandText += $" AND CONVERT(nvarchar(128), {QuoteIdentifier(plcColumn)}) = @Plc";
                command.Parameters.AddWithValue("@Plc", plcFilter);
            }
            using var reader = await command.ExecuteReaderAsync(ct);
            var counts = new Dictionary<string, Dictionary<int,int>>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync(ct))
            {
                if (reader.IsDBNull(0) || reader.IsDBNull(1)) continue;
                var plc = Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)?.Trim() ?? "";
                if (!TryReadIntegralValue(reader.GetValue(1), out int record) || string.IsNullOrWhiteSpace(plc)) continue;
                if (!counts.TryGetValue(plc, out var map)) counts[plc] = map = new Dictionary<int,int>();
                map[record] = map.TryGetValue(record, out var count) ? count + 1 : 1;
            }
            return counts.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .SelectMany(x => x.Value.Where(v => v.Value > 1).OrderBy(v => v.Key)
                    .Select(v => new DuplicateRecordGroup { Plc = x.Key, Record = v.Key, Occurrences = v.Value }))
                .ToList();
        }

        public async Task<List<DuplicateAddressGroup>> FindDuplicateAddressesAsync(string tableName, string addressKind, string plcFilter = null, CancellationToken ct = default)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);
            var (schema, table) = ParseTableName(tableName);
            var columns = await GetTableColumnsAsync(tableName, ct);
            var preferred = addressKind.Equals("Input", StringComparison.OrdinalIgnoreCase) ? "Address_Input" : "Address_Output";
            var compact = addressKind.Equals("Input", StringComparison.OrdinalIgnoreCase) ? "AddressInput" : "AddressOutput";
            var actualColumn = columns.Select(x => x.Name).FirstOrDefault(n => n.Equals(preferred, StringComparison.OrdinalIgnoreCase))
                ?? columns.Select(x => x.Name).FirstOrDefault(n => n.Equals(compact, StringComparison.OrdinalIgnoreCase));
            var isWord = columns.Any(x => x.Name.Equals("Address", StringComparison.OrdinalIgnoreCase));
            if (isWord) actualColumn = columns.First(x => x.Name.Equals("Address", StringComparison.OrdinalIgnoreCase)).Name;
            if (string.IsNullOrWhiteSpace(actualColumn)) throw new InvalidOperationException($"В {tableName} не найден столбец адреса.");
            var plcColumn = columns.Select(x => x.Name).FirstOrDefault(n => n.Equals("PLC", StringComparison.OrdinalIgnoreCase));
            var query = $"SELECT {QuoteIdentifier(actualColumn)}" + (string.IsNullOrWhiteSpace(plcColumn) ? "" : $", {QuoteIdentifier(plcColumn)}") + $" FROM {QuoteIdentifier(schema)}.{QuoteIdentifier(table)} WHERE {QuoteIdentifier(actualColumn)} IS NOT NULL";
            using var command = new SqlCommand(query, connection) { CommandTimeout = 120 };
            if (!string.IsNullOrWhiteSpace(plcFilter) && !string.IsNullOrWhiteSpace(plcColumn))
            {
                command.CommandText += $" AND CONVERT(nvarchar(128), {QuoteIdentifier(plcColumn)}) = @Plc";
                command.Parameters.AddWithValue("@Plc", plcFilter);
            }
            using var reader = await command.ExecuteReaderAsync(ct);
            var counts = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync(ct))
            {
                if (reader.IsDBNull(0)) continue;
                var raw = Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture);
                var plc = !string.IsNullOrWhiteSpace(plcColumn) && !reader.IsDBNull(1) ? Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture)?.Trim() ?? "" : "";
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var parsed = isWord ? ParseWordAddresses(raw, addressKind.Equals("Input", StringComparison.OrdinalIgnoreCase) ? "IW" : "QW").Select(a => $"{a.Prefix}{a.Address}")
                                    : ParseControllerAddresses(raw).Select(a => a.Bit.HasValue ? $"{a.Prefix}{a.ByteAddress}.{a.Bit.Value}" : $"{a.Prefix}{a.ByteAddress}");
                foreach (var address in parsed)
                {
                    var key = plc + "\u001f" + address;
                    counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;
                }
            }
            return counts.Where(x => x.Value > 1).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x =>
            {
                var parts = x.Key.Split('\u001f');
                return new DuplicateAddressGroup { Plc = parts[0], Address = parts.Length > 1 ? parts[1] : x.Key, Occurrences = x.Value };
            }).ToList();
        }

        public async Task<List<FreeAddressByteGroup>> FindFreeAddressBitsAsync(string tableName, string addressKind, string plcFilter = null, CancellationToken ct = default)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            var (schema, table) = ParseTableName(tableName);
            var columns = await GetTableColumnsAsync(tableName, ct);
            string preferred = addressKind.Equals("Input", StringComparison.OrdinalIgnoreCase) ? "Address_Input" : "Address_Output";
            string compact = addressKind.Equals("Input", StringComparison.OrdinalIgnoreCase) ? "AddressInput" : "AddressOutput";
            var actualColumn = columns.Select(x => x.Name).FirstOrDefault(n => n.Equals(preferred, StringComparison.OrdinalIgnoreCase))
                ?? columns.Select(x => x.Name).FirstOrDefault(n => n.Equals(compact, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(actualColumn))
                throw new InvalidOperationException($"В {tableName} не найден столбец {preferred}.");

            var plcColumn = columns.Select(x => x.Name).FirstOrDefault(n => n.Equals("PLC", StringComparison.OrdinalIgnoreCase));
            var query = $"SELECT {QuoteIdentifier(actualColumn)} FROM {QuoteIdentifier(schema)}.{QuoteIdentifier(table)} WHERE {QuoteIdentifier(actualColumn)} IS NOT NULL";
            using var command = new SqlCommand(query, connection) { CommandTimeout = 120 };
            if (!string.IsNullOrWhiteSpace(plcFilter) && !string.IsNullOrWhiteSpace(plcColumn))
            {
                query += $" AND CONVERT(nvarchar(128), {QuoteIdentifier(plcColumn)}) = @Plc";
                command.CommandText = query;
                command.Parameters.AddWithValue("@Plc", plcFilter);
            }
            using var reader = await command.ExecuteReaderAsync(ct);

            var occupied = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync(ct))
            {
                if (reader.IsDBNull(0)) continue;
                var raw = Convert.ToString(reader.GetValue(0));
                if (string.IsNullOrWhiteSpace(raw)) continue;
                foreach (var address in ParseControllerAddresses(raw))
                {
                    var key = address.Prefix + address.ByteAddress.ToString(CultureInfo.InvariantCulture);
                    if (!occupied.TryGetValue(key, out var bits))
                    {
                        bits = new HashSet<int>();
                        occupied[key] = bits;
                    }
                    if (address.Bit.HasValue) bits.Add(address.Bit.Value);
                    else for (int bit = 0; bit < 8; bit++) bits.Add(bit);
                }
            }

            return occupied.Select(pair =>
            {
                int firstDigit = 0;
                while (firstDigit < pair.Key.Length && !char.IsDigit(pair.Key[firstDigit])) firstDigit++;
                string prefix = pair.Key[..firstDigit];
                int byteAddress = int.Parse(pair.Key[firstDigit..], CultureInfo.InvariantCulture);
                var bits = Enumerable.Range(0, 8).Select(bit => new FreeAddressBit
                {
                    Bit = bit, IsFree = !pair.Value.Contains(bit)
                }).ToList();
                return new FreeAddressByteGroup
                {
                    Prefix = prefix, ByteAddress = byteAddress, Bits = bits,
                    FreeBits = bits.Where(x => x.IsFree).Select(x => x.Bit).ToList()
                };
            }).OrderBy(x => x.Prefix, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.ByteAddress).ToList();
        }

        public async Task<List<FreeAddressWordGroup>> FindFreeAddressWordsAsync(string tableName, string plcFilter = null, string forcedPrefix = null, CancellationToken ct = default)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            var (schema, table) = ParseTableName(tableName);
            var columns = await GetTableColumnsAsync(tableName, ct);
            var actualColumn = columns.Select(x => x.Name).FirstOrDefault(n => n.Equals("Address", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(actualColumn))
                throw new InvalidOperationException($"В {tableName} не найден столбец Address.");

            var plcColumn = columns.Select(x => x.Name).FirstOrDefault(n => n.Equals("PLC", StringComparison.OrdinalIgnoreCase));
            var query = $"SELECT {QuoteIdentifier(actualColumn)} FROM {QuoteIdentifier(schema)}.{QuoteIdentifier(table)} WHERE {QuoteIdentifier(actualColumn)} IS NOT NULL";
            using var command = new SqlCommand(query, connection) { CommandTimeout = 120 };
            if (!string.IsNullOrWhiteSpace(plcFilter) && !string.IsNullOrWhiteSpace(plcColumn))
            {
                query += $" AND CONVERT(nvarchar(128), {QuoteIdentifier(plcColumn)}) = @Plc";
                command.CommandText = query;
                command.Parameters.AddWithValue("@Plc", plcFilter);
            }
            using var reader = await command.ExecuteReaderAsync(ct);

            var occupied = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync(ct))
            {
                if (reader.IsDBNull(0)) continue;
                var raw = Convert.ToString(reader.GetValue(0));
                if (string.IsNullOrWhiteSpace(raw)) continue;

                foreach (var address in ParseWordAddresses(raw, forcedPrefix))
                {
                    var key = address.Prefix + address.Address.ToString(CultureInfo.InvariantCulture);
                    if (!occupied.TryGetValue(key, out var bytes))
                    {
                        bytes = new HashSet<int>();
                        occupied[key] = bytes;
                    }
                    bytes.Add(address.Address);
                    bytes.Add(address.Address + 1);
                }
            }

            return occupied.Select(pair =>
            {
                int firstDigit = 0;
                while (firstDigit < pair.Key.Length && !char.IsDigit(pair.Key[firstDigit])) firstDigit++;
                string prefix = pair.Key[..firstDigit];
                int address = int.Parse(pair.Key[firstDigit..], CultureInfo.InvariantCulture);
                var bytes = Enumerable.Range(0, 2)
                    .Select(offset => new FreeAddressWordByte
                    {
                        Address = address + offset,
                        IsFree = !pair.Value.Contains(address + offset)
                    }).ToList();
                return new FreeAddressWordGroup
                {
                    Prefix = prefix, Address = address, Bytes = bytes,
                    OccupiedBytes = bytes.Count(x => !x.IsFree)
                };
            }).OrderBy(x => x.Prefix, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Address).ToList();
        }

        private static IEnumerable<ParsedWordAddress> ParseWordAddresses(string value, string forcedPrefix = null)
        {
            // Supports common PLC word notation (IW124/QW124), byte-style I124/Q124,
            // and plain numeric addresses. Every discovered address occupies two bytes.
            var matches = System.Text.RegularExpressions.Regex.Matches(
                value,
                @"(?:(?<prefix>[IQ])\s*(?:W\s*)?)?(?<address>\d+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                if (!int.TryParse(match.Groups["address"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int address)) continue;
                var prefix = !string.IsNullOrWhiteSpace(forcedPrefix) ? forcedPrefix.ToUpperInvariant() : (match.Groups["prefix"].Success ? match.Groups["prefix"].Value.ToUpperInvariant() : "");
                yield return new ParsedWordAddress(prefix, address);
            }
        }

        private sealed record ParsedWordAddress(string Prefix, int Address);

        private static IEnumerable<ParsedControllerAddress> ParseControllerAddresses(string value)
        {
            var matches = System.Text.RegularExpressions.Regex.Matches(value, @"(?<prefix>[IQ])\s*(?<byte>\d+)(?:\.(?<bit>[0-7]))?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                if (!int.TryParse(match.Groups["byte"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int byteAddress)) continue;
                int? bit = null;
                if (match.Groups["bit"].Success && int.TryParse(match.Groups["bit"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int bitValue)) bit = bitValue;
                yield return new ParsedControllerAddress(match.Groups["prefix"].Value.ToUpperInvariant(), byteAddress, bit);
            }
        }

        private sealed record ParsedControllerAddress(string Prefix, int ByteAddress, int? Bit);
    }

    public sealed class DuplicateRecordGroup
    {
        public string Plc { get; set; } = "";
        public int Record { get; set; }
        public int Occurrences { get; set; }
    }

    public sealed class DuplicateAddressGroup
    {
        public string Plc { get; set; } = "";
        public string Address { get; set; } = "";
        public int Occurrences { get; set; }
    }

    public sealed class FreeAddressBit
    {
        public int Bit { get; set; }
        public bool IsFree { get; set; }
    }

    public sealed class FreeAddressByteGroup
    {
        public string Prefix { get; set; } = "";
        public int ByteAddress { get; set; }
        public List<FreeAddressBit> Bits { get; set; } = new();
        public List<int> FreeBits { get; set; } = new();
    }

    public sealed class FreeAddressWordByte
    {
        public int Address { get; set; }
        public bool IsFree { get; set; }
    }

    public sealed class FreeAddressWordGroup
    {
        public string Prefix { get; set; } = "";
        public int Address { get; set; }
        public List<FreeAddressWordByte> Bytes { get; set; } = new();
        public int OccupiedBytes { get; set; }
    }

    public sealed class FreeRecordGroup
    {
        public string Plc { get; set; } = "";
        public int MinRecord { get; set; }
        public int MaxRecord { get; set; }
        public List<int> FreeRecords { get; set; } = new();
    }
}

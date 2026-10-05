using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Configurator;

public sealed class DisabledObjectArchiveService
{
    private readonly string _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Configurator", "DisabledObjects");

    public string GetArchivePath(string server, string database, string className)
    {
        string dir = Path.Combine(_root, Safe(server), Safe(database));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, Safe(className) + ".txt");
    }

    public bool Exists(string server, string database, string className) => File.Exists(GetArchivePath(server, database, className));

    public async Task ArchiveAsync(string server, string database, string className, string tableName, DataTable rows, CancellationToken ct = default)
    {
        var doc = new ArchiveDocument { Version = 1, TableName = tableName, Database = database, ClassName = className, Rows = rows.Rows.Cast<DataRow>().Select(ToRow).ToList() };
        await WriteAsync(GetArchivePath(server, database, className), doc, ct);
    }

    public async Task AppendAsync(string server, string database, string className, string tableName, DataRow row, CancellationToken ct = default)
    {
        string path = GetArchivePath(server, database, className);
        ArchiveDocument doc = await ReadAsync(path, ct) ?? new ArchiveDocument { Version = 1, TableName = tableName, Database = database, ClassName = className };
        doc.TableName = tableName;
        doc.Rows.Add(ToRow(row));
        await WriteAsync(path, doc, ct);
    }

    public async Task<List<Dictionary<string, string>>> GetArchivedRowsAsync(string server, string database, string className, CancellationToken ct = default)
    {
        string path = GetArchivePath(server, database, className);
        var doc = await ReadAsync(path, ct);
        return doc?.Rows.Select(x => new Dictionary<string, string>(x.Values, StringComparer.OrdinalIgnoreCase)).ToList()
               ?? new List<Dictionary<string, string>>();
    }

    public async Task<bool> RestoreOneAsync(string server, string database, string className, string tableName, DatabaseService db, Dictionary<string, string> archivedValues, CancellationToken ct = default)
    {
        if (archivedValues == null || archivedValues.Count == 0) return false;
        string path = GetArchivePath(server, database, className);
        var doc = await ReadAsync(path, ct);
        if (doc == null || doc.Rows.Count == 0) return false;
        var meta = await db.GetTableColumnsAsync(tableName, ct);
        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in archivedValues)
        {
            var info = meta.FirstOrDefault(m => m.Name.Equals(pair.Key, StringComparison.OrdinalIgnoreCase));
            if (info == null || info.IsReadOnly) continue;
            values[pair.Key] = Decode(pair.Value, info.DataType);
        }
        var identity = BuildLogicalIdentity(values);
        if (identity.Count > 0 && await db.RowExistsByValuesAsync(tableName, identity, ct)) return false;
        try { await db.InsertRowAsync(tableName, values, ct); }
        catch { return false; }

        int index = doc.Rows.FindIndex(r => SameArchivedRow(r.Values, archivedValues));
        if (index >= 0) doc.Rows.RemoveAt(index);
        if (doc.Rows.Count == 0) File.Delete(path);
        else await WriteAsync(path, doc, ct);
        return true;
    }

    private static bool SameArchivedRow(Dictionary<string, string> left, Dictionary<string, string> right)
    {
        if (left.Count != right.Count) return false;
        foreach (var pair in right)
            if (!left.TryGetValue(pair.Key, out var value) || !string.Equals(value, pair.Value, StringComparison.Ordinal)) return false;
        return true;
    }

    public async Task<int> RestoreAsync(string server, string database, string className, string tableName, DatabaseService db, CancellationToken ct = default)
    {
        string path = GetArchivePath(server, database, className);
        var doc = await ReadAsync(path, ct);
        if (doc == null || doc.Rows.Count == 0) return 0;
        var meta = await db.GetTableColumnsAsync(tableName, ct);
        int restored = 0;
        var remaining = new List<ArchivedRow>();
        foreach (var row in doc.Rows)
        {
            ct.ThrowIfCancellationRequested();
            var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in row.Values)
            {
                var info = meta.FirstOrDefault(m => m.Name.Equals(pair.Key, StringComparison.OrdinalIgnoreCase));
                if (info == null || info.IsReadOnly) continue;
                values[pair.Key] = Decode(pair.Value, info.DataType);
            }
            var identity = BuildLogicalIdentity(values);
            if (identity.Count > 0 && await db.RowExistsByValuesAsync(tableName, identity, ct))
            {
                remaining.Add(row);
                continue;
            }
            try { await db.InsertRowAsync(tableName, values, ct); restored++; }
            catch { remaining.Add(row); }
        }

        if (remaining.Count == 0) File.Delete(path);
        else await WriteAsync(path, new ArchiveDocument { Version = doc.Version, TableName = doc.TableName, Database = doc.Database, ClassName = doc.ClassName, Rows = remaining }, ct);
        return restored;
    }

    private static ArchivedRow ToRow(DataRow row)
    {
        var result = new ArchivedRow();
        foreach (DataColumn c in row.Table.Columns)
        {
            object value = row[c];
            if (value == null || value == DBNull.Value) result.Values[c.ColumnName] = null;
            else if (value is byte[] bytes) result.Values[c.ColumnName] = "__BASE64__" + Convert.ToBase64String(bytes);
            else if (value is DateTime dt) result.Values[c.ColumnName] = "__DATETIME__" + dt.ToString("O", CultureInfo.InvariantCulture);
            else result.Values[c.ColumnName] = Convert.ToString(value, CultureInfo.InvariantCulture);
        }
        return result;
    }

    private static Dictionary<string, object> BuildLogicalIdentity(Dictionary<string, object> values)
    {
        var r = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "PLC", "Record", "Area" })
        {
            var key = values.Keys.FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (key != null) r[key] = values[key];
        }
        return r.Count >= 2 ? r : new Dictionary<string, object>(values.Where(x => x.Key.Equals("Record", StringComparison.OrdinalIgnoreCase)).ToDictionary(x => x.Key, x => x.Value));
    }

    private static object Decode(string value, string type)
    {
        if (value == null) return DBNull.Value;
        if (value.StartsWith("__BASE64__", StringComparison.Ordinal)) return Convert.FromBase64String(value.Substring(10));
        if (value.StartsWith("__DATETIME__", StringComparison.Ordinal) && DateTime.TryParse(value.Substring(12), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)) return dt;
        string t = (type ?? "").ToLowerInvariant();
        try
        {
            return t switch
            {
                "int" => int.Parse(value, CultureInfo.InvariantCulture),
                "bigint" => long.Parse(value, CultureInfo.InvariantCulture),
                "smallint" => short.Parse(value, CultureInfo.InvariantCulture),
                "tinyint" => byte.Parse(value, CultureInfo.InvariantCulture),
                "bit" => value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase),
                "decimal" or "numeric" or "money" or "smallmoney" => decimal.Parse(value, CultureInfo.InvariantCulture),
                "float" => double.Parse(value, CultureInfo.InvariantCulture),
                "real" => float.Parse(value, CultureInfo.InvariantCulture),
                "uniqueidentifier" => Guid.Parse(value),
                "datetime" or "datetime2" or "smalldatetime" or "date" => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                "datetimeoffset" => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture),
                "time" => TimeSpan.Parse(value, CultureInfo.InvariantCulture),
                "binary" or "varbinary" or "image" => Convert.FromBase64String(value),
                _ => value
            };
        }
        catch { return value; }
    }

    private async Task<ArchiveDocument> ReadAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return null;
        string text = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<ArchiveDocument>(text);
    }

    private static Task WriteAsync(string path, ArchiveDocument doc, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var options = new JsonSerializerOptions { WriteIndented = true };
        return File.WriteAllTextAsync(path, JsonSerializer.Serialize(doc, options), ct);
    }

    private static string Safe(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown";
        foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
        return value.Replace('\\', '_').Replace('/', '_');
    }

    private sealed class ArchiveDocument
    {
        public int Version { get; set; }
        public string Database { get; set; }
        public string ClassName { get; set; }
        public string TableName { get; set; }
        public List<ArchivedRow> Rows { get; set; } = new();
    }

    private sealed class ArchivedRow
    {
        public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

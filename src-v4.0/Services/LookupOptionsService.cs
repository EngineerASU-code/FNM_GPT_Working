using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Configurator.Core.Architecture;

namespace Configurator;

public sealed class LookupOptionsService
{
    private readonly DatabaseService _db;
    private readonly Dictionary<string, IReadOnlyList<LookupOption>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public LookupOptionsService(DatabaseService db) => _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<IReadOnlyList<LookupOption>> TryGetAsync(string className, string tableName, string fieldName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fieldName)) return Array.Empty<LookupOption>();

        string lookupTable = null;
        string preferredKey = null;
        string displayHint = null;
        bool combineAreaNames = false;

        switch (fieldName.Trim())
        {
            case "Area":
            case "AreaName":
                lookupTable = "dbo.Areas";
                preferredKey = "AreaName";
                displayHint = "AreaName";
                combineAreaNames = true;
                break;
            case "Unit":
                lookupTable = "dbo.Units";
                preferredKey = "Unit_ID";
                displayHint = "Name";
                break;
            case "PLC":
                lookupTable = "dbo.PLC";
                preferredKey = "Record";
                displayHint = "Name";
                break;
            case "Class":
                lookupTable = "dbo.Classes";
                preferredKey = "Record";
                displayHint = "Name";
                break;
            case "Type":
                var definition = SystemLookupCatalog.ForClass(className);
                lookupTable = definition?.LookupTable;
                preferredKey = definition?.LookupKeyField;
                displayHint = "Name";
                break;
        }

        if (string.IsNullOrWhiteSpace(lookupTable)) return Array.Empty<LookupOption>();

        var tables = await _db.GetTableListAsync(ct);
        bool exists = tables.Rows.Cast<DataRow>().Any(r =>
        {
            var full = $"{Convert.ToString(r["TABLE_SCHEMA"])}.{Convert.ToString(r["TABLE_NAME"])}";
            return full.Equals(lookupTable, StringComparison.OrdinalIgnoreCase);
        });
        if (!exists) return Array.Empty<LookupOption>();

        var columns = await _db.GetTableColumnsAsync(lookupTable, ct);
        if (columns.Count == 0) return Array.Empty<LookupOption>();

        string key = columns.FirstOrDefault(c => !string.IsNullOrWhiteSpace(preferredKey) && c.Name.Equals(preferredKey, StringComparison.OrdinalIgnoreCase))?.Name
                     ?? columns.FirstOrDefault(c => c.Name.Equals("Record", StringComparison.OrdinalIgnoreCase))?.Name
                     ?? columns.FirstOrDefault(c => c.Name.Equals("Type", StringComparison.OrdinalIgnoreCase))?.Name
                     ?? (await _db.GetPrimaryKeyColumnsAsync(lookupTable, ct)).FirstOrDefault()
                     ?? columns.FirstOrDefault()?.Name;
        if (string.IsNullOrWhiteSpace(key)) return Array.Empty<LookupOption>();

        string cacheKey = lookupTable + "|" + key + "|" + (combineAreaNames ? "area" : displayHint ?? "");
        if (_cache.TryGetValue(cacheKey, out var cached)) return cached;

        var order = new[] { key }.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        var data = await _db.GetTableDataPageAsync(lookupTable, 1, 2000, order, null, ct);
        var options = new List<LookupOption>();
        foreach (DataRow row in data.Rows)
        {
            if (row[key] == DBNull.Value) continue;
            string keyText = Convert.ToString(row[key]) ?? "";
            if (string.IsNullOrWhiteSpace(keyText)) continue;
            string display = BuildDisplay(row, columns.Select(x => x.Name).ToList(), key, displayHint, combineAreaNames);
            options.Add(new LookupOption { Key = keyText, Display = display });
        }

        var result = options
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        _cache[cacheKey] = result;
        return result;
    }

    private static string BuildDisplay(DataRow row, IReadOnlyList<string> names, string key, string hint, bool combineAreaNames)
    {
        if (combineAreaNames && row.Table.Columns.Contains("AreaName"))
        {
            string l1 = Convert.ToString(row["AreaName"]) ?? "";
            string l2 = row.Table.Columns.Contains("AreaNameL2") ? Convert.ToString(row["AreaNameL2"]) ?? "" : "";
            if (!string.IsNullOrWhiteSpace(l2) && !l2.Equals(l1, StringComparison.OrdinalIgnoreCase)) return $"{l1} · {l2}";
            return l1;
        }

        foreach (var candidate in new[] { hint, "Name", "NameL1", "Name_L1", "NameL2", "Name_L2", "Description", "DescriptionL1", "Title" })
        {
            if (string.IsNullOrWhiteSpace(candidate) || !row.Table.Columns.Contains(candidate)) continue;
            string value = Convert.ToString(row[candidate]) ?? "";
            if (!string.IsNullOrWhiteSpace(value)) return $"{value}  [{Convert.ToString(row[key])}]";
        }
        return Convert.ToString(row[key]) ?? "";
    }
}

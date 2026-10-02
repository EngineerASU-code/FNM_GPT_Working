using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Configurator;

public static class ExcelExportService
{
    private const int ExcelMaxRows = 1_048_576;
    private const int ExcelMaxColumns = 16_384;

    public static async Task ExportDatabaseAsync(string connectionString, string filePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("Строка подключения не задана.", nameof(connectionString));
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("Путь к файлу не задан.", nameof(filePath));

        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        var tables = await LoadTablesAsync(connection, ct);
        var relations = await LoadForeignKeysAsync(connection, ct);
        var primaryKeys = await LoadPrimaryKeysAsync(connection, ct);
        var indexes = await LoadIndexesAsync(connection, ct);

        using var fs = File.Create(filePath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        Add(zip, "[Content_Types].xml", ContentTypesXml(tables.Count + 4));
        Add(zip, "_rels/.rels", RootRelsXml());
        Add(zip, "xl/workbook.xml", WorkbookXml(tables));
        Add(zip, "xl/_rels/workbook.xml.rels", WorkbookRelsXml(tables.Count + 4));
        Add(zip, "xl/styles.xml", StylesXml());

        WriteMetadataSheet(zip, 1, "__SchemaInfo", new[] { "Property", "Value" }, new[]
        {
            new object[] { "ExportedAtUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) },
            new object[] { "TableCount", tables.Count },
            new object[] { "ForeignKeyCount", relations.Count },
            new object[] { "PrimaryKeyCount", primaryKeys.Count },
            new object[] { "IndexCount", indexes.Count },
            new object[] { "Note", "Configurator exports the current database without changing any database relations." }
        });

        WriteMetadataSheet(zip, 2, "__PrimaryKeys", new[] { "Schema", "Table", "Column", "KeyOrdinal" },
            primaryKeys.Select(x => new object[] { x.Schema, x.Table, x.Column, x.KeyOrdinal }));
        WriteMetadataSheet(zip, 3, "__ForeignKeys", new[] { "Constraint", "SourceSchema", "SourceTable", "SourceColumn", "TargetSchema", "TargetTable", "TargetColumn", "KeyOrdinal" },
            relations.Select(x => new object[] { x.Constraint, x.SourceSchema, x.SourceTable, x.SourceColumn, x.TargetSchema, x.TargetTable, x.TargetColumn, x.KeyOrdinal }));
        WriteMetadataSheet(zip, 4, "__Indexes", new[] { "Schema", "Table", "IndexName", "IsUnique", "IsPrimaryKey", "Column", "KeyOrdinal", "IsIncluded" },
            indexes.Select(x => new object[] { x.Schema, x.Table, x.IndexName, x.IsUnique, x.IsPrimaryKey, x.Column, x.KeyOrdinal, x.IsIncluded }));

        int sheetId = 5;
        foreach (var table in tables)
        {
            ct.ThrowIfCancellationRequested();
            await WriteTableSheetAsync(zip, sheetId++, table.Schema, table.Name, connection, ct);
        }
    }

    // Kept for compatibility with existing one-sheet exports.
    public static void Export(string filePath, string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object>> rows)
    {
        var safeSheet = SafeSheetName(sheetName);
        using var fs = File.Create(filePath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        Add(zip, "[Content_Types].xml", ContentTypesXml(1));
        Add(zip, "_rels/.rels", RootRelsXml());
        Add(zip, "xl/workbook.xml", WorkbookXml(new[] { new DbTable("dbo", safeSheet) }));
        Add(zip, "xl/_rels/workbook.xml.rels", WorkbookRelsXml(1));
        Add(zip, "xl/styles.xml", StylesXml());
        WriteSheet(zip, 1, safeSheet, headers, rows);
    }

    private static async Task<List<DbTable>> LoadTablesAsync(SqlConnection connection, CancellationToken ct)
    {
        const string sql = @"SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE' ORDER BY TABLE_SCHEMA, TABLE_NAME";
        using var cmd = new SqlCommand(sql, connection);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<DbTable>();
        while (await reader.ReadAsync(ct)) result.Add(new DbTable(reader.GetString(0), reader.GetString(1)));
        return result;
    }

    private static async Task<List<DbRelation>> LoadForeignKeysAsync(SqlConnection connection, CancellationToken ct)
    {
        const string sql = @"
SELECT fk.name, s1.name, t1.name, c1.name, s2.name, t2.name, c2.name, fkc.constraint_column_id
FROM sys.foreign_keys fk
JOIN sys.foreign_key_columns fkc ON fk.object_id=fkc.constraint_object_id
JOIN sys.tables t1 ON fkc.parent_object_id=t1.object_id
JOIN sys.schemas s1 ON t1.schema_id=s1.schema_id
JOIN sys.columns c1 ON c1.object_id=t1.object_id AND c1.column_id=fkc.parent_column_id
JOIN sys.tables t2 ON fkc.referenced_object_id=t2.object_id
JOIN sys.schemas s2 ON t2.schema_id=s2.schema_id
JOIN sys.columns c2 ON c2.object_id=t2.object_id AND c2.column_id=fkc.referenced_column_id
ORDER BY s1.name,t1.name,fk.name,fkc.constraint_column_id";
        using var cmd = new SqlCommand(sql, connection);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<DbRelation>();
        while (await reader.ReadAsync(ct)) result.Add(new DbRelation(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetInt32(7)));
        return result;
    }

    private static async Task<List<DbPrimaryKey>> LoadPrimaryKeysAsync(SqlConnection connection, CancellationToken ct)
    {
        const string sql = @"
SELECT s.name,t.name,c.name,ic.key_ordinal
FROM sys.indexes i
JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id
JOIN sys.tables t ON i.object_id=t.object_id
JOIN sys.schemas s ON t.schema_id=s.schema_id
JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
WHERE i.is_primary_key=1
ORDER BY s.name,t.name,ic.key_ordinal";
        using var cmd = new SqlCommand(sql, connection);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<DbPrimaryKey>();
        while (await reader.ReadAsync(ct)) result.Add(new DbPrimaryKey(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3)));
        return result;
    }

    private static async Task<List<DbIndex>> LoadIndexesAsync(SqlConnection connection, CancellationToken ct)
    {
        const string sql = @"
SELECT s.name,t.name,i.name,i.is_unique,i.is_primary_key,c.name,ic.key_ordinal,ic.is_included_column
FROM sys.indexes i
JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id
JOIN sys.tables t ON i.object_id=t.object_id
JOIN sys.schemas s ON t.schema_id=s.schema_id
JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
WHERE i.index_id > 0 AND i.is_hypothetical=0
ORDER BY s.name,t.name,i.name,ic.key_ordinal,c.column_id";
        using var cmd = new SqlCommand(sql, connection);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<DbIndex>();
        while (await reader.ReadAsync(ct)) result.Add(new DbIndex(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3), reader.GetBoolean(4), reader.GetString(5), reader.GetInt32(6), reader.GetBoolean(7)));
        return result;
    }

    private static async Task WriteTableSheetAsync(ZipArchive zip, int sheetId, string schema, string tableName, SqlConnection connection, CancellationToken ct)
    {
        string sheetName = SafeSheetName(schema + "_" + tableName);
        var entry = zip.CreateEntry($"xl/worksheets/sheet{sheetId}.xml", CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 65536, leaveOpen: false);
        await writer.WriteAsync("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");

        using var cmd = new SqlCommand($"SELECT * FROM [{schema.Replace("]", "]]" )}].[{tableName.Replace("]", "]]" )}]", connection) { CommandTimeout = 0 };
        using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
        int fieldCount = Math.Min(reader.FieldCount, ExcelMaxColumns);
        int rowNumber = 1;
        await writer.WriteAsync($"<row r=\"1\">");
        for (int i = 0; i < fieldCount; i++) await writer.WriteAsync(Cell(reader.GetName(i), 1, i + 1, 1));
        await writer.WriteAsync("</row>");

        while (await reader.ReadAsync(ct) && rowNumber < ExcelMaxRows)
        {
            rowNumber++;
            await writer.WriteAsync($"<row r=\"{rowNumber}\">");
            for (int i = 0; i < fieldCount; i++) await writer.WriteAsync(Cell(reader.IsDBNull(i) ? null : reader.GetValue(i), rowNumber, i + 1));
            await writer.WriteAsync("</row>");
        }
        await writer.WriteAsync("</sheetData></worksheet>");
    }

    private static void WriteMetadataSheet(ZipArchive zip, int sheetId, string sheetName, IReadOnlyList<string> headers, IEnumerable<object[]> rows)
        => WriteSheet(zip, sheetId, sheetName, headers, rows.Select(x => (IReadOnlyList<object>)x));

    private static void WriteSheet(ZipArchive zip, int sheetId, string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object>> rows)
    {
        var entry = zip.CreateEntry($"xl/worksheets/sheet{sheetId}.xml", CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        var materialized = rows.Take(ExcelMaxRows - 1).ToList();
        writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        writer.Write("<row r=\"1\">");
        for (int i = 0; i < Math.Min(headers.Count, ExcelMaxColumns); i++) writer.Write(Cell(headers[i], 1, i + 1, 1));
        writer.Write("</row>");
        int row = 1;
        foreach (var values in materialized)
        {
            row++;
            writer.Write($"<row r=\"{row}\">");
            for (int i = 0; i < Math.Min(values.Count, ExcelMaxColumns); i++) writer.Write(Cell(values[i], row, i + 1));
            writer.Write("</row>");
        }
        writer.Write("</sheetData></worksheet>");
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string Cell(object value, int row, int col, int styleIndex = 0)
    {
        string reference = ColumnName(col) + row;
        string style = styleIndex > 0 ? $" s=\"{styleIndex}\"" : "";
        if (value == null || value == DBNull.Value) return $"<c r=\"{reference}\"{style}/>";
        if (value is bool b) return $"<c r=\"{reference}\"{style} t=\"b\"><v>{(b ? 1 : 0)}</v></c>";
        if (value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
            return $"<c r=\"{reference}\"{style}><v>{Convert.ToString(value, CultureInfo.InvariantCulture)}</v></c>";
        return $"<c r=\"{reference}\"{style} t=\"inlineStr\"><is><t xml:space=\"preserve\">{Esc(Convert.ToString(value, CultureInfo.InvariantCulture))}</t></is></c>";
    }

    private static string ColumnName(int col)
    {
        string result = "";
        int n = col;
        while (n > 0) { n--; result = (char)('A' + n % 26) + result; n /= 26; }
        return result;
    }

    private static string SafeSheetName(string name)
    {
        string safe = new string((name ?? "Лист").Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ' ' ? c : '_').ToArray());
        if (string.IsNullOrWhiteSpace(safe)) safe = "Лист";
        return safe.Length > 31 ? safe[..31] : safe;
    }

    private static string Esc(string value) => SecurityElement.Escape(value ?? "") ?? "";

    private static string ContentTypesXml(int sheetCount) => $"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>{string.Concat(Enumerable.Range(1, sheetCount).Select(i => $"<Override PartName=\"/xl/worksheets/sheet{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"))}</Types>
""";

    private static string RootRelsXml() => """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""";

    private static string WorkbookXml(IReadOnlyList<DbTable> tables)
    {
        var sheets = new[] { "__SchemaInfo", "__PrimaryKeys", "__ForeignKeys", "__Indexes" }.Concat(tables.Select(t => SafeSheetName(t.Schema + "_" + t.Name))).ToList();
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
        for (int i = 0; i < sheets.Count; i++) sb.Append($"<sheet name=\"{Esc(sheets[i])}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
        return sb.Append("</sheets></workbook>").ToString();
    }

    private static string WorkbookRelsXml(int sheetCount) => $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">{string.Concat(Enumerable.Range(1, sheetCount).Select(i => $"<Relationship Id=\"rId{i}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i}.xml\"/>"))}<Relationship Id=\"rId{sheetCount + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>";

    private static string StylesXml() => """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/></cellXfs></styleSheet>""";

    private sealed record DbTable(string Schema, string Name);
    private sealed record DbRelation(string Constraint, string SourceSchema, string SourceTable, string SourceColumn, string TargetSchema, string TargetTable, string TargetColumn, int KeyOrdinal);
    private sealed record DbPrimaryKey(string Schema, string Table, string Column, int KeyOrdinal);
    private sealed record DbIndex(string Schema, string Table, string IndexName, bool IsUnique, bool IsPrimaryKey, string Column, int KeyOrdinal, bool IsIncluded);
}

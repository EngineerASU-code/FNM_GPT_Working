using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;

namespace Configurator;

public static class ExcelExportService
{
    public static void Export(string filePath, string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object>> rows)
    {
        var safeSheet = string.IsNullOrWhiteSpace(sheetName) ? "Результат" : sheetName;
        safeSheet = new string(safeSheet.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ' ' ? c : '_').ToArray());
        if (safeSheet.Length > 31) safeSheet = safeSheet[..31];

        using var fs = File.Create(filePath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        Add(zip, "[Content_Types].xml", ContentTypesXml());
        Add(zip, "_rels/.rels", RootRelsXml());
        Add(zip, "xl/workbook.xml", WorkbookXml(safeSheet));
        Add(zip, "xl/_rels/workbook.xml.rels", WorkbookRelsXml());
        Add(zip, "xl/styles.xml", StylesXml());
        Add(zip, "xl/worksheets/sheet1.xml", SheetXml(headers, rows));
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string Esc(string value) => SecurityElement.Escape(value ?? "") ?? "";

    private static string Cell(object value, int row, int col)
    {
        string reference = ColumnName(col) + row;
        if (value == null || value == DBNull.Value)
            return $"<c r=\"{reference}\"/>";

        if (value is bool b)
            return $"<c r=\"{reference}\" t=\"b\"><v>{(b ? 1 : 0)}</v></c>";

        if (value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
            return $"<c r=\"{reference}\"><v>{Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)}</v></c>";

        return $"<c r=\"{reference}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Esc(Convert.ToString(value))}</t></is></c>";
    }

    private static string ColumnName(int col)
    {
        var result = "";
        var n = col;
        while (n > 0)
        {
            n--;
            result = (char)('A' + (n % 26)) + result;
            n /= 26;
        }
        return result;
    }

    private static string SheetXml(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object>> rows)
    {
        var materialized = rows.Select(r => r.ToArray()).ToList();
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        sb.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
        if (headers.Count > 0)
            sb.Append($"<autoFilter ref=\"A1:{ColumnName(headers.Count)}{Math.Max(1, materialized.Count + 1)}\"/>");
        sb.Append("<cols>");
        for (int i = 0; i < headers.Count; i++)
        {
            var max = headers[i]?.Length ?? 0;
            foreach (var row in materialized.Take(250))
                if (i < row.Length) max = Math.Max(max, Convert.ToString(row[i])?.Length ?? 0);
            var width = Math.Min(55, Math.Max(12, max + 2));
            sb.Append($"<col min=\"{i + 1}\" max=\"{i + 1}\" width=\"{width.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
        }
        sb.Append("</cols><sheetData>");
        int rowNumber = 1;
        sb.Append($"<row r=\"{rowNumber}\" ht=\"22\" customHeight=\"1\">");
        for (int i = 0; i < headers.Count; i++) sb.Append(Cell(headers[i], rowNumber, i + 1, 1));
        sb.Append("</row>");
        foreach (var row in materialized)
        {
            rowNumber++;
            sb.Append($"<row r=\"{rowNumber}\">");
            for (int i = 0; i < row.Length; i++) sb.Append(Cell(row[i], rowNumber, i + 1, 0));
            sb.Append("</row>");
        }
        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    private static string Cell(object value, int row, int col, int styleIndex = 0)
    {
        string reference = ColumnName(col) + row;
        var style = styleIndex > 0 ? $" s=\"{styleIndex}\"" : "";
        if (value == null || value == DBNull.Value) return $"<c r=\"{reference}\"{style}/>";
        if (value is bool b) return $"<c r=\"{reference}\"{style} t=\"b\"><v>{(b ? 1 : 0)}</v></c>";
        if (value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
            return $"<c r=\"{reference}\"{style}><v>{Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)}</v></c>";
        return $"<c r=\"{reference}\"{style} t=\"inlineStr\"><is><t xml:space=\"preserve\">{Esc(Convert.ToString(value))}</t></is></c>";
    }

    private static string ContentTypesXml() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
<Default Extension="xml" ContentType="application/xml"/>
<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
<Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
</Types>
""";

    private static string RootRelsXml() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
</Relationships>
""";

    private static string WorkbookXml(string sheetName) => $"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
<sheets><sheet name="{Esc(sheetName)}" sheetId="1" r:id="rId1"/></sheets>
</workbook>
""";

    private static string WorkbookRelsXml() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
</Relationships>
""";

    private static string StylesXml() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
<numFmts count="0"/><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts>
<fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
<borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
<cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
<cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/></cellXfs>
</styleSheet>
""";
}

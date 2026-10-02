using System;
using System.Collections.Generic;
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
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        var tables = await LoadTablesAsync(connection, ct);
        var relations = await LoadForeignKeysAsync(connection, ct);
        var primaryKeys = await LoadPrimaryKeysAsync(connection, ct);
        var indexes = await LoadIndexesAsync(connection, ct);

        using var fs = File.Create(filePath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        int sheetCount = tables.Count + 4;
        Add(zip, "[Content_Types].xml", ContentTypesXml(sheetCount));
        Add(zip, "_rels/.rels", RootRelsXml());
        Add(zip, "xl/workbook.xml", WorkbookXml(tables));
        Add(zip, "xl/_rels/workbook.xml.rels", WorkbookRelsXml(sheetCount));
        Add(zip, "xl/styles.xml", StylesXml());
        WriteMetadataSheet(zip, 1, "__SchemaInfo", new[] { "Property", "Value" }, new object[][] {
            new object[] { "ExportedAtUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) },
            new object[] { "TableCount", tables.Count }, new object[] { "ForeignKeyCount", relations.Count },
            new object[] { "PrimaryKeyCount", primaryKeys.Count }, new object[] { "IndexCount", indexes.Count },
            new object[] { "Note", "Current database only; no database relations are changed by export." }
        });
        WriteMetadataSheet(zip, 2, "__PrimaryKeys", new[] { "Schema", "Table", "Column", "KeyOrdinal" }, primaryKeys.Select(x => new object[] { x.Schema, x.Table, x.Column, x.KeyOrdinal }));
        WriteMetadataSheet(zip, 3, "__ForeignKeys", new[] { "Constraint", "SourceSchema", "SourceTable", "SourceColumn", "TargetSchema", "TargetTable", "TargetColumn", "KeyOrdinal" }, relations.Select(x => new object[] { x.Constraint, x.SourceSchema, x.SourceTable, x.SourceColumn, x.TargetSchema, x.TargetTable, x.TargetColumn, x.KeyOrdinal }));
        WriteMetadataSheet(zip, 4, "__Indexes", new[] { "Schema", "Table", "IndexName", "IsUnique", "IsPrimaryKey", "Column", "KeyOrdinal", "IsIncluded" }, indexes.Select(x => new object[] { x.Schema, x.Table, x.IndexName, x.IsUnique, x.IsPrimaryKey, x.Column, x.KeyOrdinal, x.IsIncluded }));

        int sheetId = 5;
        foreach (var table in tables)
        {
            ct.ThrowIfCancellationRequested();
            await WriteTableSheetAsync(zip, sheetId++, table.Schema, table.Name, connection, ct);
        }
    }

    public static void Export(string filePath, string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object>> rows)
    {
        using var fs = File.Create(filePath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        Add(zip, "[Content_Types].xml", ContentTypesXml(1)); Add(zip, "_rels/.rels", RootRelsXml());
        Add(zip, "xl/workbook.xml", WorkbookXml(new[] { new DbTable("dbo", SafeSheetName(sheetName)) }));
        Add(zip, "xl/_rels/workbook.xml.rels", WorkbookRelsXml(1)); Add(zip, "xl/styles.xml", StylesXml());
        WriteSheet(zip, 1, SafeSheetName(sheetName), headers, rows);
    }

    private static async Task<List<DbTable>> LoadTablesAsync(SqlConnection c, CancellationToken ct)
    {
        using var cmd = new SqlCommand("SELECT TABLE_SCHEMA,TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE' ORDER BY TABLE_SCHEMA,TABLE_NAME", c);
        using var r = await cmd.ExecuteReaderAsync(ct); var list = new List<DbTable>();
        while (await r.ReadAsync(ct)) list.Add(new DbTable(r.GetString(0), r.GetString(1))); return list;
    }

    private static async Task<List<DbRelation>> LoadForeignKeysAsync(SqlConnection c, CancellationToken ct)
    {
        const string sql = @"SELECT fk.name,s1.name,t1.name,c1.name,s2.name,t2.name,c2.name,fkc.constraint_column_id FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fkc ON fk.object_id=fkc.constraint_object_id JOIN sys.tables t1 ON fkc.parent_object_id=t1.object_id JOIN sys.schemas s1 ON t1.schema_id=s1.schema_id JOIN sys.columns c1 ON c1.object_id=t1.object_id AND c1.column_id=fkc.parent_column_id JOIN sys.tables t2 ON fkc.referenced_object_id=t2.object_id JOIN sys.schemas s2 ON t2.schema_id=s2.schema_id JOIN sys.columns c2 ON c2.object_id=t2.object_id AND c2.column_id=fkc.referenced_column_id ORDER BY s1.name,t1.name,fk.name,fkc.constraint_column_id";
        using var cmd = new SqlCommand(sql, c); using var r = await cmd.ExecuteReaderAsync(ct); var list = new List<DbRelation>();
        while (await r.ReadAsync(ct)) list.Add(new DbRelation(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetInt32(7))); return list;
    }

    private static async Task<List<DbPrimaryKey>> LoadPrimaryKeysAsync(SqlConnection c, CancellationToken ct)
    {
        const string sql = @"SELECT s.name,t.name,c.name,ic.key_ordinal FROM sys.indexes i JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id JOIN sys.tables t ON i.object_id=t.object_id JOIN sys.schemas s ON t.schema_id=s.schema_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.is_primary_key=1 ORDER BY s.name,t.name,ic.key_ordinal";
        using var cmd = new SqlCommand(sql,c); using var r = await cmd.ExecuteReaderAsync(ct); var list = new List<DbPrimaryKey>();
        while (await r.ReadAsync(ct)) list.Add(new DbPrimaryKey(r.GetString(0),r.GetString(1),r.GetString(2),r.GetInt32(3))); return list;
    }

    private static async Task<List<DbIndex>> LoadIndexesAsync(SqlConnection c, CancellationToken ct)
    {
        const string sql = @"SELECT s.name,t.name,i.name,i.is_unique,i.is_primary_key,c.name,ic.key_ordinal,ic.is_included_column FROM sys.indexes i JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id JOIN sys.tables t ON i.object_id=t.object_id JOIN sys.schemas s ON t.schema_id=s.schema_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.index_id>0 AND i.is_hypothetical=0 ORDER BY s.name,t.name,i.name,ic.key_ordinal,c.column_id";
        using var cmd = new SqlCommand(sql,c); using var r = await cmd.ExecuteReaderAsync(ct); var list = new List<DbIndex>();
        while (await r.ReadAsync(ct)) list.Add(new DbIndex(r.GetString(0),r.GetString(1),r.GetString(2),r.GetBoolean(3),r.GetBoolean(4),r.GetString(5),r.GetInt32(6),r.GetBoolean(7))); return list;
    }

    private static async Task WriteTableSheetAsync(ZipArchive zip,int id,string schema,string table,SqlConnection c,CancellationToken ct)
    {
        var entry=zip.CreateEntry($"xl/worksheets/sheet{id}.xml",CompressionLevel.Fastest); using var w=new StreamWriter(entry.Open(),new UTF8Encoding(false),65536);
        await w.WriteAsync("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        using var cmd=new SqlCommand($"SELECT * FROM [{schema.Replace("]", "]]" )}].[{table.Replace("]", "]]" )}]",c){CommandTimeout=0}; using var r=await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess,ct);
        int count=Math.Min(r.FieldCount,ExcelMaxColumns), row=1; await w.WriteAsync("<row r=\"1\">"); for(int i=0;i<count;i++) await w.WriteAsync(Cell(r.GetName(i),1,i+1,1)); await w.WriteAsync("</row>");
        while(await r.ReadAsync(ct)&&row<ExcelMaxRows){row++;await w.WriteAsync($"<row r=\"{row}\">");for(int i=0;i<count;i++)await w.WriteAsync(Cell(r.IsDBNull(i)?null:r.GetValue(i),row,i+1));await w.WriteAsync("</row>");}
        await w.WriteAsync("</sheetData></worksheet>");
    }

    private static void WriteMetadataSheet(ZipArchive z,int id,string name,IReadOnlyList<string> headers,IEnumerable<object[]> rows)=>WriteSheet(z,id,name,headers,rows.Select(x=>(IReadOnlyList<object>)x));
    private static void WriteSheet(ZipArchive z,int id,string name,IReadOnlyList<string> headers,IEnumerable<IReadOnlyList<object>> rows)
    {
        var e=z.CreateEntry($"xl/worksheets/sheet{id}.xml",CompressionLevel.Fastest);using var w=new StreamWriter(e.Open(),new UTF8Encoding(false));var data=rows.Take(ExcelMaxRows-1).ToList();
        w.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\">");
        for(int i=0;i<Math.Min(headers.Count,ExcelMaxColumns);i++)w.Write(Cell(headers[i],1,i+1,1));w.Write("</row>");int row=1;
        foreach(var vals in data){row++;w.Write($"<row r=\"{row}\">");for(int i=0;i<Math.Min(vals.Count,ExcelMaxColumns);i++)w.Write(Cell(vals[i],row,i+1));w.Write("</row>");}w.Write("</sheetData></worksheet>");
    }

    private static void Add(ZipArchive z,string name,string content){var e=z.CreateEntry(name,CompressionLevel.Fastest);using var w=new StreamWriter(e.Open(),new UTF8Encoding(false));w.Write(content);}
    private static string Cell(object value,int row,int col,int style=0){string r=ColumnName(col)+row,s=style>0?$" s=\"{style}\"":"";if(value==null||value==DBNull.Value)return $"<c r=\"{r}\"{s}/>";if(value is bool b)return $"<c r=\"{r}\"{s} t=\"b\"><v>{(b?1:0)}</v></c>";if(value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)return $"<c r=\"{r}\"{s}><v>{Convert.ToString(value,CultureInfo.InvariantCulture)}</v></c>";return $"<c r=\"{r}\"{s} t=\"inlineStr\"><is><t xml:space=\"preserve\">{Esc(Convert.ToString(value,CultureInfo.InvariantCulture))}</t></is></c>";}
    private static string ColumnName(int col){string r="";for(int n=col;n>0;){n--;r=(char)('A'+n%26)+r;n/=26;}return r;}
    private static string SafeSheetName(string name){var s=new string((name??"Лист").Select(c=>char.IsLetterOrDigit(c)||c=='_'||c=='-'||c==' '?c:'_').ToArray());if(string.IsNullOrWhiteSpace(s))s="Лист";return s.Length>31?s[..31]:s;}
    private static string Esc(string v)=>SecurityElement.Escape(v??"")??"";
    private static string ContentTypesXml(int n)=>$"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>{string.Concat(Enumerable.Range(1,n).Select(i=>$"<Override PartName=\"/xl/worksheets/sheet{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"))}</Types>";
    private static string RootRelsXml()=>"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>";
    private static string WorkbookXml(IReadOnlyList<DbTable> t){var names=new[]{"__SchemaInfo","__PrimaryKeys","__ForeignKeys","__Indexes"}.Concat(t.Select(x=>SafeSheetName(x.Schema+"_"+x.Name))).ToList();var b=new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");for(int i=0;i<names.Count;i++)b.Append($"<sheet name=\"{Esc(names[i])}\" sheetId=\"{i+1}\" r:id=\"rId{i+1}\"/>");return b.Append("</sheets></workbook>").ToString();}
    private static string WorkbookRelsXml(int n)=>$"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">{string.Concat(Enumerable.Range(1,n).Select(i=>$"<Relationship Id=\"rId{i}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i}.xml\"/>"))}<Relationship Id=\"rId{n+1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>";
    private static string StylesXml()=>"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts><fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills><borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs></styleSheet>";
    private sealed record DbTable(string Schema,string Name); private sealed record DbRelation(string Constraint,string SourceSchema,string SourceTable,string SourceColumn,string TargetSchema,string TargetTable,string TargetColumn,int KeyOrdinal); private sealed record DbPrimaryKey(string Schema,string Table,string Column,int KeyOrdinal); private sealed record DbIndex(string Schema,string Table,string IndexName,bool IsUnique,bool IsPrimaryKey,string Column,int KeyOrdinal,bool IsIncluded);
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace Configurator.Core.Architecture;

/// <summary>Identifies complex roots without collapsing their child tables into one record.</summary>
public static class ComplexEntityCatalog
{
    public static ComplexEntityDescriptor Resolve(DatabaseTable table, DatabaseSchema schema)
    {
        if (table == null) return null;
        if (table.Name.Equals("Prog", StringComparison.OrdinalIgnoreCase)) return BuildProgram(schema);
        if (table.Name.Equals("Matrix_List", StringComparison.OrdinalIgnoreCase)) return BuildMatrix(schema);
        return null;
    }

    private static ComplexEntityDescriptor BuildProgram(DatabaseSchema schema) =>
        new("Program", "dbo.Prog",
            schema.Tables.Where(t => t.Name.StartsWith("Prog_", StringComparison.OrdinalIgnoreCase)).Select(t => t.FullName).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            "Root key: PLC + PLC_Class_Number + Record. Children are linked by program/sequence/step context; never CRUD the graph as one SQL row.");

    private static ComplexEntityDescriptor BuildMatrix(DatabaseSchema schema) =>
        new("Matrix", "dbo.Matrix_List",
            schema.Tables.Where(t => t.Name.StartsWith("Matrix_", StringComparison.OrdinalIgnoreCase)).Select(t => t.FullName).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            "Root key: PLC + PLC_Class_Number + Record. Device references are polymorphic and resolve through Classes.DBName.");
}

public sealed record ComplexEntityDescriptor(string Name, string RootTable, IReadOnlyList<string> ChildTables, string Rule);

using System;
using System.Collections.Generic;
using System.Linq;
using Configurator.Core.Architecture;

namespace Configurator;

public partial class MainWindow
{
    private static int? ResolveSystemClassNumber(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            return null;

        string normalizedTable = NormalizeTableName(tableName);
        foreach (var definition in SystemClassCatalog.CreateDefaults())
        {
            var primary = definition.StorageMappings.FirstOrDefault(x =>
                string.Equals(x.Role, "primary", StringComparison.OrdinalIgnoreCase));
            if (primary == null || definition.ClassNumber <= 0)
                continue;

            if (string.Equals(NormalizeTableName(primary.TableName), normalizedTable, StringComparison.OrdinalIgnoreCase))
                return definition.ClassNumber;
        }

        return null;
    }

    private static string ResolveClassNumberColumn(IEnumerable<ColumnInfo> columns)
    {
        return columns?.FirstOrDefault(IsClassNumberColumn)?.Name;
    }

    private static bool IsClassNumberColumn(ColumnInfo column)
    {
        if (column == null || string.IsNullOrWhiteSpace(column.Name))
            return false;

        string normalized = NormalizeIdentifier(column.Name);
        return normalized == "classnumber" || normalized == "plcclassnumber";
    }

    private static string NormalizeTableName(string value)
        => (value ?? "").Trim().Replace("[", "").Replace("]", "").ToLowerInvariant();

    private static string NormalizeIdentifier(string value)
        => new string((value ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Configurator.Core.Architecture;

public static class ArchitectureFingerprint
{
    public static string Create(DatabaseSchema schema)
    {
        var canonical = string.Join("\n",
            schema.Tables.OrderBy(t => t.FullName, StringComparer.OrdinalIgnoreCase)
                .Select(t => t.FullName + "|" + string.Join(",", t.Columns.Select(c => c.Name + ":" + c.DataType + ":" + c.IsNullable)))
                .Concat(schema.Relations.OrderBy(r => r.RuleId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(r => r.SourceTable, StringComparer.OrdinalIgnoreCase)
                    .Select(r => $"R|{r.RuleId}|{r.SourceTable}|{string.Join(",", r.SourceColumns)}|{r.TargetTable}|{string.Join(",", r.TargetColumns)}")));

        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical)));
    }
}

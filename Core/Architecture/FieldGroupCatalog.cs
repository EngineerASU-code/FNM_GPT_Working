using System;
using System.Collections.Generic;

namespace Configurator.Core.Architecture;

public static class FieldGroupCatalog
{
    public static string Resolve(string className, string fieldName)
    {
        string n = fieldName ?? string.Empty;
        string c = className ?? string.Empty;
        string u = n.Replace("_", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();

        if (c.Equals("Matrix", StringComparison.OrdinalIgnoreCase))
            return ContainsAny(u, "name", "plc", "record", "area") ? "Object" : "Matrix";

        if (c.Equals("Status", StringComparison.OrdinalIgnoreCase))
        {
            if (ContainsAny(u, "mstype", "msno", "msnumber", "msx", "msy", "msangle", "picturenumber", "picture", "icon")) return "Visual";
            if (ContainsAny(u, "plc", "record", "area", "name", "description", "plcclassnumber")) return "Object";
            return "Config";
        }

        if (c.Equals("Step", StringComparison.OrdinalIgnoreCase))
        {
            if (ContainsAny(u, "dest", "condition", "ready", "route", "cip", "transition", "prev", "next"))
                return "Conditions";
            if (ContainsAny(u, "plc", "record", "name", "area", "description", "fcnumber"))
                return "Object";
            return "Parameters";
        }

        if (ContainsAny(u, "plc", "record", "area", "name", "description", "plcclassnumber"))
            return "Object";

        if (ContainsAny(u, "mstype", "msno", "msnumber", "msx", "msy", "msangle", "picturenumber", "picture"))
            return "Visual";

        return "Config";
    }

    public static IReadOnlyList<string> GetGroups(string className)
    {
        if (className.Equals("Step", StringComparison.OrdinalIgnoreCase))
            return new[] { "Object", "Conditions", "Parameters" };
        if (className.Equals("Program", StringComparison.OrdinalIgnoreCase))
            return new[] { "Object", "Config", "Parameters" };
        return new[] { "Object", "Config", "Visual" };
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
        foreach (var token in tokens)
            if (value.Contains(token, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}

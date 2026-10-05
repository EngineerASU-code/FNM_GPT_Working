using System;
using System.Collections.Generic;
using System.Linq;

namespace Configurator.Core.Architecture;

/// <summary>Runtime description of the currently connected project database.</summary>
public sealed class DatabaseSchema
{
    public string DatabaseName { get; init; } = "";
    public string ServerName { get; init; } = "";
    public DateTimeOffset AnalyzedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public List<DatabaseTable> Tables { get; } = new();
    public List<DatabaseRelation> Relations { get; } = new();
    public DatabaseTable FindTable(string name) => Tables.FirstOrDefault(x => string.Equals(x.FullName, name, StringComparison.OrdinalIgnoreCase));
}

public sealed class DatabaseTable
{
    public string SchemaName { get; init; } = "dbo";
    public string Name { get; init; } = "";
    public string FullName => $"{SchemaName}.{Name}";
    public List<DatabaseColumn> Columns { get; } = new();
    public List<DatabaseKey> Keys { get; } = new();
    public bool IsComplexEntity { get; init; }
}

public sealed class DatabaseColumn
{
    public string Name { get; init; } = "";
    public string DataType { get; init; } = "";
    public int MaxLength { get; init; }
    public bool IsNullable { get; init; }
    public bool IsIdentity { get; init; }
    public bool IsComputed { get; init; }
}

public sealed class DatabaseKey
{
    public string Name { get; init; } = "";
    public bool IsPrimary { get; init; }
    public bool IsUnique { get; init; }
    public List<string> Columns { get; } = new();
}

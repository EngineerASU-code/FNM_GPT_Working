using System;
using System.Collections.Generic;

namespace Configurator.Core.Architecture;

/// <summary>Per-project interpretation profile; it records mappings and rules without changing the DB.</summary>
public sealed class ProjectArchitectureProfile
{
    public string DatabaseFingerprint { get; init; } = "";
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public List<DatabaseRelation> ConfirmedRelations { get; } = new();
    public List<DatabaseRelation> UnresolvedRelations { get; } = new();
    public Dictionary<string, string> ClassTableMappings { get; } = new(StringComparer.OrdinalIgnoreCase);
}

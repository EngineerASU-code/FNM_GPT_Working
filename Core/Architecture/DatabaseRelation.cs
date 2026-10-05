using System.Collections.Generic;

namespace Configurator.Core.Architecture;

public enum DatabaseRelationKind
{
    PhysicalForeignKey,
    CompositeEntityKey,
    ConventionalLookup,
    AreaNameLookup,
    ClassLookup,
    UnitLookup,
    PolymorphicClassReference,
    ContextualProgramReference,
    UnresolvedCandidate
}

public enum RelationConfidence
{
    Confirmed,
    High,
    Medium,
    Low
}

/// <summary>Observed relation. It never instructs Configurator to create or alter an SQL constraint.</summary>
public sealed class DatabaseRelation
{
    public string SourceTable { get; init; } = "";
    public string TargetTable { get; init; } = "";
    public List<string> SourceColumns { get; } = new();
    public List<string> TargetColumns { get; } = new();
    public DatabaseRelationKind Kind { get; init; }
    public RelationConfidence Confidence { get; init; }
    public string RuleId { get; init; } = "";
    public string Comment { get; init; } = "";
    /// <summary>Medium-confidence logical dependencies are protected too: uncertainty must block destructive work rather than silently ignore a real reference.</summary>
    public bool IsUsableForDeletionProtection => Confidence is RelationConfidence.Confirmed or RelationConfidence.High or RelationConfidence.Medium;
}

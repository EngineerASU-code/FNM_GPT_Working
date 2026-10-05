using System.Collections.Generic;

namespace Configurator.Core.Architecture;

public sealed class DeletionRequest
{
    public string TableName { get; init; } = "";
    public IReadOnlyDictionary<string, object> Key { get; init; } = new Dictionary<string, object>();
}

public sealed class DependencyHit
{
    public string SourceTable { get; init; } = "";
    public IReadOnlyDictionary<string, object> SourceKey { get; init; } = new Dictionary<string, object>();
    public string SourceColumnDescription { get; init; } = "";
    public string TargetTable { get; init; } = "";
    public DatabaseRelation Relation { get; init; } = new();
}

public sealed class DeletionPlan
{
    public DeletionRequest Request { get; init; } = new();
    public List<DependencyHit> Dependencies { get; } = new();
    public List<string> ValidationErrors { get; } = new();
    public bool RequiresReplacement => Dependencies.Count > 0;
    public bool CanCommit => ValidationErrors.Count == 0 && Dependencies.TrueForAll(x => x.Relation.IsUsableForDeletionProtection);
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace Configurator.Core.Architecture;

/// <summary>Capabilities detected from the actual connected database, not from a project/version name.</summary>
public sealed class ArchitectureCapabilities
{
    public bool SupportsArea { get; set; }
    public bool SupportsPlc { get; set; }
    public bool SupportsProgram { get; set; }
    public bool SupportsStep { get; set; }
    public bool SupportsMatrix { get; set; }
    public bool SupportsValves2S { get; set; }
    public bool SupportsProgramStepParam { get; set; }
    public bool SupportsProgramStepParamHmi { get; set; }
    public List<string> UnknownTables { get; } = new();
    public List<string> Warnings { get; } = new();
}

public enum EntityScopeKind
{
    Global,
    Plc,
    PlcAndClass,
    PlcClassAndParent
}

public sealed class EntityScope
{
    public EntityScopeKind Kind { get; init; }
    public IReadOnlyList<string> KeyColumns { get; init; } = Array.Empty<string>();
    public override string ToString() => KeyColumns.Count == 0 ? Kind.ToString() : $"{Kind}: {string.Join(" + ", KeyColumns)}";
}

public enum EntityCapability
{
    Add,
    Edit,
    Clone,
    Delete,
    Move,
    Restore
}

public sealed class EntityDescriptor
{
    public string Name { get; init; } = "";
    public string TableName { get; init; } = "";
    public string PrimaryKey { get; init; } = "";
    public EntityScope Scope { get; init; } = new EntityScope();
    public List<FieldDefinition> Fields { get; } = new();
    public List<DatabaseRelation> Relations { get; } = new();
    public List<EntityDescriptor> Children { get; } = new();
    public List<EntityCapability> Capabilities { get; } = new();
    public bool IsHierarchical { get; init; }
}

public sealed class DependencyGraphNode
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string TableName { get; init; } = "";
    public List<string> Fields { get; } = new();
}

public sealed class DependencyGraphEdge
{
    public string From { get; init; } = "";
    public string To { get; init; } = "";
    public string Label { get; init; } = "";
    public DatabaseRelationKind Kind { get; init; }
    public RelationConfidence Confidence { get; init; }
}

/// <summary>Single graph model used by the Mode 2 visualizer and by backend operations.</summary>
public sealed class DependencyGraph
{
    public List<DependencyGraphNode> Nodes { get; } = new();
    public List<DependencyGraphEdge> Edges { get; } = new();

    public DependencyGraph FromSchema(DatabaseSchema schema)
    {
        Nodes.Clear();
        Edges.Clear();
        foreach (var table in schema.Tables)
            Nodes.Add(new DependencyGraphNode { Id = table.Name, Label = table.Name, TableName = table.Name });

        foreach (var relation in schema.Relations)
        {
            var from = relation.SourceTable;
            var to = relation.TargetTable;
            if (Nodes.All(x => !string.Equals(x.Id, from, StringComparison.OrdinalIgnoreCase)))
                Nodes.Add(new DependencyGraphNode { Id = from, Label = from, TableName = from });
            if (Nodes.All(x => !string.Equals(x.Id, to, StringComparison.OrdinalIgnoreCase)))
                Nodes.Add(new DependencyGraphNode { Id = to, Label = to, TableName = to });
            Edges.Add(new DependencyGraphEdge
            {
                From = from,
                To = to,
                Label = $"{string.Join(", ", relation.SourceColumns)} → {string.Join(", ", relation.TargetColumns)}",
                Kind = relation.Kind,
                Confidence = relation.Confidence
            });
        }
        return this;
    }
}

public enum OperationKind
{
    Add,
    Edit,
    Clone,
    Delete,
    Move
}

public sealed class OperationStep
{
    public string Description { get; init; } = "";
    public string TableName { get; init; } = "";
    public string SqlPreview { get; init; } = "";
    public bool IsValidation { get; init; }
}

/// <summary>Prepared change set. UI may show it as a dry-run before execution.</summary>
public sealed class OperationPlan
{
    public Guid Id { get; } = Guid.NewGuid();
    public OperationKind Kind { get; init; }
    public string EntityName { get; init; } = "";
    public string ScopeDescription { get; init; } = "";
    public List<OperationStep> Steps { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> Errors { get; } = new();
    public bool IsValid => Errors.Count == 0;
}

public sealed class OperationValidationResult
{
    public bool Success { get; init; }
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public static OperationValidationResult Ok() => new OperationValidationResult { Success = true };
}

public sealed class OperationJournalEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public OperationKind Kind { get; init; }
    public string EntityName { get; init; } = "";
    public string KeyDescription { get; init; } = "";
    public string Summary { get; init; } = "";
    public int AffectedRows { get; init; }
    public bool Committed { get; init; }
    public bool DryRun { get; init; }
    public string Validation { get; init; } = "";
}

public sealed class ArchitectureCompatibilityReport
{
    public int TableCount { get; init; }
    public int KnownEntityCount { get; init; }
    public int LogicalRelationCount { get; init; }
    public int PhysicalForeignKeyCount { get; init; }
    public int BrokenReferenceCount { get; init; }
    public ArchitectureCapabilities Capabilities { get; init; } = new ArchitectureCapabilities();
    public List<string> Messages { get; } = new();
}

/// <summary>Maps old identifiers to new identifiers during deep clone.</summary>
public sealed class ReferenceRemappingEngine
{
    private readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);

    public void Add(string oldId, string newId)
    {
        if (string.IsNullOrWhiteSpace(oldId)) throw new ArgumentException("Old identifier is empty.");
        if (string.IsNullOrWhiteSpace(newId)) throw new ArgumentException("New identifier is empty.");
        _map[oldId] = newId;
    }

    public bool TryMap(string oldId, out string newId) => _map.TryGetValue(oldId, out newId);
    public IReadOnlyDictionary<string, string> Snapshot() => new Dictionary<string, string>(_map);
}

/// <summary>Builds a hierarchical operation plan from the actual entity graph.</summary>
public sealed class HierarchicalOperationPlanner
{
    public OperationPlan PlanAdd(EntityDescriptor entity, string scope)
    {
        var plan = new OperationPlan { Kind = OperationKind.Add, EntityName = entity.Name, ScopeDescription = scope };
        plan.Steps.Add(new OperationStep { Description = $"Create {entity.Name}", TableName = entity.TableName });
        foreach (var child in entity.Children)
            AddChildren(plan, child, $"Create child {child.Name}");
        plan.Steps.Add(new OperationStep { Description = "Validate complete hierarchy", IsValidation = true });
        return plan;
    }

    public OperationPlan PlanClone(EntityDescriptor entity, string scope)
    {
        var plan = new OperationPlan { Kind = OperationKind.Clone, EntityName = entity.Name, ScopeDescription = scope };
        plan.Steps.Add(new OperationStep { Description = $"Clone root {entity.Name}", TableName = entity.TableName });
        foreach (var child in entity.Children)
            AddChildren(plan, child, $"Clone child {child.Name}");
        plan.Steps.Add(new OperationStep { Description = "Remap internal references", IsValidation = true });
        plan.Steps.Add(new OperationStep { Description = "Validate cloned hierarchy", IsValidation = true });
        return plan;
    }

    public OperationPlan PlanDelete(EntityDescriptor entity, string scope)
    {
        var plan = new OperationPlan { Kind = OperationKind.Delete, EntityName = entity.Name, ScopeDescription = scope };
        foreach (var child in entity.Children.Reverse<EntityDescriptor>())
            AddChildren(plan, child, $"Delete dependent {child.Name}");
        plan.Steps.Add(new OperationStep { Description = $"Delete {entity.Name}", TableName = entity.TableName });
        plan.Steps.Add(new OperationStep { Description = "Validate post-delete hierarchy", IsValidation = true });
        return plan;
    }

    private static void AddChildren(OperationPlan plan, EntityDescriptor entity, string description)
    {
        plan.Steps.Add(new OperationStep { Description = description, TableName = entity.TableName });
        foreach (var child in entity.Children)
            AddChildren(plan, child, $"Process child {child.Name}");
    }
}

/// <summary>Reusable Record allocator. The caller supplies the real scope and occupied values from the connected DB.</summary>
public sealed class RecordAllocator
{
    public int FindFirstFree(IEnumerable<int> occupied, int min = 1, int max = ushort.MaxValue)
    {
        var used = new HashSet<int>(occupied.Where(x => x >= min && x <= max));
        for (var value = min; value <= max; value++)
            if (!used.Contains(value)) return value;
        throw new InvalidOperationException($"No free Record exists in range {min}..{max}.");
    }

    public void Validate(int record, IEnumerable<int> occupied, int min = 1, int max = ushort.MaxValue)
    {
        if (record < min || record > max)
            throw new ArgumentOutOfRangeException(nameof(record), $"Record must be in range {min}..{max}.");
        if (occupied.Contains(record))
            throw new InvalidOperationException($"Record {record} is already occupied in this scope.");
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace Configurator.Core.Architecture;

/// <summary>Explicit logical rules stable across the analyzed project family.</summary>
public static class KnownProjectRelationRules
{
    public static void Add(DatabaseSchema schema)
    {
        AddActionsAndEvents(schema);
        AddReglerChannels(schema);
        AddStatusSensors(schema);
        AddProgramChildren(schema);
    }

    private static void AddActionsAndEvents(DatabaseSchema schema)
    {
        foreach (var tableName in new[] { "Actions", "Events" })
        {
            var source = Find(schema, tableName); var target = Find(schema, "Classes");
            if (source != null && target != null && Has(source, "Class") && Has(target, "Record"))
                Add(schema, source, target, new[] { "Class" }, new[] { "Record" }, DatabaseRelationKind.ClassLookup, RelationConfidence.High, "class-reference", "Class value resolves to Classes.Record.");
        }
    }

    private static void AddReglerChannels(DatabaseSchema schema)
    {
        var source = Find(schema, "Reglers");
        if (source == null || !Has(source, "PLC")) return;
        var ai = Find(schema, "AnaIN");
        if (ai != null && Has(source, "AI_Number") && Has(ai, "PLC") && Has(ai, "Record"))
            Add(schema, source, ai, new[] { "PLC", "AI_Number" }, new[] { "PLC", "Record" }, DatabaseRelationKind.ContextualProgramReference, RelationConfidence.High, "regler-ai", "Regler AI channel resolves by PLC + AnaIN.Record.");
        var ao = Find(schema, "AnaOUT");
        if (ao != null && Has(source, "AO_Number") && Has(ao, "PLC") && Has(ao, "Record"))
            Add(schema, source, ao, new[] { "PLC", "AO_Number" }, new[] { "PLC", "Record" }, DatabaseRelationKind.ContextualProgramReference, RelationConfidence.High, "regler-ao", "Regler AO channel resolves by PLC + AnaOUT.Record.");
    }

    private static void AddStatusSensors(DatabaseSchema schema)
    {
        var source = Find(schema, "Statuses"); var target = Find(schema, "DiscretIN");
        if (source == null || target == null || !Has(source, "PLC") || !Has(target, "PLC") || !Has(target, "Record")) return;
        foreach (var column in new[] { "LSL_Sensor", "LSH_Sensor" })
            if (Has(source, column))
                Add(schema, source, target, new[] { "PLC", column }, new[] { "PLC", "Record" }, DatabaseRelationKind.ContextualProgramReference, RelationConfidence.High, "status-" + column.ToLowerInvariant(), $"Statuses.{column} resolves to DiscretIN.Record within PLC context.");
    }

    private static void AddProgramChildren(DatabaseSchema schema)
    {
        if (Find(schema, "Prog") == null) return;
        AddChild(schema, "Prog_Recipe", "Recipe_Record", "Prog_Recipe", "Record", "program-recipe");
        AddChild(schema, "Prog_Recipes", "Recipe_Record", "Prog_Recipe", "Record", "program-recipe-values");
        AddChild(schema, "Prog_QueueSelections", "Queue_Record", "Prog_Queue", "Record", "program-queue-selection");
        AddChild(schema, "Prog_QueueSelections", "Status_Record", "Statuses", "Record", "program-status-selection");
        AddChild(schema, "Prog_QueueSelections", "Matrix_Record", "Matrix_List", "Record", "program-matrix-selection");
        AddChild(schema, "Prog_QueueSelections", "Seq_Record", "Prog_Seq", "Record", "program-sequence-selection");
    }

    private static void AddChild(DatabaseSchema schema, string sourceName, string sourceColumn, string targetName, string targetColumn, string rule)
    {
        var source = Find(schema, sourceName); var target = Find(schema, targetName);
        if (source == null || target == null || !Has(source, sourceColumn) || !Has(target, targetColumn)) return;
        Add(schema, source, target, new[] { sourceColumn }, new[] { targetColumn }, DatabaseRelationKind.ContextualProgramReference, RelationConfidence.Medium, rule, "Contextual program relation; full validation must include PLC/program/sequence context.");
    }

    private static DatabaseTable Find(DatabaseSchema schema, string name) => schema.Tables.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    private static bool Has(DatabaseTable table, string column) => table.Columns.Any(c => c.Name.Equals(column, StringComparison.OrdinalIgnoreCase));
    private static void Add(DatabaseSchema schema, DatabaseTable source, DatabaseTable target, IEnumerable<string> sourceColumns, IEnumerable<string> targetColumns, DatabaseRelationKind kind, RelationConfidence confidence, string rule, string comment)
    {
        schema.Relations.Add(new DatabaseRelation { SourceTable = source.FullName, TargetTable = target.FullName, Kind = kind, Confidence = confidence, RuleId = rule, Comment = comment }.WithColumns(sourceColumns, targetColumns));
    }
}

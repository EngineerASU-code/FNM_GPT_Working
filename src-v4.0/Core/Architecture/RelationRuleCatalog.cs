using System.Collections.Generic;

namespace Configurator.Core.Architecture;

/// <summary>Conservative rules used when SQL foreign keys are missing.</summary>
public static class RelationRuleCatalog
{
    public const string EntityKey = "entity-key-plc-class-record";
    public const string PlcRecord = "scalar-plc-record";
    public const string ClassRecord = "scalar-class-record";
    public const string AreaName = "scalar-area-name";
    public const string UnitId = "scalar-unit-id";
    public const string TypeLookup = "table-type-lookup";
    public const string PolymorphicClass = "polymorphic-class-dbname";

    public static readonly IReadOnlyList<string> RuleOrder = new[]
    {
        "Physical FK", EntityKey, PlcRecord, ClassRecord, AreaName, UnitId, TypeLookup, PolymorphicClass
    };
}

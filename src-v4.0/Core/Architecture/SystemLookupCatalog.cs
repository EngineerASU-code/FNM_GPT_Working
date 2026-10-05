using System;
using System.Collections.Generic;
using System.Linq;

namespace Configurator.Core.Architecture;

public static class SystemLookupCatalog
{
    // Mapping is deliberately kept explicit: the source table names are part of the
    // engineering model, while the actual existence of a table is checked against
    // the selected project database at runtime.
    private static readonly IReadOnlyList<LookupDefinition> Definitions = new[]
    {
        new LookupDefinition { ClassId = "valve",   ClassName = "Valve",   TargetTable = "dbo.Valves",    LookupTable = "dbo.ValveType" },
        new LookupDefinition { ClassId = "dsv",     ClassName = "DSV",     TargetTable = "dbo.Valve2S",   LookupTable = "dbo.Valve2SType" },
        new LookupDefinition { ClassId = "motor",   ClassName = "Motor",   TargetTable = "dbo.Motors",    LookupTable = "dbo.MotorType" },
        new LookupDefinition { ClassId = "di",      ClassName = "DI",      TargetTable = "dbo.DiscretIN", LookupTable = "dbo.DiscretInType" },
        new LookupDefinition { ClassId = "do",      ClassName = "DO",      TargetTable = "dbo.DiscretOUT",LookupTable = "dbo.DiscretOutType" },
        new LookupDefinition { ClassId = "ai",      ClassName = "AI",      TargetTable = "dbo.AnaIn",     LookupTable = "dbo.AnaInType" },
        new LookupDefinition { ClassId = "ao",      ClassName = "AO",      TargetTable = "dbo.AnaOUT",    LookupTable = "dbo.AnaOutType" },
        new LookupDefinition { ClassId = "pid",     ClassName = "PID",     TargetTable = "dbo.Reglers",   LookupTable = "dbo.ReglerType" },
        new LookupDefinition { ClassId = "status",  ClassName = "Status",  TargetTable = "dbo.Statuses",  LookupTable = "dbo.StatusType" },
        new LookupDefinition { ClassId = "program", ClassName = "Program", TargetTable = "dbo.Prog",      LookupTable = "dbo.ProTypes" },
        new LookupDefinition { ClassId = "step",    ClassName = "Step",    TargetTable = "dbo.Prog_Step",LookupTable = "dbo.Prog_StepType" },
        new LookupDefinition { ClassId = "matrix",  ClassName = "Matrix",  TargetTable = "dbo.Matrix",   LookupTable = "dbo.MatrixType" }
    };

    public static IReadOnlyList<LookupDefinition> All => Definitions;

    public static LookupDefinition Find(string tableName, string fieldName)
        => Definitions.FirstOrDefault(x =>
            x.TargetTable.Equals(tableName ?? "", StringComparison.OrdinalIgnoreCase) &&
            x.TargetField.Equals(fieldName ?? "", StringComparison.OrdinalIgnoreCase));

    public static LookupDefinition ForClass(string className)
        => Definitions.FirstOrDefault(x => x.ClassName.Equals(className ?? "", StringComparison.OrdinalIgnoreCase));
}

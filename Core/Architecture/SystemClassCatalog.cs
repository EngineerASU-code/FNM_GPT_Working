using System;
using System.Collections.Generic;

namespace Configurator.Core.Architecture;

public static class SystemClassCatalog
{
    public static List<ClassDefinition> CreateDefaults()
    {
        return new List<ClassDefinition>
        {
            CreateObject("valve", "Valve", "Equipment", "dbo.Valves", 1),
            CreateObject("dsv", "DSV", "Equipment", "dbo.Valve2S", 2),
            CreateObject("motor", "Motor", "Equipment", "dbo.Motors", 3),
            CreateObject("di", "DI", "Equipment", "dbo.DiscretIN", 4),
            CreateObject("do", "DO", "Equipment", "dbo.DiscretOUT", 5),
            CreateObject("ai", "AI", "Equipment", "dbo.AnaIn", 6),
            CreateObject("ao", "AO", "Equipment", "dbo.AnaOUT", 7),
            CreateObject("pid", "PID", "Equipment", "dbo.Reglers", 8),
            CreateObject("status", "Status", "System", "dbo.Statuses", 9),
            CreateProgram(),
            CreateStep(),
            CreateMatrix()
        };
    }

    private static ClassDefinition CreateObject(string id, string name, string category, string table, int number)
    {
        var definition = new ClassDefinition
        {
            Id = id,
            Name = name,
            Category = category,
            IsSystem = true,
            Available = true,
            ClassNumber = number,
            Description = $"Системный класс {name}",
            StorageMappings = new() { new StorageMapping { Role = "primary", TableName = table } }
        };
        definition.Fields.Add(new FieldDefinition { Name = "PLC", DataType = "auto", Description = "Основной контекст PLC", Group = "Object" });
        definition.Fields.Add(new FieldDefinition { Name = "PLC_Class_Number", DataType = "auto", Description = "Номер класса PLC", Group = "Object" });
        definition.Fields.Add(new FieldDefinition { Name = "Record", DataType = "auto", Description = "Идентификатор в пределах PLC", Group = "Object" });
        definition.Fields.Add(new FieldDefinition { Name = "Name", DataType = "string", Description = "Имя объекта", Group = "Object" });
        definition.Fields.Add(new FieldDefinition { Name = "Area", DataType = "auto", Description = "Контекст области", Group = "Object" });
        definition.Fields.Add(new FieldDefinition { Name = "DescriptionL1", DataType = "string", Description = "Описание L1", Group = "Object" });
        definition.Fields.Add(new FieldDefinition { Name = "DescriptionL2", DataType = "string", Description = "Описание L2", Group = "Object" });
        return definition;
    }

    private static ClassDefinition CreateProgram()
    {
        var value = CreateObject("program", "Program", "Process", "dbo.Prog", 10);
        value.Description = "Составной родительский класс программы.";
        value.TypeStorageTable = "dbo.ProTypes";
        value.StorageMappings.AddRange(new[]
        {
            new StorageMapping { Role = "procedure", TableName = "dbo.Prog_Seq" },
            new StorageMapping { Role = "steps", TableName = "dbo.Prog_Step" },
            new StorageMapping { Role = "transitions", TableName = "dbo.Prog_StepTransition" },
            new StorageMapping { Role = "stepParams", TableName = "dbo.Prog_StepParam" },
            new StorageMapping { Role = "recipe", TableName = "dbo.Prog_Recipe" },
            new StorageMapping { Role = "recipeValues", TableName = "dbo.Prog_Recipes" },
            new StorageMapping { Role = "softKeys", TableName = "dbo.Prog_SoftKey" },
            new StorageMapping { Role = "messages", TableName = "dbo.Prog_Msg" },
            new StorageMapping { Role = "queues", TableName = "dbo.Prog_Queue" },
            new StorageMapping { Role = "queueSelections", TableName = "dbo.Prog_QueueSelections" }
        });
        return value;
    }

    private static ClassDefinition CreateStep()
    {
        var value = CreateObject("step", "Step", "Process", "dbo.Prog_Step", 11);
        value.Description = "Родительский класс шага, используемый внутри программ.";
        value.StorageMappings.Add(new StorageMapping { Role = "transitions", TableName = "dbo.Prog_StepTransition" });
        value.StorageMappings.Add(new StorageMapping { Role = "parameters", TableName = "dbo.Prog_StepParam" });
        value.Fields.Add(new FieldDefinition { Name = "FC_Number", DataType = "auto", Description = "FC программы", Group = "Object" });
        return value;
    }

    private static ClassDefinition CreateMatrix()
    {
        return new ClassDefinition
        {
            Id = "matrix", Name = "Matrix", Category = "Process", IsSystem = true, Available = true, ClassNumber = 12,
            Description = "Матрица активаций проекта",
            StorageMappings = new() { new StorageMapping { Role = "primary", TableName = "dbo.Matrix" } }
        };
    }
}

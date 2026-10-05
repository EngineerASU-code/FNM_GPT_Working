using System.Collections.Generic;

namespace Configurator.Core.Architecture;

/// <summary>
/// v4.0: system classes are labels only. They are not storage templates.
/// The connected project database is the source of truth for tables and fields.
/// </summary>
public static class SystemClassCatalog
{
    public static List<ClassDefinition> CreateDefaults() => new()
    {
        Label("valve", "Valve", "Equipment", 1),
        Label("dsv", "DSV", "Equipment", 2),
        Label("motor", "Motor", "Equipment", 3),
        Label("di", "DI", "Equipment", 4),
        Label("do", "DO", "Equipment", 5),
        Label("ai", "AI", "Equipment", 6),
        Label("ao", "AO", "Equipment", 7),
        Label("pid", "PID", "Equipment", 8),
        Label("status", "Status", "System", 9),
        Label("program", "Program", "Process", 10),
        Label("step", "Step", "Process", 11),
        Label("matrix", "Matrix", "Process", 12)
    };

    private static ClassDefinition Label(string id, string name, string category, int number) => new()
    {
        Id = id,
        Name = name,
        Category = category,
        IsSystem = true,
        Available = true,
        ClassNumber = number,
        Description = $"Метка системного класса {name}. Структура определяется подключенной БД."
    };
}

namespace Configurator.Core.Architecture;

public sealed class LookupDefinition
{
    public string ClassId { get; init; } = "";
    public string ClassName { get; init; } = "";
    public string TargetTable { get; init; } = "";
    public string TargetField { get; init; } = "Type";
    public string LookupTable { get; init; } = "";
    public string LookupKeyField { get; init; } = "Type";
    public string DisplayName => string.IsNullOrWhiteSpace(ClassName)
        ? LookupTable
        : (string.IsNullOrWhiteSpace(LookupTable) ? ClassName : $"{ClassName} · {LookupTable}");
}

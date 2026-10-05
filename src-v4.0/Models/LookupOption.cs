namespace Configurator;

public sealed class LookupOption
{
    public string Key { get; init; } = "";
    public string Display { get; init; } = "";
    public override string ToString() => Display;
}

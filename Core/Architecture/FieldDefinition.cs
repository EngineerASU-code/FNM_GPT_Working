using System;

namespace Configurator.Core.Architecture;

public sealed class FieldDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string DataType { get; set; } = "string";
    public int Size { get; set; }
    public int Offset { get; set; }
    public int BitOffset { get; set; } = -1;
    public int BitLength { get; set; }
    public bool Nullable { get; set; } = true;
    public string DefaultValue { get; set; } = "";
    public string Description { get; set; } = "";
    public string Group { get; set; } = "Config";
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsInherited { get; set; }
}

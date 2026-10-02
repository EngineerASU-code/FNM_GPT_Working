using System.Collections.Generic;

namespace Configurator.Core.Architecture;

public sealed class StorageMapping
{
    public string Role { get; set; } = "primary";
    public string TableName { get; set; } = "";
    public List<string> KeyColumns { get; set; } = new();
}

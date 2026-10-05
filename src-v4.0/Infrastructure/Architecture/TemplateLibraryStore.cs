using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Configurator.Core.Architecture;

namespace Configurator.Infrastructure.Architecture;

public sealed class TemplateLibraryStore
{
    private readonly string _path;

    public TemplateLibraryStore(string path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Configurator", "class-library.json");
    }

    public List<ClassDefinition> Load()
    {
        try
        {
            if (!File.Exists(_path)) return new List<ClassDefinition>();
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<List<ClassDefinition>>(json) ?? new List<ClassDefinition>();
        }
        catch
        {
            return new List<ClassDefinition>();
        }
    }

    public void Save(IEnumerable<ClassDefinition> classes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(_path, JsonSerializer.Serialize(classes.ToList(), options));
    }
}

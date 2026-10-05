using System;
using System.Collections.Generic;
using System.Linq;
using Configurator.Core.Architecture;
using Configurator.Infrastructure.Architecture;

namespace Configurator.Application.Architecture;

public sealed class ClassArchitectureService
{
    private readonly TemplateLibraryStore _store;
    private readonly Dictionary<string, ClassDefinition> _classes = new(StringComparer.OrdinalIgnoreCase);

    public ClassArchitectureService(TemplateLibraryStore store = null)
    {
        _store = store ?? new TemplateLibraryStore();
        Reload();
    }

    public IReadOnlyList<ClassDefinition> Classes => _classes.Values.OrderBy(x => x.IsSystem ? 0 : 1).ThenBy(x => x.ClassNumber > 0 ? x.ClassNumber : int.MaxValue).ThenBy(x => x.Name).ToList();

    public void Reload()
    {
        _classes.Clear();
        foreach (var item in SystemClassCatalog.CreateDefaults()) _classes[item.Id] = item;
        foreach (var item in _store.Load())
        {
            if (string.IsNullOrWhiteSpace(item.Id)) continue;
            if (_classes.TryGetValue(item.Id, out var system))
                Merge(system, item);
            else
                _classes[item.Id] = item;
        }
    }

    public void Upsert(ClassDefinition definition)
    {
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        _classes[definition.Id] = definition;
    }

    public void DeleteUserClass(string id)
    {
        if (!_classes.TryGetValue(id, out var definition)) return;
        if (definition.IsSystem) throw new InvalidOperationException("Системный класс нельзя удалить.");
        _classes.Remove(id);
    }

    public void Save()
    {
        var custom = _classes.Values.ToList();
        _store.Save(custom);
    }

    public ClassDefinition Find(string id) => id != null && _classes.TryGetValue(id, out var value) ? value : null;

    public IReadOnlyList<FieldDefinition> GetEffectiveFields(ClassDefinition definition)
    {
        return GetEffectiveFieldsInternal(definition, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private IReadOnlyList<FieldDefinition> GetEffectiveFieldsInternal(ClassDefinition definition, HashSet<string> visited)
    {
        if (definition == null || !visited.Add(definition.Id)) return Array.Empty<FieldDefinition>();

        var result = new List<FieldDefinition>();
        if (!string.IsNullOrWhiteSpace(definition.BaseClassId))
        {
            var baseClass = Find(definition.BaseClassId);
            if (baseClass != null)
            {
                foreach (var inherited in GetEffectiveFieldsInternal(baseClass, visited))
                {
                    var clone = CloneField(inherited);
                    clone.IsInherited = true;
                    result.Add(clone);
                }
            }
        }

        foreach (var own in definition.Fields)
        {
            int index = result.FindIndex(x => x.Name.Equals(own.Name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) result[index] = own;
            else result.Add(own);
        }

        return result;
    }

    private static FieldDefinition CloneField(FieldDefinition source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        DataType = source.DataType,
        Size = source.Size,
        Offset = source.Offset,
        BitOffset = source.BitOffset,
        BitLength = source.BitLength,
        Nullable = source.Nullable,
        DefaultValue = source.DefaultValue,
        Description = source.Description,
        Group = source.Group,
        IsInherited = true
    };

    private static void Merge(ClassDefinition target, ClassDefinition source)
    {
        if (!string.IsNullOrWhiteSpace(source.Description)) target.Description = source.Description;
        if (!string.IsNullOrWhiteSpace(source.BaseClassId)) target.BaseClassId = source.BaseClassId;
        target.Available = source.Available;
        if (source.ClassNumber > 0) target.ClassNumber = source.ClassNumber;
        if (source.Fields.Count > 0) target.Fields = source.Fields;
        if (source.Types.Count > 0) target.Types = source.Types;
    }
}

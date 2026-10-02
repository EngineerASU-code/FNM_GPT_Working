using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Configurator.Core.Architecture;

namespace Configurator;

public partial class TemplateArchitectureView
{
private async Task LoadFieldsAsync(ClassDefinition selected)
    {
        CancelLoad();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;
        if (_reader == null || selected.PrimaryStorage == null)
        {
            RebuildGroups();
            return;
        }

        try
        {
            var fields = await _reader.LoadFieldsAsync(selected.PrimaryStorage.TableName, selected.Name, ct);
            if (ct.IsCancellationRequested || !ReferenceEquals(_selectedClass, selected))
                return;

            if (selected.IsSystem)
            {
                if (selected.Name.Equals("DSV", StringComparison.OrdinalIgnoreCase) && selected.PrimaryStorage.TableName.Equals("dbo.Valve2S", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var field in fields) field.Group = FieldGroupCatalog.Resolve(selected.Name, field.Name);
                    selected.Fields = fields;
                    TxtStatus.Text = $"DSV · dbo.Valve2S · SQL-столбцов: {fields.Count}";
                    RebuildGroups();
                    return;
                }
                // Для системных классов фактическая SQL-таблица является источником
                // истины по составу полей. Сохраняем только человекочитаемые
                // описания из каталога, но никогда не ограничиваем набор колонок
                // заранее заданными семью полями.
                var descriptions = selected.Fields
                    .Where(x => !string.IsNullOrWhiteSpace(x.Description))
                    .ToDictionary(x => x.Name, x => x.Description, StringComparer.OrdinalIgnoreCase);
                foreach (var field in fields)
                {
                    field.Group = FieldGroupCatalog.Resolve(selected.Name, field.Name);
                    if (descriptions.TryGetValue(field.Name, out var description))
                        field.Description = description;
                }
                selected.Fields = fields;
                TxtStatus.Text = $"{selected.Name} · SQL-полей: {fields.Count}";
            }
            else if (fields.Count > 0)
            {
                selected.Fields = MergeFields(selected.Fields, fields, selected.Name);
            }

            RebuildGroups();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            RebuildGroups();
            TxtStatus.Text = $"Структура SQL не загружена: {ex.Message}";
        }
    }

private static List<FieldDefinition> MergeFields(List<FieldDefinition> existing, List<FieldDefinition> sqlFields, string className)
    {
        var result = new List<FieldDefinition>();
        var sqlByName = sqlFields.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var old in existing)
        {
            if (sqlByName.TryGetValue(old.Name, out var actual))
            {
                actual.Group = FieldGroupCatalog.Resolve(className, actual.Name);
                result.Add(actual);
                sqlByName.Remove(old.Name);
            }
        }
        foreach (var extra in sqlFields.Where(x => sqlByName.ContainsKey(x.Name)))
            result.Add(extra);
        return result;
    }

private void RebuildGroups()
    {
        GroupHost.Children.Clear();
        if (_selectedClass == null) return;

        foreach (var groupName in FieldGroupCatalog.GetGroups(_selectedClass.Name))
        {
            // Для системных классов источником истины является фактическая SQL-таблица.
            // Не берём здесь устаревший снимок из ClassArchitectureService: после загрузки
            // dbo.Valve2S / dbo.Valves / dbo.Motors и т.д. selected.Fields уже содержит
            // полный актуальный набор колонок SQL.
            var effectiveFields = _selectedClass.IsSystem
                ? _selectedClass.Fields
                : _architecture.GetEffectiveFields(_selectedClass);
            var fields = effectiveFields
                .Where(x => string.Equals(
                    string.IsNullOrWhiteSpace(x.Group) ? FieldGroupCatalog.Resolve(_selectedClass.Name, x.Name) : x.Group,
                    groupName,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (fields.Count == 0) continue;

            var expander = new Expander
            {
                Header = groupName switch
                {
                    "Object" => "Object · Основные данные",
                    "Config" => "Config · Настройки и адреса",
                    "Visual" => "Visual · Отображение",
                    "Conditions" => "Conditions · Условия и переходы",
                    _ => groupName
                },
                IsExpanded = true,
                Margin = new Thickness(0, 0, 0, 7),
                Padding = new Thickness(10, 7, 10, 8)
            };
            expander.SetResourceReference(Control.BackgroundProperty, "BrushBase");
            expander.SetResourceReference(Control.ForegroundProperty, "BrushText");

            var stack = new StackPanel();
            stack.Children.Add(CreateFieldHeader());
            var list = new ListBox { ItemsSource = fields, ItemTemplate = (DataTemplate)FindResource("FieldRowTemplate"), BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, SelectionMode = SelectionMode.Single, Tag = groupName };
            list.SelectionChanged += FieldList_SelectionChanged;
            stack.Children.Add(list);
            expander.Content = stack;
            GroupHost.Children.Add(expander);
        }
    }

private static Grid CreateFieldHeader()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 3) };
        foreach (var width in new[] { 180.0, 115.0, 75.0, 75.0, 75.0, 80.0, double.NaN })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(width) ? new GridLength(1, GridUnitType.Star) : new GridLength(width) });
        string[] labels = { "Имя", "Тип", "Размер", "Смещение", "Бит", "Nullable", "Описание" };
        for (int i = 0; i < labels.Length; i++)
        {
            var text = new TextBlock { Text = labels[i], FontSize = 10, Margin = new Thickness(4, 0, 0, 0) };
            text.SetResourceReference(TextBlock.ForegroundProperty, "BrushTextSecondary");
            Grid.SetColumn(text, i);
            grid.Children.Add(text);
        }
        return grid;
    }

private static TemplateColumn ToTemplateColumn(FieldDefinition field)
    {
        string type = string.IsNullOrWhiteSpace(field.DataType) || field.DataType.Equals("auto", StringComparison.OrdinalIgnoreCase) ? "int" : field.DataType;
        bool text = type.Equals("string", StringComparison.OrdinalIgnoreCase);
        if (text) type = "nvarchar";
        return new TemplateColumn
        {
            Name = field.Name,
            DataType = type,
            MaxLength = field.Size > 0 ? field.Size : (text ? 500 : 0),
            Precision = 18,
            Scale = 2,
            IsNullable = field.Nullable,
            DefaultDefinition = field.DefaultValue == "DEFAULT" ? "0" : field.DefaultValue,
            IsExisting = false,
            IsPrimaryKey = field.Name.Equals("Record", StringComparison.OrdinalIgnoreCase)
        };
    }
}

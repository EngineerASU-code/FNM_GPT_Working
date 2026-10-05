using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Configurator;

public partial class ProjectModeView
{
    private async void RenderLookupObjectDetailsAsync()
    {
        if (_selectedRow == null || _selectedClass == null || _connection == null || string.IsNullOrWhiteSpace(_selectedDatabase)) return;

        ObjectEditor.Children.Clear();
        ObjectEditor.ColumnDefinitions.Clear();
        ObjectEditor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });
        ObjectEditor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });

        TxtObjectName.Text = PickName(_selectedRow, _selectedClass);
        string plc = GetText("PLC");
        string record = GetText("Record");
        TxtObjectMeta.Text = string.IsNullOrWhiteSpace(plc) && string.IsNullOrWhiteSpace(record)
            ? $"Класс: {_selectedClass.Name} · все поля объекта"
            : $"PLC {plc} · Record {record} · все поля объекта";
        TxtPreviewGlyph.Text = _selectedClass.Name.Length > 3 ? _selectedClass.Name[..3].ToUpperInvariant() : _selectedClass.Name.ToUpperInvariant();
        TxtPreviewClass.Text = _selectedClass.Name;

        var actualFields = _selectedRow.Table.Columns.Cast<DataColumn>()
            .Select(c => _selectedClass.Fields.FirstOrDefault(f => f.Name.Equals(c.ColumnName, StringComparison.OrdinalIgnoreCase))
                         ?? new Core.Architecture.FieldDefinition { Name = c.ColumnName, DataType = c.DataType.Name, Group = "Прочее" })
            .ToList();
        var grouped = actualFields
            .Select(f => new { Field = f, Group = ResolveDisplayGroupForObject(f) })
            .GroupBy(x => x.Group, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => ObjectGroupOrder(g.Key))
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var lookups = new LookupOptionsService(new DatabaseService(_connection.ToConnectionString(_selectedDatabase)));
        int column = 0;
        foreach (var group in grouped)
        {
            var card = new Border { Margin = new Thickness(column == 0 ? 0 : 6, 0, 0, 8), Padding = new Thickness(10), CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Stretch };
            card.SetResourceReference(Border.BackgroundProperty, "BrushBase");
            card.SetResourceReference(Border.BorderBrushProperty, "BrushBorder");
            card.BorderThickness = new Thickness(1);
            var stack = new StackPanel();
            var title = new TextBlock { Text = group.Key, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 7) };
            title.SetResourceReference(TextBlock.ForegroundProperty, "BrushText");
            stack.Children.Add(title);
            foreach (var item in group)
            {
                var field = item.Field;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star), MinWidth = 110 });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star), MinWidth = 150 });
                var label = new TextBlock { Text = field.Name, FontSize = 10, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = field.Name };
                label.SetResourceReference(TextBlock.ForegroundProperty, "BrushTextSecondary");
                Grid.SetColumn(label, 0);
                row.Children.Add(label);
                FrameworkElement editor;
                var options = await lookups.TryGetAsync(_selectedClass.Name, _selectedClass.PrimaryStorage?.TableName, field.Name);
                if (options.Count > 0)
                {
                    var combo = new ComboBox { Height = 30, Padding = new Thickness(6, 2, 6, 2), Tag = field.Name, ItemsSource = options, DisplayMemberPath = nameof(LookupOption.Display), SelectedValuePath = nameof(LookupOption.Key), SelectedValue = GetText(field.Name), ToolTip = $"{field.Name}: список связанных значений" };
                    combo.SetResourceReference(Control.BackgroundProperty, "BrushPanel");
                    combo.SetResourceReference(Control.ForegroundProperty, "BrushText");
                    combo.SetResourceReference(Control.BorderBrushProperty, "BrushBorder");
                    editor = combo;
                }
                else
                {
                    var box = new TextBox { Text = GetText(field.Name), Height = 30, Padding = new Thickness(7, 3, 7, 3), Tag = field.Name, TextWrapping = TextWrapping.NoWrap, HorizontalContentAlignment = HorizontalAlignment.Left, ToolTip = $"{field.Name}: {GetText(field.Name)}" };
                    box.SetResourceReference(Control.BackgroundProperty, "BrushPanel");
                    box.SetResourceReference(Control.ForegroundProperty, "BrushText");
                    box.SetResourceReference(Control.BorderBrushProperty, "BrushBorder");
                    editor = box;
                }
                Grid.SetColumn(editor, 1);
                row.Children.Add(editor);
                stack.Children.Add(row);
            }
            card.Child = stack;
            Grid.SetColumn(card, column);
            ObjectEditor.Children.Add(card);
            column = column == 0 ? 1 : 0;
        }
        TxtStatus.Text = $"{_selectedClass.Name} · показаны все {actualFields.Count} полей · связанные поля доступны списками";
    }

    private string ResolveDisplayGroupForObject(Core.Architecture.FieldDefinition field)
    {
        var group = Core.Architecture.FieldGroupCatalog.Resolve(_selectedClass.Name, field.Name);
        return string.IsNullOrWhiteSpace(group) ? "Прочее" : group;
    }

    private static int ObjectGroupOrder(string group) => group switch
    {
        "Object" => 0, "Visual" => 1, "Config" => 2, "Conditions" => 3, "Parameters" => 4, "Прочее" => 100, _ => 50
    };

    private async void SaveObjectWithLookupControlsAsync()
    {
        if (_selectedRow == null || _selectedClass?.PrimaryStorage == null) return;
        try
        {
            var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var control in FindEditorControls(ObjectEditor))
            {
                if (control.Tag is not string field || !_objects.Columns.Contains(field)) continue;
                string text = control switch { TextBox box => box.Text, ComboBox combo => Convert.ToString(combo.SelectedValue) ?? "", _ => null };
                values[field] = ConvertEditorValue(field, text);
            }
            var identity = BuildIdentity();
            var db = new DatabaseService(_connection.ToConnectionString(_selectedDatabase));
            await db.UpdateRowByValuesAsync(_selectedClass.PrimaryStorage.TableName, identity, values);
            foreach (var pair in values)
                if (_objects.Columns.Contains(pair.Key)) _selectedRow[pair.Key] = pair.Value ?? DBNull.Value;
            await RenderLookupObjectDetailsAsyncTask();
            TxtStatus.Text = "Объект сохранён · связанные поля проверены по справочникам";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Сохранение объекта", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RenderLookupObjectDetailsAsyncTask()
    {
        await Task.Yield();
        RenderLookupObjectDetailsAsync();
    }

    private object ConvertEditorValue(string field, string text)
    {
        if (!_objects.Columns.Contains(field)) return text ?? "";
        if (string.IsNullOrWhiteSpace(text)) return DBNull.Value;
        var type = Nullable.GetUnderlyingType(_objects.Columns[field].DataType) ?? _objects.Columns[field].DataType;
        try
        {
            if (type == typeof(string)) return text;
            if (type == typeof(int)) return int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
            if (type == typeof(long)) return long.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
            if (type == typeof(short)) return short.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
            if (type == typeof(byte)) return byte.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
            if (type == typeof(decimal)) return decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
            if (type == typeof(double)) return double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
            if (type == typeof(float)) return float.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
            if (type == typeof(bool)) return bool.Parse(text);
            if (type == typeof(Guid)) return Guid.Parse(text);
            return Convert.ChangeType(text, type, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch { throw new InvalidOperationException($"Поле «{field}» требует значение типа {type.Name}, но «{text}» нельзя преобразовать."); }
    }

    private static IEnumerable<FrameworkElement> FindEditorControls(DependencyObject root)
    {
        if (root == null) yield break;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is TextBox || child is ComboBox) yield return (FrameworkElement)child;
            foreach (var nested in FindEditorControls(child)) yield return nested;
        }
    }
}

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

        // One continuous editor column: every field remains visible and there are
        // no empty alternating group columns on wide screens.
        ObjectEditor.Children.Clear();
        ObjectEditor.ColumnDefinitions.Clear();
        ObjectEditor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 500 });

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
        foreach (var group in grouped)
        {
            var card = new Border
            {
                Margin = new Thickness(0, 0, 0, 9),
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(8),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            card.SetResourceReference(Border.BackgroundProperty, "BrushBase");
            card.SetResourceReference(Border.BorderBrushProperty, "BrushBorder");
            card.BorderThickness = new Thickness(1);

            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 360 });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 360 });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var title = new TextBlock { Text = group.Key, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
            title.SetResourceReference(TextBlock.ForegroundProperty, "BrushText");
            Grid.SetColumnSpan(title, 2);
            content.Children.Add(title);

            int index = 0;
            foreach (var item in group)
            {
                int row = 1 + index / 2;
                int col = index % 2;
                while (content.RowDefinitions.Count <= row) content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var field = item.Field;
                var rowPanel = new Grid { Margin = new Thickness(col == 0 ? 0 : 7, 0, col == 0 ? 7 : 0, 7) };
                rowPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.75, GridUnitType.Star), MinWidth = 110 });
                rowPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star), MinWidth = 180 });

                var label = new TextBlock
                {
                    Text = field.Name,
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = $"{field.Name} · тип: {field.DataType}"
                };
                label.SetResourceReference(TextBlock.ForegroundProperty, "BrushTextSecondary");
                Grid.SetColumn(label, 0);
                rowPanel.Children.Add(label);

                FrameworkElement editor;
                var options = await lookups.TryGetAsync(_selectedClass.Name, _selectedClass.PrimaryStorage?.TableName, field.Name);
                if (options.Count > 0)
                {
                    var combo = new ComboBox
                    {
                        Height = 30,
                        Padding = new Thickness(6, 2, 6, 2),
                        Tag = field.Name,
                        ItemsSource = options,
                        DisplayMemberPath = nameof(LookupOption.Display),
                        SelectedValuePath = nameof(LookupOption.Key),
                        SelectedValue = GetText(field.Name),
                        ToolTip = $"{field.Name}: связанный справочник ({options.Count} знач.)"
                    };
                    combo.SetResourceReference(Control.BackgroundProperty, "BrushPanel");
                    combo.SetResourceReference(Control.ForegroundProperty, "BrushText");
                    combo.SetResourceReference(Control.BorderBrushProperty, "BrushBorder");
                    editor = combo;
                }
                else
                {
                    var box = new TextBox
                    {
                        Text = GetText(field.Name),
                        Height = 30,
                        Padding = new Thickness(7, 3, 7, 3),
                        Tag = field.Name,
                        TextWrapping = TextWrapping.NoWrap,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        ToolTip = $"{field.Name} · тип: {field.DataType}"
                    };
                    box.SetResourceReference(Control.BackgroundProperty, "BrushPanel");
                    box.SetResourceReference(Control.ForegroundProperty, "BrushText");
                    box.SetResourceReference(Control.BorderBrushProperty, "BrushBorder");
                    editor = box;
                }
                Grid.SetColumn(editor, 1);
                rowPanel.Children.Add(editor);
                Grid.SetColumn(rowPanel, col);
                Grid.SetRow(rowPanel, row);
                content.Children.Add(rowPanel);
                index++;
            }

            card.Child = content;
            Grid.SetColumn(card, 0);
            ObjectEditor.Children.Add(card);
        }

        TxtStatus.Text = $"{_selectedClass.Name} · показаны все {actualFields.Count} полей · связанные поля — выпадающие списки";
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
        catch (Exception ex) { MessageBox.Show(ex.Message, "Сохранение объекта", MessageBoxButton.OK, MessageBoxImage.Error); }
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

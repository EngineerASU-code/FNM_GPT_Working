using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Configurator;

public partial class EditRowWindow : Window
{
    private readonly string _tableName;
    private readonly List<ColumnInfo> _columns;
    private readonly Dictionary<string, TextBox> _inputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ComboBox> _lookups = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<LookupOption>> _lookupOptions;
    private readonly Dictionary<string, string> _original;
    private readonly string _keyColumn;
    private readonly int? _forcedClassNumber;
    private readonly string _classNumberColumn;
    private readonly string _connectionString;
    public Dictionary<string, object> Values { get; private set; }
    public bool Confirmed { get; private set; }

    public EditRowWindow(string tableName, List<ColumnInfo> columns, Dictionary<string, string> currentValues, string keyColumn, string classNumberColumn = null, int? forcedClassNumber = null, Dictionary<string, List<LookupOption>> lookupOptions = null)
    {
        InitializeComponent();
        _tableName = tableName ?? ""; _columns = columns ?? new List<ColumnInfo>(); _original = currentValues ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); _keyColumn = keyColumn ?? ""; _classNumberColumn = classNumberColumn ?? ""; _forcedClassNumber = forcedClassNumber; _lookupOptions = lookupOptions ?? new Dictionary<string, List<LookupOption>>(StringComparer.OrdinalIgnoreCase);
        // The connection is recovered from the current application database context by the caller through the environment variable only when available.
        _connectionString = App.Current?.Properties["ActiveConnectionString"] as string ?? "";
        WindowTitle.Text = $"Изменение строки — {_tableName}";
        GenerateFields();
    }

    private void GenerateFields()
    {
        foreach (var group in _columns.Where(c => !c.IsReadOnly).GroupBy(GetFieldGroup).OrderBy(x => x.Key))
        {
            var border = new Border { Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(10), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) }; border.SetResourceReference(Border.BackgroundProperty, "BrushPanel"); border.SetResourceReference(Border.BorderBrushProperty, "BrushBorder");
            var root = new StackPanel(); var header = new TextBlock { Text = group.Key, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 7) }; header.SetResourceReference(TextBlock.ForegroundProperty, "BrushTextSecondary"); root.Children.Add(header); var grid = new UniformGrid { Columns = 3 };
            foreach (var col in group)
            {
                var panel = new StackPanel { Margin = new Thickness(0, 0, 12, 8) }; var label = new TextBlock { Text = col.Name + " (" + col.DataType + ")", FontSize = 10 }; label.SetResourceReference(TextBlock.ForegroundProperty, "BrushText"); panel.Children.Add(label);
                if (_lookupOptions.TryGetValue(col.Name, out var options) && options.Count > 0)
                {
                    var combo = new ComboBox { Height = 31, ItemsSource = options, DisplayMemberPath = nameof(LookupOption.Display), SelectedValuePath = nameof(LookupOption.Key), Tag = col.Name }; _lookups[col.Name] = combo; var current = ReadOriginal(col.Name); combo.SelectedValue = current; panel.Children.Add(combo);
                }
                else
                {
                    var box = new TextBox { Height = 31, Padding = new Thickness(7, 3, 7, 3), Tag = col.Name, Text = ReadOriginal(col.Name) }; box.SetResourceReference(TextBox.BackgroundProperty, "BrushBase"); box.SetResourceReference(TextBox.ForegroundProperty, "BrushText"); box.SetResourceReference(TextBox.BorderBrushProperty, "BrushBorder");
                    if (_forcedClassNumber.HasValue && col.Name.Equals(_classNumberColumn, StringComparison.OrdinalIgnoreCase)) { box.Text = _forcedClassNumber.Value.ToString(CultureInfo.InvariantCulture); box.IsReadOnly = true; }
                    _inputs[col.Name] = box; panel.Children.Add(box);
                }
                grid.Children.Add(panel);
            }
            root.Children.Add(grid); border.Child = root; FieldsPanel.Children.Add(border);
        }
        if (_columns.Any(c => c.Name.Equals("Record", StringComparison.OrdinalIgnoreCase))) TxtRecordHint.Text = "Record можно изменить только на свободное значение в текущем PLC/классе. После ввода Configurator повторно проверит занятость и границы архитектуры.";
    }

    private string ReadOriginal(string name) => _original.TryGetValue(name, out var value) ? value ?? "" : "";

    private async void BtnConfirm_Click(object sender, RoutedEventArgs e)
    {
        Values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase); var errors = new List<string>();
        foreach (var col in _columns.Where(c => !c.IsReadOnly))
        {
            string text = _lookups.TryGetValue(col.Name, out var combo) ? combo.SelectedValue?.ToString() ?? "" : _inputs.TryGetValue(col.Name, out var box) ? box.Text.Trim() : "";
            if (_forcedClassNumber.HasValue && col.Name.Equals(_classNumberColumn, StringComparison.OrdinalIgnoreCase)) { Values[col.Name] = _forcedClassNumber.Value; continue; }
            if (string.IsNullOrWhiteSpace(text)) { if (col.HasDefault) continue; if (col.IsNullable) { Values[col.Name] = DBNull.Value; continue; } if (!col.IsIdentity) errors.Add($"Поле «{col.Name}» обязательно."); continue; }
            try { Values[col.Name] = ConvertValue(text, col); } catch (Exception ex) { errors.Add($"{col.Name}: {ex.Message}"); }
        }
        if (errors.Count > 0) { MessageBox.Show(string.Join("\n", errors), "Ошибка ввода", MessageBoxButton.OK, MessageBoxImage.Warning); Values = null; return; }

        if (Values.TryGetValue("Record", out var recordObject) && int.TryParse(Convert.ToString(recordObject), out int newRecord) && _columns.Any(c => c.Name.Equals("Record", StringComparison.OrdinalIgnoreCase)) && !string.Equals(ReadOriginal("Record"), Convert.ToString(newRecord, CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase))
        {
            int? plc = Values.TryGetValue("PLC", out var plcObject) && int.TryParse(Convert.ToString(plcObject), out int p) ? p : null;
            int? classNumber = Values.TryGetValue("PLC_Class_Number", out var classObject) && int.TryParse(Convert.ToString(classObject), out int cn) ? cn : _forcedClassNumber;
            if (string.IsNullOrWhiteSpace(_connectionString))
            {
                MessageBox.Show("Невозможно проверить новый Record: отсутствует подключение к БД. Изменение остановлено.", "Record", MessageBoxButton.OK, MessageBoxImage.Warning); Values = null; return;
            }
            var allocator = new RecordAllocationService(_connectionString);
            var validation = await allocator.ValidateAsync(_tableName, newRecord, plc, classNumber);
            if (!validation.IsValid)
            {
                var free = await allocator.FindFirstFreeAsync(_tableName, plc, classNumber);
                MessageBox.Show($"Record {newRecord} нельзя сохранить.\n\n{validation.Message}\n\nСвободные варианты: {string.Join(", ", free.SuggestedRecords)}", "Изменение остановлено", MessageBoxButton.OK, MessageBoxImage.Warning); Values = null; return;
            }
        }
        Confirmed = true; Close();
    }

    private static string GetFieldGroup(ColumnInfo col) { string n = col.Name.ToLowerInvariant(); if (n is "plc" or "record" || n.Contains("class") || n == "area") return "Идентификация и контекст"; if (n.StartsWith("name") || n.StartsWith("description") || n.StartsWith("text")) return "Наименование и описание"; if (n.Contains("address") || n.Contains("number")) return "PLC / идентификаторы"; return "Параметры"; }
    private static object ConvertValue(string text, ColumnInfo col) => col.DataType.ToLowerInvariant() switch { "tinyint" => byte.Parse(text, CultureInfo.InvariantCulture), "smallint" => short.Parse(text, CultureInfo.InvariantCulture), "int" => int.Parse(text, CultureInfo.InvariantCulture), "bigint" => long.Parse(text, CultureInfo.InvariantCulture), "decimal" or "numeric" or "money" or "smallmoney" => decimal.Parse(text, CultureInfo.InvariantCulture), "float" => double.Parse(text, CultureInfo.InvariantCulture), "real" => float.Parse(text, CultureInfo.InvariantCulture), "bit" => text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase) || text.Equals("да", StringComparison.OrdinalIgnoreCase), "uniqueidentifier" => Guid.Parse(text), "date" or "datetime" or "datetime2" or "smalldatetime" => DateTime.Parse(text, CultureInfo.InvariantCulture), "datetimeoffset" => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture), "time" => TimeSpan.Parse(text, CultureInfo.InvariantCulture), _ => text };
    private void BtnCancel_Click(object sender, RoutedEventArgs e) { Confirmed = false; Close(); }
    private void Window_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Close(); else if (e.Key == Key.Enter) BtnConfirm_Click(sender, e); }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Configurator;

public partial class AddRowWindow : Window
{
    private readonly List<ColumnInfo> _columns;
    private readonly Dictionary<string, TextBox> _fieldInputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ComboBox> _lookupInputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<LookupOption>> _lookupOptions;
    private readonly string _recordColumn;
    private readonly string _tableName;
    private readonly string _connectionString;
    private readonly string _plcColumn;
    private readonly int? _forcedClassNumber;
    private readonly string _classNumberColumn;
    private CancellationTokenSource _recordCts;
    private int _recordGeneration;
    private bool _updatingRecord;
    private readonly int _initialRecord;

    public Dictionary<string, object> Values { get; private set; }
    public bool Confirmed { get; private set; }

    public AddRowWindow(string tableName, List<ColumnInfo> columns, int nextRecordValue, string recordColumn, string connectionString = null, string plcColumn = null, int? forcedClassNumber = null, string classNumberColumn = null, Dictionary<string, List<LookupOption>> lookupOptions = null)
    {
        InitializeComponent();
        _tableName = tableName ?? "";
        _columns = columns ?? new List<ColumnInfo>();
        _initialRecord = nextRecordValue;
        _recordColumn = recordColumn ?? "";
        _connectionString = connectionString;
        _plcColumn = string.IsNullOrWhiteSpace(plcColumn) ? _columns.FirstOrDefault(c => c.Name.Equals("PLC", StringComparison.OrdinalIgnoreCase))?.Name ?? "" : plcColumn;
        _forcedClassNumber = forcedClassNumber;
        _classNumberColumn = classNumberColumn ?? "";
        _lookupOptions = lookupOptions ?? new Dictionary<string, List<LookupOption>>(StringComparer.OrdinalIgnoreCase);
        WindowTitle.Text = $"Новая строка — {_tableName}";
        GenerateFields();
        Loaded += async (_, _) => await UpdateRecordAsync();
        Closed += (_, _) => _recordCts?.Cancel();
    }

    private void GenerateFields()
    {
        FieldsPanel.Children.Clear();
        foreach (var group in _columns.Where(c => !c.IsReadOnly).GroupBy(GetFieldGroup).OrderBy(x => x.Key))
        {
            var border = new Border { Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(10), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
            border.SetResourceReference(Border.BackgroundProperty, "BrushPanel"); border.SetResourceReference(Border.BorderBrushProperty, "BrushBorder");
            var root = new StackPanel();
            var header = new TextBlock { Text = group.Key, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 7) }; header.SetResourceReference(TextBlock.ForegroundProperty, "BrushTextSecondary"); root.Children.Add(header);
            var grid = new UniformGrid { Columns = 3 };
            foreach (var col in group)
            {
                var panel = new StackPanel { Margin = new Thickness(0, 0, 12, 8) };
                var label = new TextBlock { Text = GetLabel(col), FontSize = 10 }; label.SetResourceReference(TextBlock.ForegroundProperty, "BrushText"); panel.Children.Add(label);
                bool isRecord = col.Name.Equals(_recordColumn, StringComparison.OrdinalIgnoreCase);
                bool isClass = !string.IsNullOrWhiteSpace(_classNumberColumn) && col.Name.Equals(_classNumberColumn, StringComparison.OrdinalIgnoreCase);
                if (_lookupOptions.TryGetValue(col.Name, out var options) && options.Count > 0)
                {
                    var combo = new ComboBox { Height = 31, ItemsSource = options, DisplayMemberPath = nameof(LookupOption.Display), SelectedValuePath = nameof(LookupOption.Key), Tag = col.Name };
                    _lookupInputs[col.Name] = combo;
                    if (isClass && _forcedClassNumber.HasValue) combo.SelectedValue = _forcedClassNumber.Value.ToString(CultureInfo.InvariantCulture);
                    panel.Children.Add(combo);
                }
                else
                {
                    var box = new TextBox { Height = 31, Padding = new Thickness(7, 3, 7, 3), Tag = col.Name };
                    box.SetResourceReference(TextBox.BackgroundProperty, "BrushBase"); box.SetResourceReference(TextBox.ForegroundProperty, "BrushText"); box.SetResourceReference(TextBox.BorderBrushProperty, "BrushBorder");
                    if (isClass && _forcedClassNumber.HasValue) { box.Text = _forcedClassNumber.Value.ToString(CultureInfo.InvariantCulture); box.IsReadOnly = true; }
                    if (isRecord && _initialRecord > 0) box.Text = _initialRecord.ToString(CultureInfo.InvariantCulture);
                    if (!string.IsNullOrWhiteSpace(_plcColumn) && col.Name.Equals(_plcColumn, StringComparison.OrdinalIgnoreCase)) box.TextChanged += PlcTextBox_TextChanged;
                    _fieldInputs[col.Name] = box; panel.Children.Add(box);
                }
                grid.Children.Add(panel);
            }
            root.Children.Add(grid); border.Child = root; FieldsPanel.Children.Add(border);
        }
    }

    private async void PlcTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingRecord) return;
        await UpdateRecordAsync();
    }

    private async Task UpdateRecordAsync()
    {
        if (string.IsNullOrWhiteSpace(_connectionString) || string.IsNullOrWhiteSpace(_tableName) || string.IsNullOrWhiteSpace(_recordColumn) || !_fieldInputs.TryGetValue(_recordColumn, out var recordBox)) return;
        int generation = ++_recordGeneration;
        _recordCts?.Cancel(); _recordCts = new CancellationTokenSource(); var ct = _recordCts.Token;
        int? plc = null;
        if (!string.IsNullOrWhiteSpace(_plcColumn) && _fieldInputs.TryGetValue(_plcColumn, out var plcBox) && int.TryParse(plcBox.Text.Trim(), out int parsed)) plc = parsed;
        try
        {
            var allocator = new RecordAllocationService(_connectionString);
            var result = await allocator.FindFirstFreeAsync(_tableName, plc, _forcedClassNumber, null, ct);
            if (generation != _recordGeneration || ct.IsCancellationRequested) return;
            if (!result.FirstFreeRecord.HasValue)
            {
                TxtRecordHint.Text = $"Record: свободных значений нет. Допустимый диапазон 1..{result.MaxRecord}. Добавление будет заблокировано.";
                return;
            }
            _updatingRecord = true; recordBox.Text = result.FirstFreeRecord.Value.ToString(CultureInfo.InvariantCulture); _updatingRecord = false;
            TxtRecordHint.Text = $"Рекомендуемый первый свободный Record: {result.FirstFreeRecord}. Другие свободные: {string.Join(", ", result.SuggestedRecords.Skip(1))}. Диапазон архитектуры: 1..{result.MaxRecord}.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { TxtRecordHint.Text = "Не удалось автоматически подобрать Record: " + ex.Message; }
        finally { _updatingRecord = false; }
    }

    private async void BtnConfirm_Click(object sender, RoutedEventArgs e)
    {
        Values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        foreach (var col in _columns.Where(c => !c.IsReadOnly))
        {
            string text = _lookupInputs.TryGetValue(col.Name, out var combo) ? combo.SelectedValue?.ToString() ?? "" : _fieldInputs.TryGetValue(col.Name, out var box) ? box.Text.Trim() : "";
            if (_forcedClassNumber.HasValue && col.Name.Equals(_classNumberColumn, StringComparison.OrdinalIgnoreCase)) { Values[col.Name] = _forcedClassNumber.Value; continue; }
            if (string.IsNullOrWhiteSpace(text))
            {
                if (col.HasDefault) continue;
                if (col.IsNullable) { Values[col.Name] = DBNull.Value; continue; }
                if (!col.IsIdentity) errors.Add($"Поле «{col.Name}» обязательно.");
                continue;
            }
            try { Values[col.Name] = ConvertValue(text, col); } catch (Exception ex) { errors.Add($"{col.Name}: {ex.Message}"); }
        }

        if (errors.Count > 0) { MessageBox.Show(string.Join("\n", errors), "Ошибка ввода", MessageBoxButton.OK, MessageBoxImage.Warning); Values = null; return; }
        if (Values.TryGetValue(_recordColumn, out var recordValue) && int.TryParse(Convert.ToString(recordValue), out int record))
        {
            int? plc = Values.TryGetValue(_plcColumn, out var plcValue) && int.TryParse(Convert.ToString(plcValue), out int p) ? p : null;
            var validation = await new RecordAllocationService(_connectionString).ValidateAsync(_tableName, record, plc, _forcedClassNumber);
            if (!validation.IsValid)
            {
                var free = await new RecordAllocationService(_connectionString).FindFirstFreeAsync(_tableName, plc, _forcedClassNumber);
                MessageBox.Show($"Record {record} недопустим.\n\n{validation.Message}\n\nСвободные варианты: {string.Join(", ", free.SuggestedRecords)}", "Добавление остановлено", MessageBoxButton.OK, MessageBoxImage.Warning);
                Values = null; return;
            }
        }
        Confirmed = true; Close();
    }

    private static string GetLabel(ColumnInfo col) => col.Name + " (" + col.DataType + ")" + (col.IsNullable ? " — nullable" : "") + (col.HasDefault ? " — default" : "");
    private static string GetFieldGroup(ColumnInfo col)
    {
        string n = col.Name.ToLowerInvariant();
        if (n is "plc" or "record" || n.Contains("class") || n == "area") return "Идентификация и контекст";
        if (n.StartsWith("name") || n.StartsWith("description") || n.StartsWith("text")) return "Наименование и описание";
        if (n.Contains("address") || n.Contains("number")) return "PLC / идентификаторы";
        return "Параметры";
    }

    private static object ConvertValue(string text, ColumnInfo col)
    {
        return col.DataType.ToLowerInvariant() switch
        {
            "tinyint" => byte.Parse(text, CultureInfo.InvariantCulture), "smallint" => short.Parse(text, CultureInfo.InvariantCulture), "int" => int.Parse(text, CultureInfo.InvariantCulture), "bigint" => long.Parse(text, CultureInfo.InvariantCulture),
            "decimal" or "numeric" or "money" or "smallmoney" => decimal.Parse(text, CultureInfo.InvariantCulture), "float" => double.Parse(text, CultureInfo.InvariantCulture), "real" => float.Parse(text, CultureInfo.InvariantCulture),
            "bit" => text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase) || text.Equals("да", StringComparison.OrdinalIgnoreCase), "uniqueidentifier" => Guid.Parse(text),
            "date" or "datetime" or "datetime2" or "smalldatetime" => DateTime.Parse(text, CultureInfo.InvariantCulture), "datetimeoffset" => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture), "time" => TimeSpan.Parse(text, CultureInfo.InvariantCulture),
            _ => text
        };
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) { Confirmed = false; Close(); }
    private void Window_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Close(); else if (e.Key == Key.Enter) BtnConfirm_Click(sender, e); }
}

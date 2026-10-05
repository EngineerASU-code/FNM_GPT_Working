using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Configurator.Application.Architecture;
using Configurator.Core.Architecture;

namespace Configurator;

public partial class ProjectModeView : UserControl
{
    private ConnectionSettings _connection;
    private string _selectedDatabase = "";
    private ProjectDatabaseCatalog _catalog;
    private ClassDefinition _selectedClass;
    private DataTable _objects = new();
    private DataRow _selectedRow;
    private List<ColumnInfo> _columns = new();
    private CancellationTokenSource _loadCts;
    private bool _filterGuard;

    public ProjectModeView()
    {
        InitializeComponent();
        BtnDelete.IsEnabled = false; BtnDuplicate.IsEnabled = false; BtnSave.IsEnabled = false; BtnNewObject.IsEnabled = false;
    }

    public event EventHandler ProjectSelectionRequested;

    public void Configure(ConnectionSettings connection, IEnumerable<string> databases, string selectedDatabase)
    {
        _connection = connection;
        SetProjectDatabase(selectedDatabase);
    }

    public void SetProjectDatabase(string database)
    {
        _selectedDatabase = database ?? "";
        TxtProjectDatabase.Text = string.IsNullOrWhiteSpace(_selectedDatabase) ? "не выбран" : _selectedDatabase;
        _ = LoadCatalogAsync();
    }

    private void SelectProject_Click(object sender, RoutedEventArgs e) => ProjectSelectionRequested?.Invoke(this, EventArgs.Empty);
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadCatalogAsync();

    private async Task LoadCatalogAsync()
    {
        _loadCts?.Cancel(); _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;
        _catalog = null; _selectedClass = null; _objects = new DataTable(); _selectedRow = null;
        ClassList.ItemsSource = null; OtherElementList.ItemsSource = null; ObjectList.ItemsSource = null; CmbPlcFilter.ItemsSource = null;
        ClearObjectEditor();
        if (_connection == null || string.IsNullOrWhiteSpace(_selectedDatabase)) return;
        try
        {
            var service = new ProjectDatabaseCatalogService(_connection.ToConnectionString(_selectedDatabase));
            _catalog = await service.LoadAsync(ct);
            if (ct.IsCancellationRequested) return;
            ClassList.ItemsSource = _catalog.Classes.Where(x => x.Available).ToList();
            OtherElementList.ItemsSource = BuildOtherElements();
            TxtStatus.Text = $"Подключена БД «{_catalog.Schema.DatabaseName}» · классов: {_catalog.Classes.Count}";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { TxtStatus.Text = "Не удалось прочитать архитектуру БД"; MessageBox.Show(ex.Message, "Проект", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private List<OtherElementItem> BuildOtherElements()
    {
        var result = new List<OtherElementItem>();
        foreach (var pair in new[] { ("PLC", "PLC"), ("Area", "Areas"), ("Unit", "Units") })
        {
            var table = _catalog.Schema.Tables.FirstOrDefault(t => t.Name.Equals(pair.Item2, StringComparison.OrdinalIgnoreCase));
            if (table != null) result.Add(new OtherElementItem { Title = pair.Item1, TableName = table.FullName });
        }
        return result;
    }

    private async void ClassList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ClassList.SelectedItem is not ClassDefinition cls) return;
        _filterGuard = true; OtherElementList.SelectedIndex = -1; _filterGuard = false;
        await SelectEntityAsync(cls);
    }

    private async void OtherElementList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filterGuard || OtherElementList.SelectedItem is not OtherElementItem item || _catalog == null) return;
        _filterGuard = true; ClassList.SelectedIndex = -1; _filterGuard = false;
        var table = _catalog.Schema.FindTable(item.TableName);
        if (table == null) return;
        var cls = new ClassDefinition { Id = "element:" + item.TableName, Name = item.Title, ClassNumber = 0, IsSystem = false, Available = true };
        cls.StorageMappings.Add(new StorageMapping { Role = "primary", TableName = table.FullName, KeyColumns = table.Keys.FirstOrDefault(k => k.IsPrimary)?.Columns ?? new List<string>() });
        foreach (var column in table.Columns) cls.Fields.Add(new FieldDefinition { Name = column.Name, DataType = column.DataType, Nullable = column.IsNullable, Group = "Database" });
        await SelectEntityAsync(cls);
    }

    private async Task SelectEntityAsync(ClassDefinition cls)
    {
        _selectedClass = cls;
        TxtClassTitle.Text = cls.Name;
        BtnNewObject.IsEnabled = cls.PrimaryStorage != null && !string.IsNullOrWhiteSpace(_selectedDatabase);
        ClearObjectEditor();
        await LoadObjectsAsync();
    }

    private async Task LoadObjectsAsync()
    {
        CancelLoad(); _loadCts = new CancellationTokenSource(); var ct = _loadCts.Token;
        ObjectList.ItemsSource = null; _objects = new DataTable();
        if (_selectedClass?.PrimaryStorage == null || _connection == null) return;
        try
        {
            var db = new DatabaseService(_connection.ToConnectionString(_selectedDatabase));
            _columns = await db.GetTableColumnsAsync(_selectedClass.PrimaryStorage.TableName, ct);
            var order = _columns.Take(3).Select(x => x.Name).ToList();
            _objects = await db.GetTableDataPageAsync(_selectedClass.PrimaryStorage.TableName, 1, 5000, order, null, ct);
            RebuildPlcFilter(); ApplyObjectFilters();
            TxtObjectCount.Text = $"{_objects.Rows.Count} объектов";
            TxtStatus.Text = $"{_selectedClass.Name} · { _selectedClass.PrimaryStorage.TableName } · строк: {_objects.Rows.Count}";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { TxtStatus.Text = $"Не удалось загрузить {_selectedClass.Name}: {ex.Message}"; }
    }

    private void RebuildPlcFilter()
    {
        if (!_objects.Columns.Contains("PLC")) { CmbPlcFilter.ItemsSource = null; return; }
        var values = _objects.Rows.Cast<DataRow>().Select(r => r["PLC"] == DBNull.Value ? "" : Convert.ToString(r["PLC"]))
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        _filterGuard = true;
        CmbPlcFilter.ItemsSource = values.Select(x => new FilterItem { Title = x, Value = x }).ToList();
        CmbPlcFilter.SelectedIndex = -1;
        _filterGuard = false;
    }

    private void ObjectFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filterGuard) return;
        ApplyObjectFilters();
    }

    private void ApplyObjectFilters()
    {
        if (_objects == null) return;
        var selectedPlc = CmbPlcFilter.SelectedItems.Cast<FilterItem>().Select(x => x.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var list = new List<ObjectItem>();
        for (int i = 0; i < _objects.Rows.Count; i++)
        {
            var row = _objects.Rows[i];
            if (selectedPlc.Count > 0 && _objects.Columns.Contains("PLC"))
            {
                string plc = row["PLC"] == DBNull.Value ? "" : Convert.ToString(row["PLC"]);
                if (!selectedPlc.Contains(plc)) continue;
            }
            string name = PickName(row, _selectedClass);
            string meta = _objects.Columns.Contains("Record") ? $"PLC={GetRowText(row, "PLC")} · Record={GetRowText(row, "Record")}" : "";
            list.Add(new ObjectItem { Name = name, Meta = meta, RowIndex = i });
        }
        ObjectList.ItemsSource = list;
        TxtObjectCount.Text = $"{list.Count} объектов";
    }

    private void ObjectList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var item = ObjectList.SelectedItem as ObjectItem;
        if (item == null || item.RowIndex < 0 || item.RowIndex >= _objects.Rows.Count) { ClearObjectEditor(); return; }
        _selectedRow = _objects.Rows[item.RowIndex]; BuildObjectEditor();
        BtnDelete.IsEnabled = true; BtnDuplicate.IsEnabled = true; BtnSave.IsEnabled = true;
        TxtStatus.Text = $"Выбран: {item.Name}";
    }

    private void BuildObjectEditor()
    {
        ObjectEditor.Children.Clear(); ObjectEditor.ColumnDefinitions.Clear();
        ObjectEditor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 320 });
        if (_selectedRow == null) return;
        TxtObjectName.Text = PickName(_selectedRow, _selectedClass);
        TxtObjectMeta.Text = _objects.Columns.Contains("Record") ? $"PLC {GetRowText(_selectedRow, "PLC")} · Record {GetRowText(_selectedRow, "Record")}" : _selectedClass?.PrimaryStorage?.TableName ?? "";
        foreach (var field in _columns)
        {
            var panel = new StackPanel { Margin = new Thickness(4, 2, 4, 6) };
            var label = new TextBlock { Text = field.Name + " (" + field.DataType + ")", FontSize = 10 };
            label.SetResourceReference(TextBlock.ForegroundProperty, "BrushTextSecondary"); panel.Children.Add(label);
            var box = new TextBox { Text = GetRowText(_selectedRow, field.Name), Height = 31, Padding = new Thickness(7, 3, 7, 3), Tag = field.Name, ToolTip = field.Name };
            box.SetResourceReference(TextBox.BackgroundProperty, "BrushBase"); box.SetResourceReference(TextBox.ForegroundProperty, "BrushText"); box.SetResourceReference(TextBox.BorderBrushProperty, "BrushBorder");
            if (field.IsReadOnly || field.Name.Equals("Record", StringComparison.OrdinalIgnoreCase) && _selectedClass?.Name.Equals("PLC", StringComparison.OrdinalIgnoreCase) == false && false) box.IsReadOnly = field.IsReadOnly;
            panel.Children.Add(box); ObjectEditor.Children.Add(panel);
        }
    }

    private async void SaveObject_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRow == null || _selectedClass?.PrimaryStorage == null) return;
        try
        {
            var values = CollectValues();
            if (values.TryGetValue("Record", out var recordValue) && int.TryParse(Convert.ToString(recordValue), out int record))
            {
                int? plc = TryInt(values, "PLC", _selectedRow);
                int? classNumber = TryInt(values, "PLC_Class_Number", _selectedRow) ?? (_selectedClass.ClassNumber > 0 ? _selectedClass.ClassNumber : null);
                var identity = BuildIdentity();
                var validation = await new RecordAllocationService(_connection.ToConnectionString(_selectedDatabase)).ValidateAsync(_selectedClass.PrimaryStorage.TableName, record, plc, classNumber, identity);
                if (!validation.IsValid)
                {
                    var allocation = await new RecordAllocationService(_connection.ToConnectionString(_selectedDatabase)).FindFirstFreeAsync(_selectedClass.PrimaryStorage.TableName, plc, classNumber, Convert.ToInt32(_selectedRow["Record"]));
                    MessageBox.Show($"Record {record} нельзя сохранить.\n\n{validation.Message}\n\nСвободные варианты: {string.Join(", ", allocation.SuggestedRecords)}", "Недопустимый Record", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            var db = new DatabaseService(_connection.ToConnectionString(_selectedDatabase));
            await db.UpdateRowByValuesAsync(_selectedClass.PrimaryStorage.TableName, BuildIdentity(), values);
            foreach (var pair in values) if (_objects.Columns.Contains(pair.Key)) _selectedRow[pair.Key] = pair.Value ?? DBNull.Value;
            BuildObjectEditor(); TxtStatus.Text = "Объект сохранён";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Сохранение объекта", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void NewObject_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedClass?.PrimaryStorage == null || _connection == null) return;
        try
        {
            var db = new DatabaseService(_connection.ToConnectionString(_selectedDatabase));
            var columns = await db.GetTableColumnsAsync(_selectedClass.PrimaryStorage.TableName);
            var recordColumn = columns.FirstOrDefault(x => x.Name.Equals("Record", StringComparison.OrdinalIgnoreCase));
            int? plc = GetSingleSelectedPlc();
            int? classNumber = _selectedClass.ClassNumber > 0 ? _selectedClass.ClassNumber : null;
            RecordAllocationResult allocation = recordColumn == null ? new RecordAllocationResult(null, 0, Array.Empty<int>(), "Record отсутствует") : await new RecordAllocationService(_connection.ToConnectionString(_selectedDatabase)).FindFirstFreeAsync(_selectedClass.PrimaryStorage.TableName, plc, classNumber);
            if (recordColumn != null && !allocation.FirstFreeRecord.HasValue)
            {
                MessageBox.Show($"Свободных Record нет. Допустимый диапазон: 1..{allocation.MaxRecord}.", "Добавление объекта", MessageBoxButton.OK, MessageBoxImage.Warning); return;
            }
            string noAutoPlc = "__MANUAL_PLC__";
            var dialog = new AddRowWindow(_selectedClass.PrimaryStorage.TableName, columns, allocation.FirstFreeRecord ?? 0, recordColumn?.Name ?? "", _connection.ToConnectionString(_selectedDatabase), noAutoPlc, classNumber, "PLC_Class_Number"){ Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() != true || !dialog.Confirmed || dialog.Values == null) return;
            var values = dialog.Values;
            if (recordColumn != null && values.TryGetValue(recordColumn.Name, out var recordObject) && int.TryParse(Convert.ToString(recordObject), out int record))
            {
                int? chosenPlc = TryInt(values, "PLC", null);
                var validation = await new RecordAllocationService(_connection.ToConnectionString(_selectedDatabase)).ValidateAsync(_selectedClass.PrimaryStorage.TableName, record, chosenPlc, classNumber);
                if (!validation.IsValid)
                {
                    var fresh = await new RecordAllocationService(_connection.ToConnectionString(_selectedDatabase)).FindFirstFreeAsync(_selectedClass.PrimaryStorage.TableName, chosenPlc, classNumber);
                    MessageBox.Show($"Указан недопустимый Record {record}.\n{validation.Message}\n\nРекомендуемые свободные: {string.Join(", ", fresh.SuggestedRecords)}", "Добавление остановлено", MessageBoxButton.OK, MessageBoxImage.Warning); return;
                }
            }
            await db.InsertRowAsync(_selectedClass.PrimaryStorage.TableName, values);
            await LoadObjectsAsync();
            TxtStatus.Text = $"Объект добавлен. Рекомендуемый Record: {(allocation.FirstFreeRecord?.ToString() ?? "не используется")}. Свободные: {string.Join(", ", allocation.SuggestedRecords)}";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Добавление объекта", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void DuplicateObject_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRow == null || _selectedClass?.PrimaryStorage == null) return;
        try
        {
            var db = new DatabaseService(_connection.ToConnectionString(_selectedDatabase));
            var pk = await db.GetPrimaryKeyColumnsAsync(_selectedClass.PrimaryStorage.TableName);
            var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (DataColumn column in _objects.Columns)
            {
                var meta = _columns.FirstOrDefault(x => x.Name.Equals(column.ColumnName, StringComparison.OrdinalIgnoreCase));
                if (meta?.IsReadOnly == true) continue;
                if (pk.Contains(column.ColumnName, StringComparer.OrdinalIgnoreCase)) continue;
                values[column.ColumnName] = _selectedRow[column.ColumnName];
            }
            if (_objects.Columns.Contains("Record"))
            {
                int? plc = TryInt(values, "PLC", _selectedRow); int? classNumber = TryInt(values, "PLC_Class_Number", _selectedRow) ?? (_selectedClass.ClassNumber > 0 ? _selectedClass.ClassNumber : null);
                var allocation = await new RecordAllocationService(_connection.ToConnectionString(_selectedDatabase)).FindFirstFreeAsync(_selectedClass.PrimaryStorage.TableName, plc, classNumber);
                if (!allocation.FirstFreeRecord.HasValue) throw new InvalidOperationException("Нет свободного Record для дубликата.");
                values["Record"] = allocation.FirstFreeRecord.Value;
            }
            await db.InsertRowAsync(_selectedClass.PrimaryStorage.TableName, values);
            await LoadObjectsAsync();
            TxtStatus.Text = "Объект продублирован с первым свободным Record";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Дублирование", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void DeleteObject_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRow == null || _selectedClass?.PrimaryStorage == null) return;
        try
        {
            var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (DataColumn column in _objects.Columns) row[column.ColumnName] = _selectedRow[column.ColumnName];
            bool deleted = await new SafeDeletionWorkflowService(_connection.ToConnectionString(_selectedDatabase)).TryDeleteAsync(_selectedClass.PrimaryStorage.TableName, row, Window.GetWindow(this));
            if (!deleted) return;
            await LoadObjectsAsync(); ClearObjectEditor(); TxtStatus.Text = "Удаление завершено после проверки зависимостей";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Удаление", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private Dictionary<string, object> CollectValues()
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var box in FindTextBoxes(ObjectEditor))
        {
            if (box.Tag is not string name) continue;
            var column = _columns.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (column == null || column.IsReadOnly) continue;
            result[name] = string.IsNullOrWhiteSpace(box.Text) ? (column.IsNullable ? DBNull.Value : "") : ConvertForColumn(box.Text.Trim(), column);
        }
        return result;
    }

    private Dictionary<string, object> BuildIdentity()
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (_selectedRow == null || _selectedClass?.PrimaryStorage == null) return result;
        var pk = _catalog?.Schema.FindTable(_selectedClass.PrimaryStorage.TableName)?.Keys.FirstOrDefault(k => k.IsPrimary)?.Columns;
        if (pk != null) foreach (var key in pk) if (_objects.Columns.Contains(key)) result[key] = _selectedRow[key];
        if (result.Count > 0) return result;
        foreach (var key in new[] { "PLC", "PLC_Class_Number", "Record" }) if (_objects.Columns.Contains(key)) result[key] = _selectedRow[key];
        return result;
    }

    private void ClearObjectEditor()
    {
        _selectedRow = null; ObjectEditor.Children.Clear(); TxtObjectName.Text = "Выберите объект"; TxtObjectMeta.Text = ""; BtnDelete.IsEnabled = false; BtnDuplicate.IsEnabled = false; BtnSave.IsEnabled = false;
    }

    private void CancelLoad() { try { _loadCts?.Cancel(); } catch { } }
    private static IEnumerable<TextBox> FindTextBoxes(DependencyObject root)
    {
        if (root == null) yield break;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is TextBox box) yield return box;
            foreach (var nested in FindTextBoxes(child)) yield return nested;
        }
    }

    private static string PickName(DataRow row, ClassDefinition cls)
    {
        foreach (var name in new[] { "Name", "Name_L1", "NameL1", "Tag", "AreaName", "IP" }) if (row.Table.Columns.Contains(name) && row[name] != DBNull.Value && !string.IsNullOrWhiteSpace(Convert.ToString(row[name]))) return Convert.ToString(row[name]);
        string plc = GetRowText(row, "PLC"); string record = GetRowText(row, "Record");
        return string.IsNullOrWhiteSpace(plc) && string.IsNullOrWhiteSpace(record) ? cls?.Name ?? "Объект" : $"{cls?.Name ?? "Объект"} · PLC={plc} · Record={record}";
    }

    private static string GetRowText(DataRow row, string column) => row != null && row.Table.Columns.Contains(column) && row[column] != DBNull.Value ? Convert.ToString(row[column]) ?? "" : "";
    private int? GetSingleSelectedPlc() => CmbPlcFilter.SelectedItems.Count == 1 && CmbPlcFilter.SelectedItem is FilterItem item && int.TryParse(item.Value, out int value) ? value : null;
    private static int? TryInt(Dictionary<string, object> values, string name, DataRow fallback) { if (values.TryGetValue(name, out var value) && int.TryParse(Convert.ToString(value), out int result)) return result; if (fallback != null && fallback.Table.Columns.Contains(name) && int.TryParse(GetRowText(fallback, name), out result)) return result; return null; }
    private static object ConvertForColumn(string text, ColumnInfo column)
    {
        switch (column.DataType.ToLowerInvariant())
        {
            case "tinyint": return byte.Parse(text, CultureInfo.InvariantCulture);
            case "smallint": return short.Parse(text, CultureInfo.InvariantCulture);
            case "int": return int.Parse(text, CultureInfo.InvariantCulture);
            case "bigint": return long.Parse(text, CultureInfo.InvariantCulture);
            case "decimal": case "numeric": case "money": case "smallmoney": return decimal.Parse(text, CultureInfo.InvariantCulture);
            case "float": return double.Parse(text, CultureInfo.InvariantCulture);
            case "real": return float.Parse(text, CultureInfo.InvariantCulture);
            case "bit": return text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase) || text.Equals("да", StringComparison.OrdinalIgnoreCase);
            case "uniqueidentifier": return Guid.Parse(text);
            case "date": case "datetime": case "datetime2": case "smalldatetime": return DateTime.Parse(text, CultureInfo.InvariantCulture);
            default: return text;
        }
    }

    private sealed class FilterItem { public string Title { get; init; } = ""; public string Value { get; init; } = ""; }
    private sealed class OtherElementItem { public string Title { get; init; } = ""; public string TableName { get; init; } = ""; }
    private sealed class ObjectItem { public string Name { get; init; } = ""; public string Meta { get; init; } = ""; public int RowIndex { get; init; } }
}

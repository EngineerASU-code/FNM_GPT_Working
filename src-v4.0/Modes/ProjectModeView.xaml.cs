using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Configurator.Application.Architecture;
using Configurator.Core.Architecture;
using Configurator.Infrastructure.Database;

namespace Configurator;

public partial class ProjectModeView : UserControl
{
    private readonly ClassArchitectureService _architecture = new();
    private ConnectionSettings _connection;
    private string _selectedDatabase = "";
    private ClassDefinition _selectedClass;
    private DataTable _objects = new();
    private DataRow _selectedRow;
    private CancellationTokenSource _loadCts;
    private ClassRegistryService _classRegistry;
    private bool _filterSelectionGuard;

    public ProjectModeView()
    {
        InitializeComponent();
        ObjectList.ContextMenu = BuildObjectContextMenu();
        ObjectList.PreviewMouseRightButtonDown += ObjectList_PreviewMouseRightButtonDown;
        ObjectList.PreviewKeyDown += ObjectList_PreviewKeyDown;
        BtnDelete.IsEnabled = false;
        BtnDuplicate.IsEnabled = false;
        BtnSave.IsEnabled = false;
        InitializeEmptyFilterLists();
        RefreshClasses();
    }

    public void Configure(ConnectionSettings connection, IEnumerable<string> databases, string selectedDatabase)
    {
        _connection = connection;
        _selectedDatabase = selectedDatabase ?? string.Empty;
        TxtProjectDatabase.Text = string.IsNullOrWhiteSpace(_selectedDatabase) ? "не выбран" : _selectedDatabase;
        _ = ConfigureProjectAsync();
    }

    public void SetProjectDatabase(string database)
    {
        _selectedDatabase = database ?? string.Empty;
        TxtProjectDatabase.Text = string.IsNullOrWhiteSpace(_selectedDatabase) ? "не выбран" : _selectedDatabase;
        _ = ConfigureProjectAsync();
    }

    private void SelectProject_Click(object sender, RoutedEventArgs e)
        => ProjectSelectionRequested?.Invoke(this, EventArgs.Empty);

    public event EventHandler ProjectSelectionRequested;

    private async Task ConfigureProjectAsync()
    {
        await RefreshProjectAvailabilityAsync();
        await PopulateGlobalFilterListsAsync();
    }

    private void RefreshClasses()
    {
        var classes = _architecture.Classes
            .Where(x => x.Available && !x.Name.Equals("Program", StringComparison.OrdinalIgnoreCase) && !x.Name.Equals("Step", StringComparison.OrdinalIgnoreCase) && !x.Name.Equals("Matrix", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.ClassNumber > 0 ? x.ClassNumber : int.MaxValue)
            .ThenBy(x => x.Name)
            .ToList();
        ClassList.ItemsSource = classes;
    }

    private async Task RefreshProjectAvailabilityAsync()
    {
        if (_connection == null || string.IsNullOrWhiteSpace(_selectedDatabase))
        {
            RefreshClasses();
            return;
        }

        foreach (var cls in _architecture.Classes) cls.Available = true;
        try
        {
            _classRegistry = new ClassRegistryService(_connection.ToConnectionString(_selectedDatabase));
            var availability = await _classRegistry.GetAvailabilityAsync();
            foreach (var cls in _architecture.Classes)
                cls.Available = availability.TryGetValue(cls.ClassNumber, out bool available) ? available : true;
        }
        catch
        {
            // If a legacy DB has no registry yet, system defaults remain available.
        }

        RefreshClasses();
    }

    private async void ClassList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ClassList.SelectedItem is not ClassDefinition cls) return;
        _selectedClass = cls;
        await LoadActualClassFieldsAsync();
        TxtClassTitle.Text = cls.Name;
        BtnNewObject.IsEnabled = !string.IsNullOrWhiteSpace(cls.PrimaryStorage?.TableName) && !string.IsNullOrWhiteSpace(_selectedDatabase);
        ClearObjectEditor();
        await LoadObjectsAsync();
        RebuildObjectFilters();
    }

    private async Task LoadActualClassFieldsAsync()
    {
        if (_selectedClass?.PrimaryStorage == null || _connection == null || string.IsNullOrWhiteSpace(_selectedDatabase)) return;
        try
        {
            var reader = new Configurator.Infrastructure.Database.ClassArchitectureReader(_connection.ToConnectionString(_selectedDatabase));
            var actual = await reader.LoadFieldsAsync(_selectedClass.PrimaryStorage.TableName, _selectedClass.Name);
            if (actual.Count > 0)
                _selectedClass.Fields = actual;
        }
        catch { }
    }

    private async Task LoadObjectsAsync()
    {
        CancelLoad();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;
        _objects = new DataTable();
        ObjectList.ItemsSource = null;
        if (_selectedClass?.PrimaryStorage == null || string.IsNullOrWhiteSpace(_selectedDatabase)) return;

        try
        {
            var db = new DatabaseService(_connection.ToConnectionString(_selectedDatabase));
            var columns = await db.GetTableColumnsAsync(_selectedClass.PrimaryStorage.TableName, ct);
            if (columns.Count == 0) return;
            var order = columns.Take(3).Select(x => x.Name).ToList();
            _objects = await db.GetTableDataPageAsync(_selectedClass.PrimaryStorage.TableName, 1, 5000, order, null, ct);
            RebuildObjectFilters();
            ApplyObjectFilters();
            TxtStatus.Text = $"{_selectedClass.Name} · объектов: {_objects.Rows.Count}";
            if (TxtObjectCount != null) TxtObjectCount.Text = $"{_objects.Rows.Count} объектов";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Не удалось загрузить объекты: {ex.Message}";
        }
    }

    private static List<ObjectItem> BuildObjectNames(DataTable table, ClassDefinition cls)
    {
        var result = new List<ObjectItem>();
        for (int i = 0; i < table.Rows.Count; i++)
        {
            var row = table.Rows[i];
            result.Add(new ObjectItem { Name = PickName(row, cls), RowIndex = i });
        }
        return result;
    }

    private static string PickName(DataRow row, ClassDefinition cls)
    {
        foreach (var field in new[] { "Name", "Name_L1", "NameL1", "Tag" })
            if (row.Table.Columns.Contains(field) && row[field] != DBNull.Value && !string.IsNullOrWhiteSpace(Convert.ToString(row[field])))
                return Convert.ToString(row[field]);
        string plc = row.Table.Columns.Contains("PLC") ? Convert.ToString(row["PLC"]) : "";
        string record = row.Table.Columns.Contains("Record") ? Convert.ToString(row["Record"]) : "";
        return string.IsNullOrWhiteSpace(plc) && string.IsNullOrWhiteSpace(record) ? $"{cls.Name} · {row.GetHashCode()}" : $"{cls.Name} · PLC={plc} · Record={record}";
    }


    private void ObjectList_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Delete || ObjectList.SelectedItems.Count == 0) return;
        DeleteObject_Click(sender, new RoutedEventArgs());
        e.Handled = true;
    }

    private void ObjectList_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var container = ItemsControl.ContainerFromElement(ObjectList, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (container?.DataContext is ObjectItem item)
        {
            ObjectList.SelectedItems.Clear();
            ObjectList.SelectedItem = item;
        }
    }

    private ContextMenu BuildObjectContextMenu()
    {
        var menu = new ContextMenu();
        var edit = new MenuItem { Header = "Открыть объект" }; edit.Click += (s, e) => BuildObjectEditor();
        menu.Items.Add(edit);
        return menu;
    }

    private void ObjectList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = ObjectList.SelectedItems.Cast<ObjectItem>().ToList();
        if (selected.Count == 0)
        {
            ClearObjectEditor();
            return;
        }
        var item = selected[0];
        if (item.RowIndex < 0 || item.RowIndex >= _objects.Rows.Count) return;
        _selectedRow = _objects.Rows[item.RowIndex];
        BuildObjectEditor();
        BtnDelete.IsEnabled = true;
        BtnDuplicate.IsEnabled = selected.Count == 1;
        BtnSave.IsEnabled = selected.Count == 1;
        TxtStatus.Text = selected.Count > 1
            ? $"Выбрано объектов: {selected.Count} · редактор показывает первый"
            : $"Выбран объект: {item.Name}";
    }

    private void BuildObjectEditor()
    {
        ObjectEditor.Children.Clear();
        ObjectEditor.ColumnDefinitions.Clear();
        for (int i = 0; i < 2; i++) ObjectEditor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 250 });
        if (_selectedRow == null || _selectedClass == null) return;
        string objectName = PickName(_selectedRow, _selectedClass);
        TxtObjectName.Text = objectName;
        TxtPreviewGlyph.Text = _selectedClass.Name.Length > 3 ? _selectedClass.Name[..3].ToUpperInvariant() : _selectedClass.Name.ToUpperInvariant();
        TxtPreviewClass.Text = _selectedClass.Name;
        string plc = GetText("PLC");
        string record = GetText("Record");
        TxtObjectMeta.Text = $"PLC {plc} · Record {record}";

        foreach (var group in FieldGroupCatalog.GetGroups(_selectedClass.Name))
        {
            var fields = _selectedClass.Fields.Where(f => FieldGroupCatalog.Resolve(_selectedClass.Name, f.Name).Equals(group, StringComparison.OrdinalIgnoreCase)).ToList();
            if (fields.Count == 0) continue;
            var groupCard = new Border { Margin = new Thickness(4), Padding = new Thickness(10), CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 250 };
            groupCard.SetResourceReference(Border.BackgroundProperty, "BrushBase");
            groupCard.SetResourceReference(Border.BorderBrushProperty, "BrushBorder");
            groupCard.BorderThickness = new Thickness(1);
            var groupStack = new StackPanel();
            var header = new TextBlock { Text = group, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
            header.SetResourceReference(TextBlock.ForegroundProperty, "BrushText");
            groupStack.Children.Add(header);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            int col = 0;
            Grid current = null;
            foreach (var field in fields)
            {
                if (col == 0)
                {
                    current = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                    current.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    current.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                }
                var stack = new StackPanel { Margin = new Thickness(col == 0 ? 0 : 6, 0, 0, 0) };
                var label = new TextBlock { Text = field.Name, FontSize = 10, Margin = new Thickness(0, 0, 0, 2) };
                label.SetResourceReference(TextBlock.ForegroundProperty, "BrushTextSecondary");
                stack.Children.Add(label);
                var box = new TextBox { Text = GetText(field.Name), Height = 32, MinWidth = 120, Padding = new Thickness(7, 3, 7, 3), Tag = field.Name, TextWrapping = TextWrapping.NoWrap, HorizontalContentAlignment = HorizontalAlignment.Left, ToolTip = $"{field.Name}: {GetText(field.Name)}" };
                stack.Children.Add(box);
                Grid.SetColumn(stack, col);
                current.Children.Add(stack);
                col++;
                if (col == 2)
                {
                    Grid.SetRow(current, grid.RowDefinitions.Count - 1);
                    grid.Children.Add(current);
                    col = 0;
                }
            }
            if (col == 1 && current != null)
            {
                Grid.SetRow(current, grid.RowDefinitions.Count - 1);
                grid.Children.Add(current);
            }
            groupStack.Children.Add(grid);
            groupCard.Child = groupStack;
            Grid.SetColumn(groupCard, Math.Min(ObjectEditor.ColumnDefinitions.Count - 1, ObjectEditor.Children.Count));
            ObjectEditor.Children.Add(groupCard);
        }
    }

    private string GetText(string field) => _selectedRow != null && _selectedRow.Table.Columns.Contains(field) && _selectedRow[field] != DBNull.Value ? Convert.ToString(_selectedRow[field]) ?? "" : "";

    private async void SaveObject_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRow == null || _selectedClass?.PrimaryStorage == null) return;
        try
        {
            var values = CollectValues();
            var identity = BuildIdentity();
            var db = new DatabaseService(_connection.ToConnectionString(_selectedDatabase));
            await db.UpdateRowByValuesAsync(_selectedClass.PrimaryStorage.TableName, identity, values);
            foreach (var pair in values)
                if (_objects.Columns.Contains(pair.Key))
                    _selectedRow[pair.Key] = pair.Value ?? DBNull.Value;
            BuildObjectEditor();
            TxtStatus.Text = "Объект сохранён";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Сохранение объекта", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private Dictionary<string, object> CollectValues()
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var box in FindTextBoxes(ObjectEditor))
        {
            string name = box.Tag as string;
            if (string.IsNullOrWhiteSpace(name) || !_objects.Columns.Contains(name)) continue;
            result[name] = box.Text;
        }
        return result;
    }

    private Dictionary<string, object> BuildIdentity()
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "PLC", "Record", "Area" })
            if (_objects.Columns.Contains(name)) result[name] = _selectedRow[name];
        if (result.Count >= 2) return result;

        var primary = new DatabaseService(_connection.ToConnectionString(_selectedDatabase))
            .GetPrimaryKeyColumnsAsync(_selectedClass.PrimaryStorage.TableName).GetAwaiter().GetResult();
        if (primary.Count > 0)
        {
            foreach (var key in primary)
                if (_objects.Columns.Contains(key)) result[key] = _selectedRow[key];
            return result;
        }

        result.Clear();
        foreach (DataColumn column in _objects.Columns)
        {
            var name = column.ColumnName;
            if (!_selectedRow.Table.Columns.Contains(name)) continue;
            result[name] = _selectedRow[name];
        }
        return result;
    }

    private async void DuplicateObject_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRow == null || _selectedClass?.PrimaryStorage == null) return;
        try
        {
            var db = new DatabaseService(_connection.ToConnectionString(_selectedDatabase));
            var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var columns = await db.GetTableColumnsAsync(_selectedClass.PrimaryStorage.TableName);
            foreach (DataColumn c in _objects.Columns)
            {
                var meta = columns.FirstOrDefault(x => x.Name.Equals(c.ColumnName, StringComparison.OrdinalIgnoreCase));
                if (meta != null && meta.IsReadOnly) continue;
                if (c.ColumnName.Equals("Record", StringComparison.OrdinalIgnoreCase) && _objects.Columns.Contains("PLC")) continue;
                values[c.ColumnName] = _selectedRow[c.ColumnName];
            }
            if (_objects.Columns.Contains("Record"))
            {
                int plc = _objects.Columns.Contains("PLC") && _selectedRow["PLC"] != DBNull.Value ? Convert.ToInt32(_selectedRow["PLC"]) : 0;
                int max = 0;
                foreach (DataRow row in _objects.Rows)
                {
                    if (_objects.Columns.Contains("PLC") && row["PLC"] != DBNull.Value && Convert.ToInt32(row["PLC"]) != plc) continue;
                    if (row["Record"] == DBNull.Value) continue;
                    if (int.TryParse(Convert.ToString(row["Record"]), out int r)) max = Math.Max(max, r);
                }
                values["Record"] = max + 1;
            }
            await db.InsertRowAsync(_selectedClass.PrimaryStorage.TableName, values);
            await LoadObjectsAsync();
            TxtStatus.Text = "Объект продублирован";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Дублирование", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void DeleteObject_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedClass?.PrimaryStorage == null || ObjectList.SelectedItems.Count == 0) return;
        var selected = ObjectList.SelectedItems.Cast<ObjectItem>().ToList();
        var names = selected.Select(x => x.Name).ToList();
        var message = selected.Count == 1
            ? $"Удалить объект «{names[0]}»?"
            : $"Удалить выбранные объекты ({selected.Count} шт.)?";
        if (MessageBox.Show(message, "Удаление объекта", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            var db = new DatabaseService(_connection.ToConnectionString(_selectedDatabase));
            int affected = 0;
            foreach (var item in selected)
            {
                if (item.RowIndex < 0 || item.RowIndex >= _objects.Rows.Count) continue;
                _selectedRow = _objects.Rows[item.RowIndex];
                var identity = BuildIdentity();
                affected += await db.DeleteRowsByValuesAsync(_selectedClass.PrimaryStorage.TableName, new List<Dictionary<string, object>> { identity });
            }
            await LoadObjectsAsync();
            TxtStatus.Text = $"Удалено объектов: {selected.Count} · строк: {affected}";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Удаление", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void NewObject_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedClass?.PrimaryStorage == null) return;
        var dialog = new TextInputWindow("Новый объект", $"Имя нового { _selectedClass.Name }:", $"{_selectedClass.Name}_001") { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var db = new DatabaseService(_connection.ToConnectionString(_selectedDatabase));
            var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in _selectedClass.Fields)
            {
                if (_selectedClass.Name.Equals("Status", StringComparison.OrdinalIgnoreCase) && field.Name.Equals("Name", StringComparison.OrdinalIgnoreCase)) values[field.Name] = dialog.Result;
                else if (field.Name.Equals("Name", StringComparison.OrdinalIgnoreCase)) values[field.Name] = dialog.Result;
                else if (field.Name.Equals("Record", StringComparison.OrdinalIgnoreCase)) values[field.Name] = await NextRecordAsync(db);
                else if (field.Name.Equals("PLC_Class_Number", StringComparison.OrdinalIgnoreCase)) values[field.Name] = _selectedClass.ClassNumber;
                else values[field.Name] = DBNull.Value;
            }
            if (_objects.Columns.Contains("PLC")) values["PLC"] = 1;
            await db.InsertRowAsync(_selectedClass.PrimaryStorage.TableName, values);
            await LoadObjectsAsync();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Новый объект", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async Task<int> NextRecordAsync(DatabaseService db)
    {
        if (!_objects.Columns.Contains("Record")) return 1;
        int plc = _objects.Columns.Contains("PLC") ? 1 : 0;
        int max = _objects.Rows.Cast<DataRow>().Where(r => !_objects.Columns.Contains("PLC") || r["PLC"] == DBNull.Value || Convert.ToInt32(r["PLC"]) == plc).Select(r => r["Record"] == DBNull.Value ? 0 : Convert.ToInt32(r["Record"])).DefaultIfEmpty(0).Max();
        await Task.CompletedTask;
        return max + 1;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedDatabase) || _connection == null) return;
        if (!await _connection.TestConnectionAsync(_selectedDatabase))
        {
            MessageBox.Show($"Связь с БД «{_selectedDatabase}» потеряна. Обновление невозможно.", "Объекты проекта", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        await ConfigureProjectAsync();
        if (_selectedClass != null)
        {
            await LoadActualClassFieldsAsync();
            await LoadObjectsAsync();
        }
    }

    private IEnumerable<TextBox> FindTextBoxes(Panel root)
    {
        foreach (var child in root.Children)
        {
            if (child is TextBox box) yield return box;
            else if (child is Panel panel) foreach (var nested in FindTextBoxes(panel)) yield return nested;
        }
    }

    private void ClearObjectEditor()
    {
        _selectedRow = null;
        ObjectEditor.Children.Clear();
        TxtObjectName.Text = "Выберите объект";
        TxtObjectMeta.Text = "";
        TxtPreviewGlyph.Text = "OBJ";
        TxtPreviewClass.Text = "";
        BtnDelete.IsEnabled = false;
        BtnDuplicate.IsEnabled = false;
        BtnSave.IsEnabled = false;
    }

    private void CancelLoad()
    {
        _loadCts?.Cancel();
        _loadCts = null;
    }

    private sealed class ObjectItem
    {
        public string Name { get; set; } = "";
        public int RowIndex { get; set; }
    }
}

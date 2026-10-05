using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Configurator.Core.Architecture;

namespace Configurator;

public partial class LookupSettingsWindow : Window
{
    private readonly ConnectionSettings _connection;
    private readonly List<string> _databases;
    private readonly IReadOnlyList<LookupDefinition> _definitions = SystemLookupCatalog.All;
    private static readonly HashSet<string> HiddenLookupTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "dbo.MatrixDeviceListTypes",
        "dbo.ClassesType",
        "dbo.ProgStepParamHMITypes",
        "dbo.ProgStepParamTypes",
        "dbo.ProgTypes",
        "dbo.ProTypes"
    };
    private LookupDefinition _selectedDefinition;
    private DatabaseService _db;
    private List<ColumnInfo> _columns = new();
    private List<string> _keys = new();
    private DataTable _data;

    public LookupSettingsWindow(ConnectionSettings connection, IEnumerable<string> databases, string selectedDatabase)
    {
        InitializeComponent();
        _connection = connection;
        _databases = (databases ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        CmbDatabase.ItemsSource = _databases;
        if (_databases.Count > 0)
        {
            int index = _databases.FindIndex(x => x.Equals(selectedDatabase, StringComparison.OrdinalIgnoreCase));
            CmbDatabase.SelectedIndex = index >= 0 ? index : 0;
        }
        BuildLookupList();
        SetButtons(false);
    }

    private async void CmbDatabase_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbDatabase.SelectedItem is not string dbName) return;
        if (!await _connection.TestConnectionAsync(dbName))
        {
            MessageBox.Show($"Связь с БД «{dbName}» потеряна. Эта БД недоступна для получения значений.", "Настройки", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _db = new DatabaseService(_connection.ToConnectionString(dbName));
        await RefreshAvailableLookupsAsync();
    }

    private async Task RefreshAvailableLookupsAsync()
    {
        if (_db == null) return;

        var knownByTable = _definitions
            .Where(x => !string.IsNullOrWhiteSpace(x.LookupTable))
            .ToDictionary(x => x.LookupTable, StringComparer.OrdinalIgnoreCase);

        var available = new List<LookupDefinition>();
        try
        {
            // Текстовый список = любая реальная таблица dbo, оканчивающаяся на
            // Type/Types. Каталог системных классов только добавляет понятную
            // привязку к классу, но не ограничивает список настроек.
            var tables = await _db.GetTypeLookupTablesAsync();
            foreach (var table in tables)
            {
                if (HiddenLookupTables.Contains(table))
                    continue;

                if (knownByTable.TryGetValue(table, out var known))
                {
                    available.Add(known);
                    continue;
                }

                string shortName = table.StartsWith("dbo.", StringComparison.OrdinalIgnoreCase)
                    ? table[4..]
                    : table;
                string className = shortName.EndsWith("Types", StringComparison.OrdinalIgnoreCase)
                    ? shortName[..^5]
                    : shortName.EndsWith("Type", StringComparison.OrdinalIgnoreCase)
                        ? shortName[..^4]
                        : shortName;

                available.Add(new LookupDefinition
                {
                    ClassId = $"lookup:{table}",
                    ClassName = className,
                    TargetTable = "",
                    TargetField = "Type",
                    LookupTable = table,
                    LookupKeyField = "Type"
                });
            }
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Не удалось получить список Type/Types: {ex.Message}";
        }

        available = available
            .GroupBy(x => x.LookupTable, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.LookupTable, StringComparer.OrdinalIgnoreCase)
            .ToList();

        LookupList.ItemsSource = available;
        if (available.Count > 0) LookupList.SelectedIndex = 0;
        else
        {
            _selectedDefinition = null;
            TxtLookupTitle.Text = "Нет доступных списков";
            TxtLookupMeta.Text = "В выбранной БД не найдены таблицы dbo.*Type / dbo.*Types.";
            DataGrid.ItemsSource = null;
            SetButtons(false);
        }
    }

    private void BuildLookupList()
    {
        LookupList.DisplayMemberPath = nameof(LookupDefinition.DisplayName);
    }

    private async void LookupList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LookupList.SelectedItem is not LookupDefinition definition || _db == null) return;
        _selectedDefinition = definition;
        TxtLookupTitle.Text = $"{definition.ClassName} · {definition.TargetField}";
        TxtLookupMeta.Text = string.IsNullOrWhiteSpace(definition.TargetTable)
            ? $"Текстовый список: {definition.LookupTable}"
            : $"{definition.TargetTable} · {definition.TargetField}  ←  {definition.LookupTable}";
        await LoadSelectedLookupAsync();
    }

    private async Task LoadSelectedLookupAsync()
    {
        try
        {
            _columns = await _db.GetTableColumnsAsync(_selectedDefinition.LookupTable);
            _keys = await _db.GetPrimaryKeyColumnsAsync(_selectedDefinition.LookupTable);
            var order = _keys.Count > 0 ? _keys : _columns.Take(1).Select(x => x.Name).ToList();
            _data = await _db.GetTableDataPageAsync(_selectedDefinition.LookupTable, 1, 1000, order, null);
            DataGrid.ItemsSource = _data.DefaultView;
            SetButtons(true);
            TxtStatus.Text = $"Записей: {_data.Rows.Count}";
        }
        catch (Exception ex)
        {
            DataGrid.ItemsSource = null;
            SetButtons(false);
            TxtStatus.Text = ex.Message;
        }
    }

    private void SetButtons(bool enabled)
    {
        BtnAdd.IsEnabled = enabled;
        BtnEdit.IsEnabled = enabled && DataGrid.SelectedItem != null;
        BtnDelete.IsEnabled = enabled && DataGrid.SelectedItem != null && _keys.Count > 0;
    }

    private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => SetButtons(_selectedDefinition != null);

    private async void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDefinition == null || _db == null) return;
        string recordColumn = _columns.FirstOrDefault(c => c.Name.Equals(_selectedDefinition.LookupKeyField, StringComparison.OrdinalIgnoreCase))?.Name
            ?? _keys.FirstOrDefault() ?? _columns.FirstOrDefault()?.Name ?? "";
        var window = new AddRowWindow(_selectedDefinition.LookupTable, _columns, 0, recordColumn, _connection.ToConnectionString(CmbDatabase.SelectedItem?.ToString() ?? "")) { Owner = this };
        window.ShowDialog();
        if (!window.Confirmed || window.Values == null || window.Values.Count == 0) return;
        try
        {
            await _db.InsertRowAsync(_selectedDefinition.LookupTable, window.Values);
            await LoadSelectedLookupAsync();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Сохранение списка", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void BtnEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDefinition == null || _db == null || DataGrid.SelectedItem is not DataRowView row) return;
        var current = _columns.ToDictionary(c => c.Name, c => row.Row.Table.Columns.Contains(c.Name) && row[c.Name] != DBNull.Value ? Convert.ToString(row[c.Name]) : "", StringComparer.OrdinalIgnoreCase);
        string key = _keys.FirstOrDefault() ?? _columns.FirstOrDefault()?.Name ?? "";
        var window = new EditRowWindow(_selectedDefinition.LookupTable, _columns, current, key) { Owner = this };
        window.ShowDialog();
        if (!window.Confirmed || window.Values == null) return;
        var identity = _keys.ToDictionary(k => k, k => row[k], StringComparer.OrdinalIgnoreCase);
        try
        {
            await _db.UpdateRowByValuesAsync(_selectedDefinition.LookupTable, identity, window.Values);
            await LoadSelectedLookupAsync();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Сохранение списка", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDefinition == null || _db == null || DataGrid.SelectedItem is not DataRowView row || _keys.Count == 0) return;
        if (MessageBox.Show("Удалить выбранную запись справочника?", "Текстовый список", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var identity = _keys.ToDictionary(k => k, k => row[k], StringComparer.OrdinalIgnoreCase);
        try
        {
            await _db.DeleteRowsByValuesAsync(_selectedDefinition.LookupTable, new List<Dictionary<string, object>> { identity });
            await LoadSelectedLookupAsync();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Удаление", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (CmbDatabase.SelectedItem is not string dbName || string.IsNullOrWhiteSpace(dbName)) return;
        try
        {
            if (!await _connection.TestConnectionAsync(dbName))
            {
                MessageBox.Show($"Связь с БД «{dbName}» потеряна. Выберите активную БД.", "Настройки", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _db = new DatabaseService(_connection.ToConnectionString(dbName));
            await RefreshAvailableLookupsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Обновление списков", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

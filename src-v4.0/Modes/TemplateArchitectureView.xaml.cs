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

public partial class TemplateArchitectureView : UserControl
{
    private readonly ClassArchitectureService _architecture = new();
    private ClassArchitectureReader _reader;
    private ConnectionSettings _connection;
    private ClassDefinition _selectedClass;
    private string _selectedDatabase = "";
    private CancellationTokenSource _loadCts;
    private bool _availabilityGuard;
    private FieldDefinition _selectedField;
    private readonly DisabledObjectArchiveService _archive = new DisabledObjectArchiveService();
    private ClassRegistryService _classRegistry;
    private Dictionary<int, bool> _projectAvailability = new();

    public TemplateArchitectureView()
    {
        InitializeComponent();
        RefreshClassList();
    }

    public void Configure(ConnectionSettings connection, IEnumerable<string> databases, string selectedDatabase)
    {
        _selectedClass = null;
        _selectedField = null;
        _loadCts?.Cancel();
        _loadCts = null;
        _availabilityGuard = false;
        ClassList.SelectedIndex = -1;
        GroupHost.Children.Clear();
        TxtClassName.Text = "Выберите класс";
        TxtClassMeta.Text = "";
        TxtStorageInfo.Text = "";
        TxtClassNumber.Text = "";
        ChkAvailable.IsChecked = false;
        BtnRestoreArchived.Visibility = Visibility.Collapsed;
        _connection = connection;
        _selectedDatabase = selectedDatabase ?? "";
        TxtProjectDatabase.Text = string.IsNullOrWhiteSpace(_selectedDatabase) ? "не выбран" : _selectedDatabase;
        _reader = _connection != null && !string.IsNullOrWhiteSpace(_selectedDatabase)
            ? new ClassArchitectureReader(_connection.ToConnectionString(_selectedDatabase)) : null;
        ConfigureGraph();
        _ = RefreshProjectAvailabilityAsync();
    }

    public void SetProjectDatabase(string database)
    {
        _selectedDatabase = database ?? "";
        TxtProjectDatabase.Text = string.IsNullOrWhiteSpace(_selectedDatabase) ? "не выбран" : _selectedDatabase;
        _reader = _connection != null && !string.IsNullOrWhiteSpace(_selectedDatabase)
            ? new ClassArchitectureReader(_connection.ToConnectionString(_selectedDatabase)) : null;
        ConfigureGraph();
        _ = RefreshProjectAvailabilityAsync();
    }

    private void ConfigureGraph()
    {
        if (DatabaseGraphControl == null) return;
        DatabaseGraphControl.Configure(_connection, _selectedDatabase);
    }

    public event EventHandler ProjectSelectionRequested;
    private void SelectProject_Click(object sender, RoutedEventArgs e) => ProjectSelectionRequested?.Invoke(this, EventArgs.Empty);

    private void RefreshClassList()
    {
        ClassList.ItemsSource = null;
        ClassList.ItemsSource = _architecture.Classes;
    }

    private async void ClassList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ClassList.SelectedItem is not ClassDefinition selected) return;
        _selectedClass = selected;
        await RenderSelectedClassAsync(selected);
    }

    private async Task RenderSelectedClassAsync(ClassDefinition selected)
    {
        TxtClassName.Text = selected.Name;
        TxtClassNumber.Text = selected.ClassNumber > 0 ? selected.ClassNumber.ToString() : "";
        _availabilityGuard = true;
        ChkAvailable.IsChecked = IsAvailabilityRelevant(selected) ? GetProjectAvailability(selected) : true;
        _availabilityGuard = false;
        TxtClassNumber.IsReadOnly = selected.IsSystem;
        BtnDeleteClass.IsEnabled = !selected.IsSystem;
        BtnSaveClass.IsEnabled = true;
        ChkAvailable.IsEnabled = true;
        bool complex = selected.Name.Equals("Program", StringComparison.OrdinalIgnoreCase);
        ComplexNotice.Visibility = complex ? Visibility.Visible : Visibility.Collapsed;
        FieldsScroll.Visibility = complex ? Visibility.Collapsed : Visibility.Visible;
        BtnAddField.IsEnabled = !complex;
        BtnDeleteField.IsEnabled = !complex;
        BtnSyncSql.IsEnabled = !complex;
        var primary = selected.PrimaryStorage;
        TxtStorageInfo.Text = primary == null ? "Хранилище: не задано" : $"Хранилище: {primary.TableName}";
        TxtClassMeta.Text = selected.IsSystem ? "Системный класс" : "Пользовательский класс";
        BtnRestoreArchived.Visibility = IsAvailabilityRelevant(selected) && _connection != null && _archive.Exists(_connection.Server, _selectedDatabase, selected.Name) ? Visibility.Visible : Visibility.Collapsed;
        await LoadFieldsAsync(selected);
        RebuildGroups();
        TxtStatus.Text = $"Класс {selected.Name} · № {selected.ClassNumber}";
    }

    private async void Available_Checked(object sender, RoutedEventArgs e)
    {
        if (_availabilityGuard || _selectedClass == null || !IsAvailabilityRelevant(_selectedClass)) return;
        if (!EnsureAdmin())
        {
            _availabilityGuard = true; ChkAvailable.IsChecked = _selectedClass.Available; _availabilityGuard = false; return;
        }
        _selectedClass.Available = true;
        await SyncClassRegistryAsync(_selectedClass);
        _projectAvailability[_selectedClass.ClassNumber] = true;
        TxtStatus.Text = "Класс включён для использования в проекте";
        RefreshClassList();
    }

    private async void Available_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_availabilityGuard || _selectedClass == null || !IsAvailabilityRelevant(_selectedClass)) return;
        if (!EnsureAdmin())
        {
            _availabilityGuard = true; ChkAvailable.IsChecked = _selectedClass.Available; _availabilityGuard = false; return;
        }
        var classDef = _selectedClass;
        var table = classDef.PrimaryStorage?.TableName;
        var answer = MessageBox.Show($"Класс «{classDef.Name}» будет помечен как «Не использовать».\n\nОчистить таблицу {table ?? "(хранилище не задано)"} от объектов?\n\nДанные сначала будут сохранены в архив.", "Отключение класса", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            _availabilityGuard = true; ChkAvailable.IsChecked = true; _availabilityGuard = false; return;
        }
        try
        {
            if (_connection == null || string.IsNullOrWhiteSpace(_selectedDatabase) || _reader == null) throw new InvalidOperationException("Сначала выберите БД проекта.");
            if (string.IsNullOrWhiteSpace(table)) throw new InvalidOperationException("Для класса не задано SQL-хранилище.");
            var db = new DatabaseService(_connection.ToConnectionString(_selectedDatabase));
            var rows = await db.GetAllTableRowsAsync(table);
            await _archive.ArchiveAsync(_connection.Server, _selectedDatabase, classDef.Name, table, rows);
            int removed = await db.ClearTableAsync(table);
            classDef.Available = false;
            await SyncClassRegistryAsync(classDef);
            _projectAvailability[classDef.ClassNumber] = false;
            BtnRestoreArchived.Visibility = Visibility.Visible;
            TxtStatus.Text = $"Класс отключён · архивировано: {rows.Rows.Count}, очищено: {removed}";
            RefreshClassList();
        }
        catch (Exception ex)
        {
            _availabilityGuard = true; ChkAvailable.IsChecked = true; _availabilityGuard = false;
            MessageBox.Show($"Не удалось отключить класс: {ex.Message}", "Отключение класса", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RestoreArchived_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedClass?.PrimaryStorage == null || _connection == null || string.IsNullOrWhiteSpace(_selectedDatabase) || !EnsureAdmin()) return;
        try
        {
            var db = new DatabaseService(_connection.ToConnectionString(_selectedDatabase));
            int restored = await _archive.RestoreAsync(_connection.Server, _selectedDatabase, _selectedClass.Name, _selectedClass.PrimaryStorage.TableName, db);
            _selectedClass.Available = true;
            await SyncClassRegistryAsync(_selectedClass);
            _projectAvailability[_selectedClass.ClassNumber] = true;
            ChkAvailable.IsChecked = true;
            BtnRestoreArchived.Visibility = Visibility.Collapsed;
            TxtStatus.Text = $"Класс включён · восстановлено объектов: {restored}";
            RefreshClassList();
        }
        catch (Exception ex) { MessageBox.Show($"Не удалось восстановить объекты: {ex.Message}", "Восстановление", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private bool GetProjectAvailability(ClassDefinition definition) => definition != null && (_projectAvailability.TryGetValue(definition.ClassNumber, out bool value) ? value : definition.Available);

    private async Task RefreshProjectAvailabilityAsync()
    {
        _projectAvailability.Clear();
        if (_connection == null || string.IsNullOrWhiteSpace(_selectedDatabase)) { RefreshClassList(); return; }
        foreach (var cls in _architecture.Classes) cls.Available = true;
        try
        {
            _classRegistry = new ClassRegistryService(_connection.ToConnectionString(_selectedDatabase));
            _projectAvailability = await _classRegistry.GetAvailabilityAsync();
            foreach (var cls in _architecture.Classes) cls.Available = _projectAvailability.TryGetValue(cls.ClassNumber, out bool available) ? available : true;
        }
        catch { }
        RefreshClassList();
    }

    private static bool IsAvailabilityRelevant(ClassDefinition c) => !c.Name.Equals("Program", StringComparison.OrdinalIgnoreCase) && !c.Name.Equals("Status", StringComparison.OrdinalIgnoreCase) && !c.Name.Equals("Matrix", StringComparison.OrdinalIgnoreCase) && !c.Name.Equals("Step", StringComparison.OrdinalIgnoreCase);

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedDatabase) || _connection == null) return;
        if (!await _connection.TestConnectionAsync(_selectedDatabase))
        {
            MessageBox.Show($"Связь с БД «{_selectedDatabase}» потеряна. Обновление невозможно.", "Шаблоны проекта", MessageBoxButton.OK, MessageBoxImage.Warning); return;
        }
        await DatabaseGraphControl.RefreshAsync();
        if (_selectedClass != null) await RenderSelectedClassAsync(_selectedClass);
    }

    private void AddField_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureEditable()) return;
        _selectedClass.Fields.Add(new FieldDefinition { Name = "NewField", DataType = "string", Group = FieldGroupCatalog.Resolve(_selectedClass.Name, "NewField") });
        RebuildGroups();
    }

    private void FieldList_SelectionChanged(object sender, SelectionChangedEventArgs e) => _selectedField = (sender as ListBox)?.SelectedItem as FieldDefinition;

    private void DeleteField_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureEditable() || _selectedField == null || _selectedClass == null) return;
        if (_selectedField.IsInherited)
        {
            MessageBox.Show("Унаследованное поле нельзя удалить в дочернем классе. Измените его в родительском классе.", "Шаблоны проекта", MessageBoxButton.OK, MessageBoxImage.Information); return;
        }
        _selectedClass.Fields.Remove(_selectedField); _selectedField = null; RebuildGroups();
    }

    private async void LoadSqlStructure_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedClass == null || _reader == null || _connection == null || string.IsNullOrWhiteSpace(_selectedDatabase) || !EnsureEditable()) return;
        string table = _selectedClass.PrimaryStorage?.TableName;
        if (string.IsNullOrWhiteSpace(table)) { MessageBox.Show("Для класса не задано SQL-хранилище.", "Синхронизация с SQL", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        try
        {
            var db = new DatabaseService(_connection.ToConnectionString(_selectedDatabase));
            if (await db.TableExistsAsync(table)) { await LoadFieldsAsync(_selectedClass); TxtStatus.Text = $"Структура {table} синхронизирована с SQL"; return; }
            var columns = _architecture.GetEffectiveFields(_selectedClass).Select(ToTemplateColumn).ToList();
            await db.CreateTemplateTableAsync(table, columns);
            await LoadFieldsAsync(_selectedClass);
            TxtStatus.Text = $"SQL-таблица {table} создана из структуры класса";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Синхронизация с SQL", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async void SaveClass_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedClass == null || !EnsureEditable()) return;
        if (!_selectedClass.IsSystem)
        {
            if (!int.TryParse(TxtClassNumber.Text.Trim(), out int number) || number <= 0) { MessageBox.Show("Номер класса должен быть положительным целым числом.", "Шаблоны проекта", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (_architecture.Classes.Any(x => x.Id != _selectedClass.Id && x.ClassNumber == number)) { MessageBox.Show($"Номер класса {number} уже используется.", "Шаблоны проекта", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            _selectedClass.ClassNumber = number;
        }
        else TxtClassNumber.Text = _selectedClass.ClassNumber.ToString();
        _architecture.Upsert(_selectedClass); _architecture.Save();
        await SyncClassRegistryAsync(_selectedClass);
        RefreshClassList();
        TxtStatus.Text = "Класс сохранён";
    }

    private async Task SyncClassRegistryAsync(ClassDefinition definition)
    {
        if (_connection == null || string.IsNullOrWhiteSpace(_selectedDatabase) || definition == null) return;
        _classRegistry ??= new ClassRegistryService(_connection.ToConnectionString(_selectedDatabase));
        await _classRegistry.UpsertAsync(definition);
    }

    private async void DeleteClass_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedClass == null || _selectedClass.IsSystem) return;
        if (!EnsureAdmin()) return;
        if (MessageBox.Show($"Удалить класс «{_selectedClass.Name}»? SQL-таблица не удаляется.", "Удаление класса", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var deletedClass = _selectedClass;
        _architecture.DeleteUserClass(deletedClass.Id); _architecture.Save();
        try { await SyncClassRegistryRemoveAsync(deletedClass); }
        catch (Exception ex) { MessageBox.Show($"Класс удалён из редактора, но не удалён из dbo.Classes: {ex.Message}", "Шаблоны проекта", MessageBoxButton.OK, MessageBoxImage.Warning); }
        _selectedClass = null; RefreshClassList(); ClearEditor();
    }

    private async void AddClass_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureAdmin()) return;
        var dialog = new NewClassWindow(_architecture.Classes) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true || dialog.Result == null) return;
        try
        {
            if (_connection != null && !string.IsNullOrWhiteSpace(_selectedDatabase))
            {
                _classRegistry ??= new ClassRegistryService(_connection.ToConnectionString(_selectedDatabase));
                int dbNext = await _classRegistry.GetNextClassNumberAsync();
                if (dialog.Result.ClassNumber < dbNext) dialog.Result.ClassNumber = dbNext;
                await _classRegistry.UpsertAsync(dialog.Result);
            }
            _architecture.Upsert(dialog.Result); _architecture.Save(); RefreshClassList(); ClassList.SelectedItem = _architecture.Find(dialog.Result.Id);
            TxtStatus.Text = $"Создан класс {dialog.Result.Name} · № {dialog.Result.ClassNumber}";
        }
        catch (Exception ex) { MessageBox.Show($"Не удалось зарегистрировать класс в dbo.Classes: {ex.Message}", "Новый класс", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async Task SyncClassRegistryRemoveAsync(ClassDefinition definition)
    {
        if (_connection == null || string.IsNullOrWhiteSpace(_selectedDatabase) || definition == null) return;
        _classRegistry ??= new ClassRegistryService(_connection.ToConnectionString(_selectedDatabase));
        await _classRegistry.RemoveAsync(definition);
    }

    private bool EnsureAdmin() => new AdminPasswordWindow { Owner = Window.GetWindow(this) }.ShowDialog() == true;
    private bool EnsureEditable() => _selectedClass != null && (!_selectedClass.IsSystem || EnsureAdmin());

    private void ClearEditor()
    {
        GroupHost.Children.Clear(); ComplexNotice.Visibility = Visibility.Collapsed; FieldsScroll.Visibility = Visibility.Visible;
        TxtClassName.Text = "Выберите класс"; TxtClassMeta.Text = ""; TxtClassNumber.Text = ""; TxtStorageInfo.Text = "";
    }

    private void CancelLoad() { _loadCts?.Cancel(); _loadCts = null; }
}

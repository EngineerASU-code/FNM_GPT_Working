using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Configurator.Application.Architecture;
using Configurator.Core.Architecture;

namespace Configurator;

public partial class TemplateArchitectureView : UserControl
{
    private ConnectionSettings _connection;
    private string _selectedDatabase = "";
    private ProjectDatabaseCatalog _catalog;
    private ClassDefinition _selectedClass;
    private CancellationTokenSource _loadCts;

    public TemplateArchitectureView() => InitializeComponent();
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
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;
        _catalog = null; _selectedClass = null;
        ClassList.ItemsSource = null; FieldGrid.ItemsSource = null; RelatedTableList.ItemsSource = null;
        TxtClassName.Text = "Структура не загружена"; TxtClassMeta.Text = ""; TxtStorageInfo.Text = ""; TxtStatus.Text = ""; TxtFooter.Text = "";
        if (_connection == null || string.IsNullOrWhiteSpace(_selectedDatabase)) return;
        try
        {
            var service = new ProjectDatabaseCatalogService(_connection.ToConnectionString(_selectedDatabase));
            _catalog = await service.LoadAsync(ct);
            if (ct.IsCancellationRequested) return;
            ClassList.ItemsSource = _catalog.Classes;
            TxtFooter.Text = $"Таблиц: {_catalog.Schema.Tables.Count} · связей: {_catalog.Schema.Relations.Count} · классов: {_catalog.Classes.Count}";
            TxtStatus.Text = "Структура прочитана из подключенной БД";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { TxtStatus.Text = "Не удалось прочитать структуру БД"; MessageBox.Show(ex.Message, "Структура БД", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void ClassList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedClass = ClassList.SelectedItem as ClassDefinition;
        if (_selectedClass == null || _catalog == null) return;
        TxtClassName.Text = _selectedClass.Name;
        TxtClassMeta.Text = $"Класс № {_selectedClass.ClassNumber} · источник: фактическая БД";
        TxtStorageInfo.Text = _selectedClass.PrimaryStorage == null
            ? "Физическая таблица для этой метки в данной БД не найдена."
            : $"Фактическое хранилище: {_selectedClass.PrimaryStorage.TableName} · колонок: {_selectedClass.Fields.Count}";
        FieldGrid.ItemsSource = _selectedClass.Fields;

        var related = new List<RelatedTableItem>();
        if (_selectedClass.PrimaryStorage != null)
        {
            var table = _catalog.Schema.FindTable(_selectedClass.PrimaryStorage.TableName);
            if (table != null)
                foreach (var relation in _catalog.Schema.Relations.Where(r => r.SourceTable.Equals(table.FullName, StringComparison.OrdinalIgnoreCase) || r.TargetTable.Equals(table.FullName, StringComparison.OrdinalIgnoreCase)))
                {
                    string other = relation.SourceTable.Equals(table.FullName, StringComparison.OrdinalIgnoreCase) ? relation.TargetTable : relation.SourceTable;
                    related.Add(new RelatedTableItem { TableName = other, Description = $"{relation.Kind}; {string.Join(", ", relation.SourceColumns)} → {string.Join(", ", relation.TargetColumns)}" });
                }
        }

        if (IsComplex(_selectedClass.Name))
            foreach (var table in _catalog.Schema.Tables.Where(t => t.Name.StartsWith("Prog", StringComparison.OrdinalIgnoreCase) || t.Name.StartsWith("Matrix", StringComparison.OrdinalIgnoreCase)))
                if (!related.Any(x => x.TableName.Equals(table.FullName, StringComparison.OrdinalIgnoreCase)))
                    related.Add(new RelatedTableItem { TableName = table.FullName, Description = "Таблица сложной сущности, обнаруженная в текущей БД." });

        RelatedTableList.ItemsSource = related.GroupBy(x => x.TableName, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).OrderBy(x => x.TableName).ToList();
    }

    private static bool IsComplex(string name) => name.Equals("Program", StringComparison.OrdinalIgnoreCase) || name.Equals("Step", StringComparison.OrdinalIgnoreCase) || name.Equals("Matrix", StringComparison.OrdinalIgnoreCase);

    private sealed class RelatedTableItem
    {
        public string TableName { get; init; } = "";
        public string Description { get; init; } = "";
    }
}

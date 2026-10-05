using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Configurator.Core.Architecture;

namespace Configurator;

public partial class DuplicateCheckWindow : Window
{
    private readonly ConnectionSettings _connection;
    private readonly List<string> _databases;
    private readonly bool _addresses;
    private List<DuplicateClassOption> _classes = new();
    private bool _loadingPlc;
    private List<DuplicateAddressGroup> _addressResults = new();
    private List<DuplicateRecordGroup> _recordResults = new();

    public DuplicateCheckWindow(ConnectionSettings connection, IEnumerable<string> databases, string selectedDatabase, bool addresses)
    {
        InitializeComponent();
        _connection = connection;
        _addresses = addresses;
        _databases = databases?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new();
        TxtTitle.Text = addresses ? "Проверить дублирование адресов" : "Проверить дублирование Recordов";
        CmbDatabase.ItemsSource = _databases;
        if (!string.IsNullOrWhiteSpace(selectedDatabase) && _databases.Any(x => x.Equals(selectedDatabase, StringComparison.OrdinalIgnoreCase))) CmbDatabase.SelectedItem = _databases.First(x => x.Equals(selectedDatabase, StringComparison.OrdinalIgnoreCase));
        else if (_databases.Count > 0) CmbDatabase.SelectedIndex = 0;
        BuildClassList();
        Loaded += async (_, _) => { await VerifyDatabaseAsync(); await LoadPlcOptionsAsync(); };
    }

    private void BuildClassList()
    {
        _classes = SystemClassCatalog.CreateDefaults()
            .Where(x => x.PrimaryStorage != null && !string.IsNullOrWhiteSpace(x.PrimaryStorage.TableName) && (!_addresses || (x.ClassNumber >= 1 && x.ClassNumber <= 8)))
            .OrderBy(x => x.ClassNumber)
            .Select(x => new DuplicateClassOption(x.Name, x.PrimaryStorage.TableName, x.ClassNumber, ResolveAddressKind(x.ClassNumber)))
            .ToList();
        CmbClass.ItemsSource = _classes;
        if (_classes.Count > 0) CmbClass.SelectedIndex = 0;
    }

    private static string ResolveAddressKind(int n) => n switch { 6 => "Input", 7 => "Output", 4 => "Input", _ => "Output" };

    private async void CmbDatabase_SelectionChanged(object sender, SelectionChangedEventArgs e)
    { if (IsLoaded && CmbDatabase.SelectedItem != null) { await VerifyDatabaseAsync(); await LoadPlcOptionsAsync(); } }
    private async void CmbClass_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded && !_loadingPlc && CmbClass.SelectedItem != null) await LoadPlcOptionsAsync(); }

    private async Task LoadPlcOptionsAsync()
    {
        var dbName = CmbDatabase.SelectedItem?.ToString(); var option = CmbClass.SelectedItem as DuplicateClassOption;
        if (string.IsNullOrWhiteSpace(dbName) || option == null) return;
        try
        {
            _loadingPlc = true; CmbPlc.IsEnabled = false;
            var db = new DatabaseService(_connection.ToConnectionString(dbName));
            var values = await db.GetDistinctColumnValuesAsync(option.TableName, "PLC");
            var items = new List<string> { "Все PLC" }; items.AddRange(values); CmbPlc.ItemsSource = items; CmbPlc.SelectedIndex = 0;
            TxtPlcStatus.Text = values.Count == 0 ? "PLC не найдены" : $"Доступно PLC: {values.Count}";
        }
        catch { CmbPlc.ItemsSource = new[] { "Все PLC" }; CmbPlc.SelectedIndex = 0; TxtPlcStatus.Text = "Не удалось загрузить PLC"; }
        finally { _loadingPlc = false; CmbPlc.IsEnabled = true; }
    }

    private async Task VerifyDatabaseAsync()
    {
        var db = CmbDatabase.SelectedItem?.ToString(); if (string.IsNullOrWhiteSpace(db)) return;
        try { TxtStatus.Text = "Проверка подключения…"; TxtStatus.Text = await _connection.TestConnectionAsync(db) ? "База доступна" : "База недоступна"; }
        catch { TxtStatus.Text = "Ошибка подключения"; }
    }

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        var dbName = CmbDatabase.SelectedItem?.ToString(); var option = CmbClass.SelectedItem as DuplicateClassOption; var plc = CmbPlc.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(dbName) || option == null) return;
        if (string.Equals(plc, "Все PLC", StringComparison.OrdinalIgnoreCase)) plc = null;
        ResultsPanel.Children.Clear(); BtnExport.IsEnabled = false; TxtStatus.Text = "Проверяем…"; SetBusy(true);
        try
        {
            var db = new DatabaseService(_connection.ToConnectionString(dbName));
            if (_addresses)
            {
                _addressResults = await db.FindDuplicateAddressesAsync(option.TableName, option.AddressKind, plc);
                _recordResults = new();
                foreach (var g in _addressResults) AddResult($"PLC {g.Plc} · {g.Address}", $"Повторяется: {g.Occurrences} раз");
                TxtResultHeader.Text = $"Дублирование адресов · {option.Title}";
                TxtStatus.Text = $"Найдено дублей: {_addressResults.Count}";
                BtnExport.IsEnabled = _addressResults.Count > 0;
            }
            else
            {
                _recordResults = await db.FindDuplicateRecordsByPlcAsync(option.TableName, plc);
                _addressResults = new();
                foreach (var g in _recordResults) AddResult($"PLC {g.Plc} · Record {g.Record}", $"Повторяется: {g.Occurrences} раз");
                TxtResultHeader.Text = $"Дублирование Recordов · {option.Title}";
                TxtStatus.Text = $"Найдено дублей: {_recordResults.Count}";
                BtnExport.IsEnabled = _recordResults.Count > 0;
            }
            if ((_addresses ? _addressResults.Count : _recordResults.Count) == 0)
            { ResultsPanel.Children.Add(new TextBlock { Text = "Дублирования не найдено.", Foreground = (Brush)FindResource("BrushTextSecondary") }); TxtStatus.Text = "Дублирования не найдено"; }
        }
        catch (Exception ex) { TxtStatus.Text = "Ошибка"; MessageBox.Show(ex.Message, "Проверка дублирования", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { SetBusy(false); }
    }

    private void AddResult(string title, string detail)
    {
        var border = new Border { Background = (Brush)FindResource("BrushBase"), BorderBrush = (Brush)FindResource("BrushBorder"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(10), Margin = new Thickness(0,0,0,8) };
        var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("BrushText") });
        panel.Children.Add(new TextBlock { Text = detail, Margin = new Thickness(0,5,0,0), Foreground = (Brush)FindResource("BrushTextSecondary") }); border.Child = panel; ResultsPanel.Children.Add(border);
    }

    private void SetBusy(bool busy) { CmbDatabase.IsEnabled = !busy; CmbClass.IsEnabled = !busy; CmbPlc.IsEnabled = !busy; BtnExport.IsEnabled = !busy && (_addresses ? _addressResults.Count > 0 : _recordResults.Count > 0); }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_addresses && _addressResults.Count == 0 || !_addresses && _recordResults.Count == 0) return;
        var option = CmbClass.SelectedItem as DuplicateClassOption;
        var dialog = new SaveFileDialog { Title = "Экспорт дублирования", Filter = "Excel (*.xlsx)|*.xlsx", DefaultExt = ".xlsx", AddExtension = true, FileName = $"Дублирование_{(_addresses ? "адресов" : "Recordов")}_{option?.Title ?? "результат"}.xlsx" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (_addresses)
            {
                var rows = _addressResults.Select(g => (IReadOnlyList<object>)new object[] { g.Plc, option?.Title ?? "", g.Address, g.Occurrences }).ToList();
                ExcelExportService.Export(dialog.FileName, "Дублирование адресов", new[] { "PLC", "Класс", "Адрес", "Количество" }, rows);
            }
            else
            {
                var rows = _recordResults.Select(g => (IReadOnlyList<object>)new object[] { g.Plc, option?.Title ?? "", g.Record, g.Occurrences }).ToList();
                ExcelExportService.Export(dialog.FileName, "Дублирование Record", new[] { "PLC", "Класс", "Record", "Количество" }, rows);
            }
            TxtStatus.Text = "Экспорт Excel выполнен";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Экспорт Excel", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private sealed record DuplicateClassOption(string Title, string TableName, int ClassNumber, string AddressKind);
}

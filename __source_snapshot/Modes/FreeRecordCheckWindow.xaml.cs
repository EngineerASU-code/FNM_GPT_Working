using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using Configurator.Core.Architecture;

namespace Configurator;

public partial class FreeRecordCheckWindow : Window
{
    private readonly ConnectionSettings _connection;
    private readonly List<string> _databases;
    private List<RecordCheckClassOption> _classes = new();
    private bool _loadingPlcOptions;
    private List<FreeRecordGroup> _lastResults = new();

    public FreeRecordCheckWindow(ConnectionSettings connection, IEnumerable<string> databases, string selectedDatabase)
    {
        InitializeComponent();
        _connection = connection;
        _databases = databases?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                     ?? new List<string>();

        CmbDatabase.ItemsSource = _databases;
        if (!string.IsNullOrWhiteSpace(selectedDatabase) && _databases.Any(x => x.Equals(selectedDatabase, StringComparison.OrdinalIgnoreCase)))
            CmbDatabase.SelectedItem = _databases.First(x => x.Equals(selectedDatabase, StringComparison.OrdinalIgnoreCase));
        else if (_databases.Count > 0)
            CmbDatabase.SelectedIndex = 0;

        BuildClassList();
        Loaded += FreeRecordCheckWindow_Loaded;
    }

    private async void FreeRecordCheckWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= FreeRecordCheckWindow_Loaded;
        if (CmbDatabase.SelectedItem == null || CmbClass.SelectedItem == null) return;
        await VerifyDatabaseAsync();
        await LoadPlcOptionsAsync();
    }

    private void BuildClassList()
    {
        _classes = SystemClassCatalog.CreateDefaults()
            .Where(x => x.PrimaryStorage != null && !string.IsNullOrWhiteSpace(x.PrimaryStorage.TableName))
            .OrderBy(x => x.ClassNumber)
            .Select(x => new RecordCheckClassOption(x.Name, x.PrimaryStorage.TableName, x.ClassNumber))
            .ToList();
        CmbClass.ItemsSource = _classes;
        if (_classes.Count > 0) CmbClass.SelectedIndex = 0;
    }

    private async void CmbDatabase_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || CmbDatabase.SelectedItem == null) return;
        await VerifyDatabaseAsync();
        await LoadPlcOptionsAsync();
    }

    private async void CmbClass_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || CmbClass.SelectedItem == null || _loadingPlcOptions) return;
        await LoadPlcOptionsAsync();
    }

    private async Task LoadPlcOptionsAsync()
    {
        var dbName = CmbDatabase.SelectedItem?.ToString();
        var option = CmbClass.SelectedItem as RecordCheckClassOption;
        if (string.IsNullOrWhiteSpace(dbName) || option == null) return;

        try
        {
            _loadingPlcOptions = true;
            CmbPlc.ItemsSource = null;
            CmbPlc.IsEnabled = false;
            var db = new DatabaseService(_connection.ToConnectionString(dbName));
            var values = await db.GetDistinctColumnValuesAsync(option.TableName, "PLC");
            var items = new List<string> { "Все PLC" };
            items.AddRange(values);
            CmbPlc.ItemsSource = items;
            CmbPlc.SelectedIndex = 0;
            TxtPlcStatus.Text = values.Count == 0 ? "PLC не найдены" : $"Доступно PLC: {values.Count}";
        }
        catch
        {
            CmbPlc.ItemsSource = new List<string> { "Все PLC" };
            CmbPlc.SelectedIndex = 0;
            TxtPlcStatus.Text = "Не удалось загрузить PLC";
        }
        finally
        {
            _loadingPlcOptions = false;
            CmbPlc.IsEnabled = true;
        }
    }

    private async Task VerifyDatabaseAsync()
    {
        var dbName = CmbDatabase.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(dbName)) return;

        try
        {
            TxtStatus.Text = "Проверка подключения…";
            var ok = await _connection.TestConnectionAsync(dbName);
            if (!ok)
            {
                TxtStatus.Text = "База недоступна";
                MessageBox.Show($"Связь с БД «{dbName}» потеряна. Выберите активную базу.",
                    "Проверить свободные поля", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            TxtStatus.Text = "База доступна";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = "Ошибка подключения";
            MessageBox.Show(ex.Message, "Проверить свободные поля", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        var dbName = CmbDatabase.SelectedItem?.ToString();
        var option = CmbClass.SelectedItem as RecordCheckClassOption;
        var plc = CmbPlc.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(dbName) || option == null) return;

        if (string.Equals(plc, "Все PLC", StringComparison.OrdinalIgnoreCase)) plc = null;

        ResultsPanel.Children.Clear();
        BtnExport.IsEnabled = false;
        _lastResults = new();
        TxtResultHeader.Text = $"Проверка {option.Title} · {option.TableName}" + (plc == null ? " · все PLC" : $" · PLC {plc}");
        TxtStatus.Text = plc == null ? "Читаем Record…" : $"Читаем Record PLC {plc}…";
        SetBusy(true);

        try
        {
            var db = new DatabaseService(_connection.ToConnectionString(dbName));
            var result = await db.FindFreeRecordsByPlcAsync(option.TableName, plc);
            _lastResults = result;

            if (result.Count == 0)
            {
                ResultsPanel.Children.Add(new TextBlock
                {
                    Text = "Свободных Record в диапазонах не найдено.",
                    Foreground = (System.Windows.Media.Brush)FindResource("BrushTextSecondary"),
                    Margin = new Thickness(0, 4, 0, 4)
                });
                TxtStatus.Text = "Готово";
                return;
            }

            foreach (var group in result)
            {
                var border = new Border
                {
                    Background = (System.Windows.Media.Brush)FindResource("BrushBase"),
                    BorderBrush = (System.Windows.Media.Brush)FindResource("BrushBorder"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(7),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                var panel = new StackPanel();
                panel.Children.Add(new TextBlock
                {
                    Text = $"PLC {group.Plc} · диапазон {group.MinRecord}–{group.MaxRecord}",
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (System.Windows.Media.Brush)FindResource("BrushText")
                });
                panel.Children.Add(new TextBlock
                {
                    Text = $"Свободные Record: {string.Join(", ", group.FreeRecords.Select(x => x.ToString()))}",
                    Margin = new Thickness(0, 5, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (System.Windows.Media.Brush)FindResource("BrushTextSecondary")
                });
                border.Child = panel;
                ResultsPanel.Children.Add(border);
            }

            TxtStatus.Text = $"Найдено групп: {result.Count}";
            BtnExport.IsEnabled = result.Count > 0;
        }
        catch (Exception ex)
        {
            TxtStatus.Text = "Ошибка";
            MessageBox.Show($"Не удалось проверить Record: {ex.Message}",
                "Проверить свободные поля", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        CmbDatabase.IsEnabled = !busy;
        CmbClass.IsEnabled = !busy;
        CmbPlc.IsEnabled = !busy;
        BtnExport.IsEnabled = !busy && _lastResults.Count > 0;
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_lastResults.Count == 0) return;
        var option = CmbClass.SelectedItem as RecordCheckClassOption;
        var dialog = new SaveFileDialog
        {
            Title = "Экспорт проверки полей",
            Filter = "Excel (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx",
            AddExtension = true,
            FileName = $"Проверка_полей_{option?.Title ?? "результат"}.xlsx"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var rows = _lastResults.Select(g => (IReadOnlyList<object>)new object[]
            {
                g.Plc, g.MinRecord, g.MaxRecord, string.Join(", ", g.FreeRecords)
            }).ToList();
            ExcelExportService.Export(dialog.FileName, "Record", new[] { "PLC", "Минимальный Record", "Максимальный Record", "Свободные Record" }, rows);
            TxtStatus.Text = "Экспорт Excel выполнен";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось экспортировать результат: {ex.Message}", "Экспорт Excel", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private sealed record RecordCheckClassOption(string Title, string TableName, int ClassNumber);
}

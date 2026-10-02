using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using Configurator.Core.Architecture;

namespace Configurator;

public partial class FreeAddressCheckWindow : Window
{
    private readonly ConnectionSettings _connection;
    private readonly List<string> _databases;
    private List<AddressCheckClassOption> _classes = new();
    private bool _loadingPlcOptions;
    private List<FreeAddressByteGroup> _lastBitResults = new();
    private List<FreeAddressWordGroup> _lastWordResults = new();
    private bool _lastWasWord;

    public FreeAddressCheckWindow(ConnectionSettings connection, IEnumerable<string> databases, string selectedDatabase)
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
        Loaded += FreeAddressCheckWindow_Loaded;
    }

    private async void FreeAddressCheckWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= FreeAddressCheckWindow_Loaded;
        if (CmbDatabase.SelectedItem == null || CmbClass.SelectedItem == null) return;
        await VerifyDatabaseAsync();
        await LoadPlcOptionsAsync();
    }

    private void BuildClassList()
    {
        // Program / Step / Matrix (9-12) do not have controller address columns.
        _classes = SystemClassCatalog.CreateDefaults()
            .Where(x => x.ClassNumber >= 1 && x.ClassNumber <= 8 && x.PrimaryStorage != null && !string.IsNullOrWhiteSpace(x.PrimaryStorage.TableName))
            .OrderBy(x => x.ClassNumber)
            .Select(x => new AddressCheckClassOption(x.Name, x.PrimaryStorage.TableName, x.ClassNumber, ResolveAddressKind(x.ClassNumber)))
            .ToList();
        CmbClass.ItemsSource = _classes;
        if (_classes.Count > 0) CmbClass.SelectedIndex = 0;
    }

    private static string ResolveAddressKind(int classNumber) => classNumber switch
    {
        6 or 7 => "Word", // AI/AO use the Address field and occupy two bytes.
        4 => "Input",
        _ => "Output"
    };

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
        var option = CmbClass.SelectedItem as AddressCheckClassOption;
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
            TxtStatus.Text = ok ? "База доступна" : "База недоступна";
            if (!ok)
                MessageBox.Show($"Связь с БД «{dbName}» потеряна. Выберите активную базу.", "Проверить свободные адреса", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            TxtStatus.Text = "Ошибка подключения";
            MessageBox.Show(ex.Message, "Проверить свободные адреса", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        var dbName = CmbDatabase.SelectedItem?.ToString();
        var option = CmbClass.SelectedItem as AddressCheckClassOption;
        var plc = CmbPlc.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(dbName) || option == null) return;
        if (string.Equals(plc, "Все PLC", StringComparison.OrdinalIgnoreCase)) plc = null;

        ResultsPanel.Children.Clear();
        BtnExport.IsEnabled = false;
        _lastBitResults = new();
        _lastWordResults = new();
        _lastWasWord = option.AddressKind == "Word";
        var wordPrefix = option.ClassNumber == 6 ? "IW" : option.ClassNumber == 7 ? "QW" : null;
        TxtResultHeader.Text = option.AddressKind == "Word"
                ? $"Проверка {option.Title} · {option.TableName} · {wordPrefix ?? "Address"}"
                : $"Проверка {option.Title} · {option.TableName} · Address_{option.AddressKind}";
        TxtStatus.Text = "Читаем адреса…";
        SetBusy(true);
        try
        {
            var db = new DatabaseService(_connection.ToConnectionString(dbName));
            if (option.AddressKind == "Word")
            {
                var wordResult = await db.FindFreeAddressWordsAsync(option.TableName, plc, wordPrefix);
                _lastWordResults = wordResult;
                RenderWordResults(wordResult);
                TxtStatus.Text = wordResult.Count == 0 ? "Адресов не найдено" : $"Проверено адресов: {wordResult.Count}";
                BtnExport.IsEnabled = wordResult.Count > 0;
                return;
            }

            var result = await db.FindFreeAddressBitsAsync(option.TableName, option.AddressKind, plc);
            _lastBitResults = result;
            if (result.Count == 0)
            {
                ResultsPanel.Children.Add(new TextBlock
                {
                    Text = "Адресов с распознанными байтами не найдено.",
                    Foreground = (System.Windows.Media.Brush)FindResource("BrushTextSecondary"),
                    Margin = new Thickness(0, 4, 0, 0)
                });
                TxtStatus.Text = "Готово";
                return;
            }

            foreach (var group in result)
            {
                var expander = new Expander { IsExpanded = false, Margin = new Thickness(0, 0, 0, 8) };
                expander.Header = $"{group.Prefix}{group.ByteAddress} · свободно {group.FreeBits.Count}/8";
                var bits = new WrapPanel { Margin = new Thickness(4, 8, 4, 4) };
                foreach (var bit in group.Bits)
                {
                    var bitBorder = new Border
                    {
                        Padding = new Thickness(9, 5, 9, 5),
                        Margin = new Thickness(0, 0, 6, 6),
                        CornerRadius = new CornerRadius(5),
                        BorderThickness = new Thickness(1),
                        BorderBrush = (System.Windows.Media.Brush)FindResource("BrushBorder"),
                        Background = bit.IsFree
                            ? (System.Windows.Media.Brush)FindResource("BrushPanel")
                            : (System.Windows.Media.Brush)FindResource("BrushSelected")
                    };
                    bitBorder.Child = new TextBlock
                    {
                        Text = $"{group.Prefix}{group.ByteAddress}.{bit.Bit} · {(bit.IsFree ? "свободен" : "занят")}",
                        Foreground = (System.Windows.Media.Brush)FindResource("BrushText")
                    };
                    bits.Children.Add(bitBorder);
                }
                expander.Content = bits;
                ResultsPanel.Children.Add(expander);
            }
            TxtStatus.Text = $"Проверено байтов: {result.Count}";
            BtnExport.IsEnabled = result.Count > 0;
        }
        catch (Exception ex)
        {
            TxtStatus.Text = "Ошибка";
            MessageBox.Show($"Не удалось проверить адреса: {ex.Message}", "Проверить свободные адреса", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { SetBusy(false); }
    }

    private void RenderWordResults(List<FreeAddressWordGroup> result)
    {
        ResultsPanel.Children.Clear();
        if (result.Count == 0)
        {
            ResultsPanel.Children.Add(new TextBlock
            {
                Text = "Адресов с распознанными двухбайтовыми словами не найдено.",
                Foreground = (System.Windows.Media.Brush)FindResource("BrushTextSecondary"),
                Margin = new Thickness(0, 4, 0, 0)
            });
            return;
        }

        foreach (var group in result)
        {
            var expander = new Expander { IsExpanded = false, Margin = new Thickness(0, 0, 0, 8) };
            expander.Header = $"{group.Prefix}{group.Address} · занято {group.OccupiedBytes}/2";
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 8, 4, 4) };
            foreach (var byteItem in group.Bytes)
            {
                var border = new Border
                {
                    Padding = new Thickness(9, 5, 9, 5),
                    Margin = new Thickness(0, 0, 6, 6),
                    CornerRadius = new CornerRadius(5),
                    BorderThickness = new Thickness(1),
                    BorderBrush = (System.Windows.Media.Brush)FindResource("BrushBorder"),
                    Background = byteItem.IsFree
                        ? (System.Windows.Media.Brush)FindResource("BrushPanel")
                        : (System.Windows.Media.Brush)FindResource("BrushSelected")
                };
                border.Child = new TextBlock
                {
                    Text = $"{group.Prefix}{byteItem.Address} · {(byteItem.IsFree ? "свободен" : "занят")}",
                    Foreground = (System.Windows.Media.Brush)FindResource("BrushText")
                };
                panel.Children.Add(border);
            }
            expander.Content = panel;
            ResultsPanel.Children.Add(expander);
        }
    }

    private void SetBusy(bool busy)
    {
        CmbDatabase.IsEnabled = !busy;
        CmbClass.IsEnabled = !busy;
        CmbPlc.IsEnabled = !busy;
        BtnExport.IsEnabled = !busy && ((_lastWasWord && _lastWordResults.Count > 0) || (!_lastWasWord && _lastBitResults.Count > 0));
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if ((_lastWasWord && _lastWordResults.Count == 0) || (!_lastWasWord && _lastBitResults.Count == 0)) return;
        var option = CmbClass.SelectedItem as AddressCheckClassOption;
        var plc = CmbPlc.SelectedItem?.ToString() ?? "Все PLC";
        var dialog = new SaveFileDialog
        {
            Title = "Экспорт проверки адресов",
            Filter = "Excel (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx",
            AddExtension = true,
            FileName = $"Проверка_адресов_{option?.Title ?? "результат"}.xlsx"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (_lastWasWord)
            {
                var rows = _lastWordResults.SelectMany(g => g.Bytes.Select(b => (IReadOnlyList<object>)new object[] { plc, option?.Title ?? "", g.Prefix + g.Address, b.Address, b.IsFree ? "свободен" : "занят" })).ToList();
                ExcelExportService.Export(dialog.FileName, "Адреса", new[] { "PLC", "Класс", "Группа", "Адрес", "Состояние" }, rows);
            }
            else
            {
                var rows = _lastBitResults.SelectMany(g => g.Bits.Select(b => (IReadOnlyList<object>)new object[] { plc, option?.Title ?? "", $"{g.Prefix}{g.ByteAddress}", b.Bit, b.IsFree ? "свободен" : "занят" })).ToList();
                ExcelExportService.Export(dialog.FileName, "Адреса", new[] { "PLC", "Класс", "Байт", "Бит", "Состояние" }, rows);
            }
            TxtStatus.Text = "Экспорт Excel выполнен";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось экспортировать результат: {ex.Message}", "Экспорт Excel", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private sealed record AddressCheckClassOption(string Title, string TableName, int ClassNumber, string AddressKind);
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Configurator.Core.Architecture;

namespace Configurator;

public partial class NewClassWindow : Window
{
    private readonly IReadOnlyList<ClassDefinition> _classes;
    public ClassDefinition Result { get; private set; }
    public NewClassWindow(IEnumerable<ClassDefinition> classes)
    {
        InitializeComponent();
        _classes = (classes ?? Array.Empty<ClassDefinition>()).ToList();
        CmbBase.ItemsSource = _classes;
        CmbBase.SelectedIndex = 0;
        CmbBase.SelectionChanged += (_, __) => { if (CmbBase.SelectedItem is ClassDefinition selected && string.IsNullOrWhiteSpace(TxtTable.Text)) TxtTable.Text = $"dbo.{TxtName.Text.Trim()}"; };
        TxtNumber.Text = (_classes.Select(x => x.ClassNumber).DefaultIfEmpty(0).Max() + 1).ToString();
        TxtTable.Text = "dbo.NewClass";
        TxtName.TextChanged += (_, __) => { if (TxtTable.Text == "dbo.NewClass" || string.IsNullOrWhiteSpace(TxtTable.Text)) TxtTable.Text = $"dbo.{TxtName.Text.Trim()}"; };
    }
    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtName.Text)) { MessageBox.Show("Введите имя класса.", "Новый класс", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        int.TryParse(TxtNumber.Text, out int number);
        Result = new ClassDefinition
        {
            Id = Guid.NewGuid().ToString("N"), Name = TxtName.Text.Trim(), ClassNumber = number, Description = TxtDescription.Text?.Trim() ?? "",
            BaseClassId = (CmbBase.SelectedItem as ClassDefinition)?.Id ?? "",
            Available = true,
            StorageMappings = new List<StorageMapping> { new StorageMapping { Role = "primary", TableName = string.IsNullOrWhiteSpace(TxtTable.Text) ? $"dbo.{TxtName.Text.Trim()}" : TxtTable.Text.Trim() } }
        };
        DialogResult = true;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

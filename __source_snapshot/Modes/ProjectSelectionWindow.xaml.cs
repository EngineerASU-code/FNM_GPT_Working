using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Configurator;

public partial class ProjectSelectionWindow : Window
{
    public string SelectedDatabase { get; private set; } = string.Empty;
    public bool ConnectNewRequested { get; private set; }

    public ProjectSelectionWindow(IEnumerable<string> databases, string selectedDatabase)
    {
        InitializeComponent();
        ProjectList.ItemsSource = (databases ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!string.IsNullOrWhiteSpace(selectedDatabase))
            ProjectList.SelectedItem = ProjectList.Items.Cast<string>().FirstOrDefault(x => x.Equals(selectedDatabase, StringComparison.OrdinalIgnoreCase));
        if (ProjectList.SelectedIndex < 0 && ProjectList.Items.Count > 0) ProjectList.SelectedIndex = 0;
    }

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        SelectedDatabase = ProjectList.SelectedItem?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(SelectedDatabase))
        {
            MessageBox.Show("Выберите подключенную базу данных.", "Выбор проекта", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
        Close();
    }

    private void ConnectNew_Click(object sender, RoutedEventArgs e)
    {
        ConnectNewRequested = true;
        DialogResult = false;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

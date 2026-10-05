using System.Windows;

namespace Configurator;

public partial class SettingsPlaceholderWindow : Window
{
    public SettingsPlaceholderWindow() => InitializeComponent();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

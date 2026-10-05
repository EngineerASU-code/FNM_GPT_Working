using System.Windows;

namespace Configurator;

public partial class HotkeysWindow : Window
{
    public HotkeysWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

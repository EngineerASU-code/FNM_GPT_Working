using System.Windows;

namespace Configurator;

public partial class TextInputWindow : Window
{
    public string Result => InputText.Text.Trim();

    public TextInputWindow(string title, string prompt, string initial = "")
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        InputText.Text = initial;
        Loaded += (_, _) => { InputText.Focus(); InputText.SelectAll(); };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Result)) return;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

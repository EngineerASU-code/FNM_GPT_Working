using System.Windows;

namespace Configurator
{
    public partial class RenameProgramWindow : Window
    {
        public string Result { get; private set; } = string.Empty;

        public RenameProgramWindow(string currentName)
        {
            InitializeComponent();
            TxtName.Text = currentName ?? string.Empty;
            Loaded += (_, _) => { TxtName.Focus(); TxtName.SelectAll(); };
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            Result = TxtName.Text.Trim();
            if (string.IsNullOrWhiteSpace(Result))
            {
                MessageBox.Show("Имя программы не может быть пустым.", "Переименование", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}

using System.Windows;
using System.Windows.Input;

namespace Configurator
{
    public partial class AdminPasswordWindow : Window
    {
        public bool IsValid { get; private set; }

        public AdminPasswordWindow()
        {
            InitializeComponent();
            Loaded += (s, e) => PasswordInput.Focus();
        }

        private void BtnUnlock_Click(object sender, RoutedEventArgs e) => Validate();

        private void PasswordInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                Validate();
        }

        private void Validate()
        {
            if (PasswordInput.Password == "admin")
            {
                IsValid = true;
                DialogResult = true;
            }
            else
            {
                MessageBox.Show("Неверный пароль.", "Доступ запрещён",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                PasswordInput.Clear();
                PasswordInput.Focus();
            }
        }
    }
}
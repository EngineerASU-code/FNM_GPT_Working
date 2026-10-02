using System.Windows;
using System.Windows.Input;

namespace Configurator
{
    public partial class EmptyModeWindow : Window
    {
        private readonly int _mode;

        public EmptyModeWindow(int mode)
        {
            _mode = mode;
            InitializeComponent();
            Loaded += (s, e) => Focus();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }
    }
}

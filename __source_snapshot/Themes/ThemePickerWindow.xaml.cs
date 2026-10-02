using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Configurator.Themes;

namespace Configurator
{
    public partial class ThemePickerWindow : Window
    {
        public ThemePickerWindow()
        {
            InitializeComponent();
            HighlightActiveCard();
        }

        private void HighlightActiveCard()
        {
            string currentName = ThemeManager.CurrentPreset?.Name ?? "Светлая";

            CardLight.BorderBrush = currentName == "Светлая"
                ? new SolidColorBrush(Color.FromRgb(44, 136, 204))
                : new SolidColorBrush(Color.FromRgb(204, 204, 204));

            CardDark.BorderBrush = currentName == "Тёмная"
                ? new SolidColorBrush(Color.FromRgb(44, 136, 204))
                : new SolidColorBrush(Color.FromRgb(102, 102, 102));
        }

        private void CardLight_Click(object sender, MouseButtonEventArgs e)
        {
            ThemeManager.ApplyTheme(ThemeManager.DefaultLight);
            HighlightActiveCard();
        }

        private void CardDark_Click(object sender, MouseButtonEventArgs e)
        {
            ThemeManager.ApplyTheme(ThemeManager.DefaultDark);
            HighlightActiveCard();
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.ResetToDefault();
            HighlightActiveCard();
        }

        private void BtnCustomize_Click(object sender, RoutedEventArgs e)
        {
            var themeWindow = new ThemeWindow(ThemeManager.CurrentPreset ?? ThemeManager.DefaultLight);
            themeWindow.Owner = this;
            themeWindow.ShowDialog();
            HighlightActiveCard();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

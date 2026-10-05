using System;
using System.Windows;
using System.Windows.Controls;

namespace Configurator
{
    public partial class LeftPanel : UserControl
    {
        public event Action AddDatabaseClicked;
        public event Action RefreshDatabaseRequested;

        public LeftPanel()
        {
            InitializeComponent();
        }

        private void BtnAddDb_Click(object sender, RoutedEventArgs e)
        {
            AddDatabaseClicked?.Invoke();
        }

        private void BtnRefreshDb_Click(object sender, RoutedEventArgs e)
        {
            RefreshDatabaseRequested?.Invoke();
        }

        public void HidePlaceholder()
        {
            if (this.FindName("EmptyTreePlaceholder") is Border placeholder)
            {
                placeholder.Visibility = Visibility.Collapsed;
            }
        }

        public void ShowPlaceholder()
        {
            if (this.FindName("EmptyTreePlaceholder") is Border placeholder)
            {
                placeholder.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// Включает или выключает кнопку "Добавить базу".
        /// Вызывается из MainWindow при добавлении/удалении баз.
        /// </summary>

        public void SetLoadingStatus(string text, bool loading)
        {
            if (DbLoadingStatus == null) return;
            DbLoadingStatus.Text = text ?? "";
            DbLoadingStatus.FontWeight = loading ? FontWeights.SemiBold : FontWeights.Normal;
            DbLoadingStatus.SetResourceReference(TextBlock.ForegroundProperty, loading ? "BrushAccent" : "BrushTextSecondary");
        }

        public void SetAddDbButtonEnabled(bool enabled)
        {
            if (BtnAddDb != null)
            {
                BtnAddDb.IsEnabled = enabled;
                BtnAddDb.Opacity = enabled ? 1.0 : 0.4;
            }
        }
    }
}

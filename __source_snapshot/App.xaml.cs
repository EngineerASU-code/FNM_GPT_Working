using System;
using System.Windows;
using Configurator.Themes;

namespace Configurator
{
    public partial class App : global::System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Тема применяется до создания главного окна, поэтому первый кадр
            // приложения не появляется в "не той" палитре.
            try
            {
                ThemeManager.InitializeDefault();
            }
            catch
            {
                // Ресурсы темы имеют безопасные значения из App.xaml.
            }

            var mainWindow = new global::Configurator.MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
        }
    }
}

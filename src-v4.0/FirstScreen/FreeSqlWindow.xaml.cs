using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Configurator
{
    public partial class FreeSqlWindow : Window
    {
        private readonly ConnectionSettings _connection;

        public FreeSqlWindow(ConnectionSettings connection, string preferredDatabase = null)
        {
            InitializeComponent();
            _connection = connection;

            CmbDatabase.ItemsSource = connection?.ConnectedDatabases?.ToList() ?? new System.Collections.Generic.List<string>();

            if (!string.IsNullOrWhiteSpace(preferredDatabase) && CmbDatabase.Items.Contains(preferredDatabase))
                CmbDatabase.SelectedItem = preferredDatabase;
            else if (CmbDatabase.Items.Count > 0)
                CmbDatabase.SelectedIndex = 0;

            UpdateDatabaseContext();
        }

        private void CmbDatabase_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            ResultGrid.ItemsSource = null;
            TxtStatus.Text = "Готово";
            UpdateDatabaseContext();
        }

        private void UpdateDatabaseContext()
        {
            string database = CmbDatabase.SelectedItem?.ToString();
            TxtStatus.Text = string.IsNullOrWhiteSpace(database)
                ? "База данных не выбрана"
                : $"Контекст: {database}";
        }

        private async void BtnExecute_Click(object sender, RoutedEventArgs e)
        {
            string database = CmbDatabase.SelectedItem?.ToString();

            if (string.IsNullOrWhiteSpace(database))
            {
                MessageBox.Show("Выберите базу данных.", "Свободный ввод",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string sql = TxtSql.Text.Trim();
            if (string.IsNullOrWhiteSpace(sql))
            {
                MessageBox.Show("Введите SQL-запрос.", "Свободный ввод",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                SetBusy(true);
                TxtStatus.Text = $"Выполнение в базе: {database}";

                // Ключевой момент: connection string создаётся СТРОГО из выбранной БД.
                // DatabaseService дополнительно проверяет фактический DB_NAME().
                var db = new DatabaseService(_connection.ToConnectionString(database));
                var result = await db.ExecuteFreeSqlAsync(sql, database);

                ResultGrid.ItemsSource = result.Result?.DefaultView;
                TxtStatus.Text = result.Result != null
                    ? $"База: {database} · Получено строк: {result.Result.Rows.Count}"
                    : $"База: {database} · Затронуто строк: {result.AffectedRows}";
            }
            catch (Exception ex)
            {
                ResultGrid.ItemsSource = null;
                TxtStatus.Text = "Ошибка выполнения";
                MessageBox.Show(ex.Message, "SQL ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void SetBusy(bool busy)
        {
            CmbDatabase.IsEnabled = !busy;
            TxtSql.IsEnabled = !busy;
            BtnExecute.IsEnabled = !busy;
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            TxtSql.Clear();
            ResultGrid.ItemsSource = null;
            UpdateDatabaseContext();
            TxtSql.Focus();
        }
    }
}
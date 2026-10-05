using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Configurator
{
    public partial class MainWindow
    {
        private void ShowKnownDatabasesLoading()
        {
            _leftPanel.ServerTree.Items.Clear();
            _leftPanel.SetLoadingStatus("Проверка сохранённых подключений…", true);
            _leftPanel.HidePlaceholder();

            foreach (var dbName in _connection.KnownDatabases
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var dbItem = new TreeViewItem
                {
                    Header = CreateTreeHeader($"◌ {dbName}", "Проверка подключения…", true),
                    IsEnabled = false,
                    ToolTip = "Проверяется подключение к базе данных"
                };
                _leftPanel.ServerTree.Items.Add(dbItem);
            }

            if (_leftPanel.ServerTree.Items.Count == 0)
                _leftPanel.ShowPlaceholder();
        }

        private void RebuildTree()
        {
            _leftPanel.ServerTree.Items.Clear();
            _leftPanel.SetLoadingStatus(_failedDatabases.Count > 0
                ? $"Подключено: {_connection.ConnectedDatabases.Count} · недоступно: {_failedDatabases.Count}"
                : $"Подключено: {_connection.ConnectedDatabases.Count}", false);
            _loadedTreeNodes.Clear();

            foreach (var dbName in _connection.ConnectedDatabases)
            {
                bool failed = _failedDatabases.Contains(dbName);
                var header = failed ? "❌ " + dbName : dbName;

                var dbItem = new TreeViewItem
                {
                    Header = CreateTreeHeader(header, "База проекта", true),
                    FontWeight = FontWeights.Bold,
                    Tag = dbName,
                    ToolTip = "База проекта"
                };

                dbItem.Selected += (s, e) =>
                {
                    e.Handled = true;
                    _selectedDbName = dbName;
                    _activeDbName = dbName;
                    UpdateRemoveDbMenuState();
                };

                if (!failed)
                {
                    dbItem.Items.Add(new TreeViewItem { Header = "Загрузка...", IsEnabled = false });
                    dbItem.Expanded += (s, e) => OnDatabaseExpanded(dbName, dbItem);
                }

                _leftPanel.ServerTree.Items.Add(dbItem);
            }

            _leftPanel.EmptyTreePlaceholder.Visibility = _leftPanel.ServerTree.Items.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            UpdateRemoveDbMenuState();
        }

        private async void OnDatabaseExpanded(string dbName, TreeViewItem dbItem)
        {
            if (_loadedTreeNodes.Contains(dbName)) return;

            dbItem.Items.Clear();
            dbItem.Items.Add(new TreeViewItem { Header = "Загрузка...", IsEnabled = false });

            string cacheKey = VmCacheKey(dbName);
            if (!_vms.ContainsKey(cacheKey))
            {
                var connStr = _connection.ToConnectionString(dbName);
                _vms[cacheKey] = new TableViewModel(connStr);
            }

            var vm = _vms[cacheKey];

            try
            {
                var tables = await vm.GetTableListAsync();

                dbItem.Items.Clear();

                foreach (DataRow row in tables.Rows)
                {
                    string schema = row[0]?.ToString() ?? "dbo";
                    string tableName = row[1]?.ToString() ?? "";
                    if (string.IsNullOrEmpty(tableName)) continue;

                    string displayName = $"{schema}.{tableName}";

                    var tableItem = new TreeViewItem
                    {
                        Header = CreateTreeHeader(displayName, "Таблица", false),
                        FontWeight = FontWeights.Normal,
                        ToolTip = displayName,
                        Tag = new TableNodeInfo
                        {
                            DatabaseName = dbName,
                            Schema = schema,
                            TableName = tableName
                        }
                    };
                    tableItem.Selected += (s, e) =>
                    {
                        e.Handled = true;
                        OnTableSelected(dbName, schema, tableName);
                    };
                    tableItem.PreviewMouseLeftButtonDown += (s, e) =>
                    {
                        if (s is TreeViewItem item)
                        {
                            item.Focus();
                            item.IsSelected = true;
                            e.Handled = true;
                        }
                    };
                    dbItem.Items.Add(tableItem);
                }

                if (dbItem.Items.Count == 0)
                    dbItem.Items.Add(new TreeViewItem { Header = "(таблиц нет)", IsEnabled = false });

                _loadedTreeNodes.Add(dbName);
            }
            catch (Exception ex)
            {
                dbItem.Items.Clear();
                dbItem.Items.Add(new TreeViewItem
                {
                    Header = $"❌ {ex.Message} (сверните и раскройте для повтора)",
                    IsEnabled = false
                });
            }
        }

        private async void OnTableSelected(string dbName, string schema, string tableName)
        {
            _activeDbName = dbName;
            _selectedDbName = dbName;

            string cacheKey = VmCacheKey(dbName);
            if (!_vms.ContainsKey(cacheKey))
            {
                var connStr = _connection.ToConnectionString(dbName);
                _vms[cacheKey] = new TableViewModel(connStr);
            }

            _activeVm = _vms[cacheKey];

            try
            {
                await _activeVm.SelectTableAsync($"{schema}.{tableName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки метаданных: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                ClearActiveTable();
                return;
            }

            _ = LoadDataAsync();
            UpdateRemoveDbMenuState();
        }


        private static StackPanel CreateTreeHeader(string title, string subtitle, bool bold)
        {
            var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(1, 0, 4, 0) };
            var titleText = new TextBlock { Text = title, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, FontSize = 12 };
            titleText.SetResourceReference(TextBlock.ForegroundProperty, "BrushText");
            panel.Children.Add(titleText);
            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                var subText = new TextBlock { Text = subtitle, FontSize = 9 };
                subText.SetResourceReference(TextBlock.ForegroundProperty, "BrushTextSecondary");
                panel.Children.Add(subText);
            }
            return panel;
        }

        public void RemoveSelectedDatabase()
        {
            string dbNameToRemove = "";

            if (_leftPanel.ServerTree?.SelectedItem is TreeViewItem selectedItem)
            {
                if (selectedItem.Parent is TreeView)
                {
                    dbNameToRemove = selectedItem.Tag?.ToString() ?? "";
                }
                else if (selectedItem.Parent is TreeViewItem parentItem
                    && parentItem.Parent is TreeView)
                {
                    dbNameToRemove = parentItem.Tag?.ToString() ?? "";
                }
            }

            if (string.IsNullOrEmpty(dbNameToRemove))
                return;

            _connection.ConnectedDatabases.Remove(dbNameToRemove);
            _connection.KnownDatabases.Remove(dbNameToRemove);
            _failedDatabases.Remove(dbNameToRemove);
            _loadedTreeNodes.Remove(dbNameToRemove);
            _vms.Remove(VmCacheKey(dbNameToRemove));

            bool wasActive = (_activeDbName == dbNameToRemove);

            RebuildTree();

            if (wasActive)
            {
                _activeDbName = "";
                _activeVm = null;
                ClearActiveTable();
            }

            _selectedDbName = "";
            UpdateRemoveDbMenuState();

            try
            {
                _connection.SaveToFile();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RemoveSelectedDatabase] Save failed: {ex.Message}");
            }
        }
    }
}

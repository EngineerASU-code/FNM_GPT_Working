using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Configurator
{
    public partial class TableFilterWindow : Window
    {
        private readonly List<ColumnInfo> _columns;

        public TableFilter Result { get; private set; }

        public TableFilterWindow(IEnumerable<ColumnInfo> columns, TableFilter current = null)
        {
            InitializeComponent();

            _columns = columns?.ToList() ?? new List<ColumnInfo>();
            // Используем строки в ComboBox, чтобы глобальный WPF style
            // никогда не показывал ColumnInfo.ToString().
            CmbColumn.ItemsSource = _columns.Select(c => c.Name).ToList();

            if (current != null)
            {
                CmbColumn.SelectedItem = _columns.FirstOrDefault(c => string.Equals(c.Name, current.Column, StringComparison.OrdinalIgnoreCase))?.Name;
            }
            if (CmbColumn.SelectedIndex < 0 && _columns.Count > 0)
                CmbColumn.SelectedIndex = 0;

            // Старые значения фильтра (английские) остаются совместимыми.
            TxtValue.Text = current?.Value ?? "";
            UpdateOperators(current?.Operator);
        }

        private ColumnInfo SelectedColumn
            => _columns.FirstOrDefault(c => string.Equals(c.Name, CmbColumn.SelectedItem?.ToString(), StringComparison.OrdinalIgnoreCase));

        private static bool IsTextType(string type)
        {
            type = (type ?? "").ToLowerInvariant();
            return type is "char" or "varchar" or "nchar" or "nvarchar" or "text" or "ntext";
        }

        private static bool IsNumericType(string type)
        {
            type = (type ?? "").ToLowerInvariant();
            return type is "tinyint" or "smallint" or "int" or "bigint" or "decimal" or "numeric" or "money" or "smallmoney" or "float" or "real";
        }

        private static bool IsDateType(string type)
        {
            type = (type ?? "").ToLowerInvariant();
            return type is "date" or "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset" or "time";
        }

        private List<string> BuildOperators(string preferred = null)
        {
            var result = new List<string>();
            if (IsTextType(SelectedColumn?.DataType))
            {
                result.AddRange(new[] { "Содержит", "Не содержит", "Равно", "Не равно", "Начинается с", "Заканчивается на" });
            }
            else if (IsNumericType(SelectedColumn?.DataType) || IsDateType(SelectedColumn?.DataType))
            {
                result.AddRange(new[] { "Равно", "Не равно", "Больше", "Больше или равно", "Меньше", "Меньше или равно" });
            }
            else
            {
                result.AddRange(new[] { "Равно", "Не равно", "Содержит", "Не содержит" });
            }

            result.Add("Пусто (NULL)");
            result.Add("Не пусто (NOT NULL)");

            CmbOperator.ItemsSource = result;

            string selected = TranslateOperator(preferred);
            CmbOperator.SelectedItem = result.Contains(selected) ? selected : result[0];
            return result;
        }

        private static string TranslateOperator(string op)
        {
            return op switch
            {
                "Contains" => "Содержит",
                "Not Contains" => "Не содержит",
                "Equals" => "Равно",
                "Not Equals" => "Не равно",
                "Starts With" => "Начинается с",
                "Ends With" => "Заканчивается на",
                "Greater" => "Больше",
                "Greater Or Equal" => "Больше или равно",
                "Less" => "Меньше",
                "Less Or Equal" => "Меньше или равно",
                "Is Null" => "Пусто (NULL)",
                "Is Not Null" => "Не пусто (NOT NULL)",
                _ => op
            };
        }

        private void UpdateOperators(string preferred = null)
        {
            BuildOperators(preferred);
            UpdateValueState();
        }

        private void UpdateValueState()
        {
            string op = CmbOperator.SelectedItem?.ToString() ?? "";
            bool needsValue = op is not ("Пусто (NULL)" or "Не пусто (NOT NULL)");
            TxtValue.IsEnabled = needsValue;
            if (!needsValue) TxtValue.Text = "";

            string type = SelectedColumn?.DataType ?? "";
            TxtTypeHint.Text = string.IsNullOrWhiteSpace(type) ? "" : $"Тип поля: {type}";
        }

        private void CmbColumn_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            UpdateOperators();
        }

        private void CmbOperator_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            UpdateValueState();
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            var column = SelectedColumn?.Name;
            var op = CmbOperator.SelectedItem?.ToString();

            if (string.IsNullOrWhiteSpace(column) || string.IsNullOrWhiteSpace(op))
            {
                MessageBox.Show("Выберите поле и условие.", "Фильтр",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool needsValue = op is not ("Пусто (NULL)" or "Не пусто (NOT NULL)");
            if (needsValue && string.IsNullOrWhiteSpace(TxtValue.Text))
            {
                MessageBox.Show("Введите значение для фильтра.", "Фильтр",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Result = new TableFilter
            {
                Column = column,
                Operator = op,
                Value = TxtValue.Text.Trim()
            };
            DialogResult = true;
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            Result = null;
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
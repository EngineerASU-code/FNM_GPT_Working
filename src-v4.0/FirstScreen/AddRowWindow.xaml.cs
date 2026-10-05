using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Configurator
{
    public partial class AddRowWindow : Window
    {
        private readonly List<ColumnInfo> _columns;
        private readonly Dictionary<string, TextBox> _fieldInputs = new Dictionary<string, TextBox>(StringComparer.OrdinalIgnoreCase);
        private readonly string _recordColumn;
        private readonly int _autoRecordValue;
        private readonly string _tableName;
        private readonly string _connectionString;
        private readonly string _plcColumn;
        private CancellationTokenSource _recordCts;
        private int _recordGeneration;
        private bool _updatingAutoRecord;
        private readonly int? _forcedClassNumber;
        private readonly string _classNumberColumn;
        private readonly Dictionary<string, List<LookupOption>> _lookupOptions;
        private readonly Dictionary<string, ComboBox> _lookupInputs = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, object> Values { get; private set; }
        public bool Confirmed { get; private set; } = false;

        public AddRowWindow(
            string tableName,
            List<ColumnInfo> columns,
            int nextRecordValue,
            string recordColumn,
            string connectionString = null,
            string plcColumn = null,
            int? forcedClassNumber = null,
            string classNumberColumn = null,
            Dictionary<string, List<LookupOption>> lookupOptions = null)
        {
            InitializeComponent();
            _forcedClassNumber = forcedClassNumber;
            _classNumberColumn = classNumberColumn ?? "";
            _lookupOptions = lookupOptions ?? new Dictionary<string, List<LookupOption>>(StringComparer.OrdinalIgnoreCase);
            _tableName = tableName ?? "";
            WindowTitle.Text = $"Новая строка — {_tableName}";
            _columns = columns ?? new List<ColumnInfo>();
            _recordColumn = recordColumn ?? "";
            _autoRecordValue = nextRecordValue;
            _connectionString = connectionString;
            _plcColumn = string.IsNullOrWhiteSpace(plcColumn)
                ? _columns.FirstOrDefault(c => c.Name.Equals("PLC", StringComparison.OrdinalIgnoreCase))?.Name ?? ""
                : plcColumn;
            if (_tableName.Equals("dbo.Prog", StringComparison.OrdinalIgnoreCase))
            {
                Width = 1080;
                Height = 680;
                MinWidth = 1000;
                FieldsScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                FieldsScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            }
            GenerateFields();
            Loaded += AddRowWindow_Loaded;
            Closed += (_, _) => _recordCts?.Cancel();
        }

        private void GenerateFields()
        {
            FieldsPanel.Children.Clear();
            var groups = _columns.Where(c => !c.IsReadOnly)
                .GroupBy(GetFieldGroup)
                .OrderBy(g => GroupOrder(g.Key))
                .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            foreach (var group in groups)
            {
                var groupBorder = new Border
                {
                    Margin = new Thickness(0, 0, 0, 10),
                    Padding = new Thickness(10, 8, 10, 8),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(7)
                };
                groupBorder.SetResourceReference(Border.BackgroundProperty, "BrushPanel");
                groupBorder.SetResourceReference(Border.BorderBrushProperty, "BrushBorder");

                var groupPanel = new StackPanel();
                var header = new TextBlock
                {
                    Text = group.Key,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 7)
                };
                header.SetResourceReference(TextBlock.ForegroundProperty, "BrushTextSecondary");
                groupPanel.Children.Add(header);

                var fieldsGrid = new UniformGrid { Columns = 3 };
                foreach (var col in group)
                {
                    var fieldPanel = new StackPanel { Margin = new Thickness(0, 0, 12, 8) };
                    bool isKey = col.Name.Equals(_recordColumn, StringComparison.OrdinalIgnoreCase);
                    bool isRecord = isKey && col.Name.Equals("Record", StringComparison.OrdinalIgnoreCase);
                    bool isClassNumber = _forcedClassNumber.HasValue &&
                        !string.IsNullOrWhiteSpace(_classNumberColumn) &&
                        col.Name.Equals(_classNumberColumn, StringComparison.OrdinalIgnoreCase);
                    string titleText = isClassNumber ? "🔒 " : (isRecord ? "↻ " : (isKey ? "🔑 " : ""));
                    titleText += $"{col.Name} ({col.DataType})";
                    if (col.IsNullable) titleText += " — nullable";
                    if (col.HasDefault) titleText += " — default";

                    var label = new TextBlock
                    { Text = titleText, FontSize = 10, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) };
                    label.SetResourceReference(TextBlock.ForegroundProperty, "BrushText");

                    bool isLookup = _lookupOptions.TryGetValue(col.Name, out var options) && options.Count > 0;
                    Control input;
                    if (isLookup)
                    {
                        var combo = new ComboBox { Width = 290, Height = 30, FontSize = 12, ItemsSource = options, DisplayMemberPath = nameof(LookupOption.Display), SelectedValuePath = nameof(LookupOption.Key) };
                        _lookupInputs[col.Name] = combo;
                        input = combo;
                    }
                    else
                    {
                        input = new TextBox { Width = 290, Height = 30, Padding = new Thickness(8, 5, 8, 5), FontSize = 12 };
                    }
                    var textBox = input as TextBox;
                    if (isClassNumber && textBox != null)
                    {
                        textBox.Text = _forcedClassNumber.Value.ToString(CultureInfo.InvariantCulture);
                        textBox.IsReadOnly = true;
                        textBox.ToolTip = "Номер класса назначается автоматически и не редактируется для системного класса.";
                    }
                    else if (isRecord && _autoRecordValue > 0 && textBox != null)
                    {
                        textBox.Text = _autoRecordValue.ToString(CultureInfo.InvariantCulture);
                    }
                    if (textBox != null)
                        textBox.ToolTip = isClassNumber
                            ? "Номер класса назначается автоматически."
                            : isRecord
                            ? (!string.IsNullOrWhiteSpace(_plcColumn) ? "Предложение: последний Record для выбранного PLC + 1. Значение можно изменить вручную." : "Предложение: последний Record + 1. Значение можно изменить вручную.")
                            : (col.HasDefault ? "Пусто → значение по умолчанию" : col.IsNullable ? "Пусто → NULL" : "Обязательное поле");

                    fieldPanel.Children.Add(label);
                    fieldPanel.Children.Add(input);
                    if (textBox != null) _fieldInputs[col.Name] = textBox;
                    if (isLookup && input is ComboBox lookupBox) lookupBox.SelectedIndex = options.FindIndex(x => x.Key == (isClassNumber ? _forcedClassNumber?.ToString(CultureInfo.InvariantCulture) : ""));
                    if (!string.IsNullOrWhiteSpace(_plcColumn) && textBox != null && col.Name.Equals(_plcColumn, StringComparison.OrdinalIgnoreCase))
                        textBox.TextChanged += PlcTextBox_TextChanged;
                    fieldsGrid.Children.Add(fieldPanel);
                }
                groupPanel.Children.Add(fieldsGrid);
                groupBorder.Child = groupPanel;
                FieldsPanel.Children.Add(groupBorder);
            }
        }

        private static int GroupOrder(string group) => group switch
        {
            "Идентификация и контекст" => 0,
            "Наименование и описание" => 1,
            "PLC / идентификаторы" => 2,
            "Параметры" => 3,
            _ => 10
        };

        private static string GetFieldGroup(ColumnInfo col)
        {
            if (col == null) return "Основное";
            string n = col.Name.ToLowerInvariant();
            if (n is "plc" or "record" || n.Contains("class") || n == "area") return "Идентификация и контекст";
            if (n.StartsWith("name") || n.StartsWith("description") || n.StartsWith("text")) return "Наименование и описание";
            if (n.Contains("address") || n.Contains("number") || n.Contains("db_")) return "PLC / идентификаторы";
            return "Параметры";
        }

        private void AddRowWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_plcColumn) && _fieldInputs.TryGetValue(_plcColumn, out var plcBox))
                _ = UpdateRecordFromPlcAsync(plcBox.Text, CancellationToken.None);
        }

        private async void PlcTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Событие подвешивается только на поле PLC, поэтому Name проверять не нужно:
            // у динамически созданного TextBox Name обычно пустой.
            if (_updatingAutoRecord || sender is not TextBox plcBox)
                return;
            await UpdateRecordFromPlcAsync(plcBox.Text, CancellationToken.None);
        }

        private async Task UpdateRecordFromPlcAsync(string plcText, CancellationToken externalToken)
        {
            if (string.IsNullOrWhiteSpace(_connectionString) || string.IsNullOrWhiteSpace(_tableName) ||
                string.IsNullOrWhiteSpace(_recordColumn) || string.IsNullOrWhiteSpace(_plcColumn))
                return;

            if (!_fieldInputs.TryGetValue(_recordColumn, out var recordBox))
                return;

            int generation = ++_recordGeneration;
            var previous = _recordCts;
            try { previous?.Cancel(); } catch (ObjectDisposedException) { }
            _recordCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            var ct = _recordCts.Token;

            if (string.IsNullOrWhiteSpace(plcText))
                return;

            try
            {
                var plcColumn = _columns.FirstOrDefault(c => c.Name.Equals(_plcColumn, StringComparison.OrdinalIgnoreCase));
                if (plcColumn == null) return;

                object plcValue = ConvertValue(plcText.Trim(), plcColumn);
                var db = new DatabaseService(_connectionString);
                int nextRecord = await db.GetNextScopedRecordValueAsync(_tableName, _recordColumn, _plcColumn, plcValue, ct);
                if (generation != _recordGeneration || ct.IsCancellationRequested) return;

                _updatingAutoRecord = true;
                recordBox.Text = nextRecord.ToString(CultureInfo.InvariantCulture);
                _updatingAutoRecord = false;
            }
            catch (OperationCanceledException)
            {
                _updatingAutoRecord = false;
            }
            catch
            {
                _updatingAutoRecord = false;
                // Некорректный PLC не блокирует ручной ввод Record.
            }
        }

        private async void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_connectionString) &&
                !string.IsNullOrWhiteSpace(_plcColumn) &&
                !string.IsNullOrWhiteSpace(_recordColumn) &&
                _fieldInputs.TryGetValue(_plcColumn, out var plcBox) &&
                _fieldInputs.TryGetValue(_recordColumn, out var recordBox) &&
                !string.IsNullOrWhiteSpace(plcBox.Text) &&
                string.IsNullOrWhiteSpace(recordBox.Text))
            {
                await UpdateRecordFromPlcAsync(plcBox.Text, CancellationToken.None);
            }

            Values = new Dictionary<string, object>();
            var errors = new List<string>();
            bool hasUserValue = false;

            foreach (var colInfo in _columns.Where(c => !c.IsReadOnly))
            {
                string fieldName = colInfo.Name;
                string text = _lookupInputs.TryGetValue(fieldName, out var lookupBox)
                    ? lookupBox.SelectedValue?.ToString() ?? ""
                    : _fieldInputs.TryGetValue(fieldName, out var textBoxValue) ? textBoxValue.Text.Trim() : "";
                if (colInfo == null || colInfo.IsReadOnly)
                    continue;

                if (_forcedClassNumber.HasValue && !string.IsNullOrWhiteSpace(_classNumberColumn) &&
                    fieldName.Equals(_classNumberColumn, StringComparison.OrdinalIgnoreCase))
                    continue;

                bool generatedField = fieldName.Equals(_recordColumn, StringComparison.OrdinalIgnoreCase);
                if (!generatedField && !string.IsNullOrEmpty(text))
                    hasUserValue = true;

                if (string.IsNullOrEmpty(text))
                {
                    if (colInfo.HasDefault)
                        continue;
                    if (colInfo.IsNullable)
                    {
                        Values[fieldName] = DBNull.Value;
                        continue;
                    }
                    if (!colInfo.IsIdentity)
                        errors.Add($"Поле \"{fieldName}\" обязательно для заполнения");
                    continue;
                }

                try
                {
                    Values[fieldName] = ConvertValue(text, colInfo);
                }
                catch (Exception ex)
                {
                    errors.Add($"Поле \"{fieldName}\": {ex.Message}");
                }
            }

            if (_forcedClassNumber.HasValue && !string.IsNullOrWhiteSpace(_classNumberColumn))
                Values[_classNumberColumn] = _forcedClassNumber.Value;

            if (errors.Count > 0)
            {
                MessageBox.Show(string.Join("\n", errors), "Ошибка ввода",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                Values = null;
                return;
            }

            // Полностью пустую строку не создаём. Это особенно важно для
            // таблиц без ключа, где SQL Server может технически разрешить
            // INSERT всех NULL. Предзаполненный Record считается данными.
            bool hasAutoGeneratedValue = _fieldInputs.Values.Any(t => t.IsReadOnly && !string.IsNullOrWhiteSpace(t.Text));
            if (!hasUserValue && !hasAutoGeneratedValue)
            {
                MessageBox.Show("Нельзя добавить полностью пустую строку. Заполните хотя бы одно поле.", "Нет данных",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Confirmed = true;
            Close();
        }

        private static object ConvertValue(string text, ColumnInfo col)
        {
            var dt = col.DataType.ToLower();

            if (dt == "int")
                return int.Parse(text);
            if (dt == "bigint")
                return long.Parse(text);
            if (dt == "smallint")
                return short.Parse(text);
            if (dt == "tinyint")
                return byte.Parse(text);

            if (dt == "decimal" || dt == "numeric" || dt == "money" || dt == "smallmoney")
                return decimal.Parse(text, CultureInfo.InvariantCulture);
            if (dt == "float")
                return double.Parse(text, CultureInfo.InvariantCulture);
            if (dt == "real")
                return float.Parse(text, CultureInfo.InvariantCulture);

            if (dt == "date" || dt == "datetime" || dt == "datetime2" || dt == "smalldatetime")
                return DateTime.Parse(text, CultureInfo.InvariantCulture);
            if (dt == "datetimeoffset")
                return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);
            if (dt == "time")
                return TimeSpan.Parse(text, CultureInfo.InvariantCulture);

            if (dt == "bit")
            {
                if (text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase) || text.Equals("да", StringComparison.OrdinalIgnoreCase))
                    return true;
                return false;
            }

            if (dt == "uniqueidentifier")
                return Guid.Parse(text);

            return text;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                BtnConfirm_Click(sender, e);
            else if (e.Key == Key.Escape)
                Close();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Configurator
{
    public class DisplayFieldsBuilder
    {
        private readonly WrapPanel _panel;
        public Dictionary<string, TextBlock> Fields { get; } = new();

        public DisplayFieldsBuilder(WrapPanel panel)
        {
            _panel = panel;
        }

        public void Generate(List<ColumnInfo> columns, string keyColumn)
        {
            _panel.Children.Clear();
            Fields.Clear();

            foreach (var col in columns)
            {
                bool isKey = !string.IsNullOrEmpty(keyColumn) &&
                    col.Name.Equals(keyColumn, StringComparison.OrdinalIgnoreCase);

                var fieldPanel = new StackPanel
                {
                    Width = 232,
                    Margin = new Thickness(0, 0, 10, 6)
                };

                var label = new TextBlock
                {
                    Text = (isKey ? "🔑 " : "") + col.Name,
                    FontSize = 11,
                    FontWeight = isKey ? FontWeights.Bold : FontWeights.Normal,
                    Margin = new Thickness(0, 0, 0, 4),
                    TextWrapping = TextWrapping.NoWrap,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                label.SetResourceReference(TextBlock.ForegroundProperty, "BrushTextSecondary");

                var displayBorder = new Border
                {
                    Width = 220,
                    Height = 32,
                    MinHeight = 32,
                    MaxHeight = 32,
                    Padding = new Thickness(8, 4, 8, 4),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4)
                };
                displayBorder.SetResourceReference(Border.BackgroundProperty, "BrushPanel");
                displayBorder.SetResourceReference(Border.BorderBrushProperty, "BrushBorder");

                var displayText = new TextBlock
                {
                    Text = "",
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.NoWrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextAlignment = TextAlignment.Left
                };
                displayText.SetResourceReference(TextBlock.ForegroundProperty, "BrushText");

                displayBorder.Child = displayText;
                fieldPanel.Children.Add(label);
                fieldPanel.Children.Add(displayBorder);
                _panel.Children.Add(fieldPanel);
                Fields[col.Name] = displayText;
            }
        }

        public void Fill(Dictionary<string, string> values)
        {
            foreach (var kvp in Fields)
            {
                if (values.TryGetValue(kvp.Key, out string val))
                    kvp.Value.Text = val;
                else
                    kvp.Value.Text = "";
            }
        }

        public void ClearValues()
        {
            foreach (var text in Fields.Values) text.Text = "";
        }

        public void Clear()
        {
            _panel.Children.Clear();
            Fields.Clear();
        }
    }
}

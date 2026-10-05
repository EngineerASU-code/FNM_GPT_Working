namespace Configurator.Themes
{
    public class ThemePreset
    {
        public string Name { get; set; } = "Светлая";

        public System.Windows.Media.Color Base { get; set; } = System.Windows.Media.Color.FromRgb(245, 245, 245);
        public System.Windows.Media.Color Panel { get; set; } = System.Windows.Media.Color.FromRgb(255, 255, 255);
        public System.Windows.Media.Color Border { get; set; } = System.Windows.Media.Color.FromRgb(204, 204, 204);
        public System.Windows.Media.Color Text { get; set; } = System.Windows.Media.Color.FromRgb(51, 51, 51);
        public System.Windows.Media.Color Accent { get; set; } = System.Windows.Media.Color.FromRgb(44, 136, 204);
        public System.Windows.Media.Color Hover { get; set; } = System.Windows.Media.Color.FromRgb(235, 240, 248);
        public System.Windows.Media.Color Selected { get; set; } = System.Windows.Media.Color.FromRgb(220, 232, 245);
        public System.Windows.Media.Color ComboBoxBackground { get; set; } = System.Windows.Media.Color.FromRgb(255, 255, 255);
        public System.Windows.Media.Color ComboBoxForeground { get; set; } = System.Windows.Media.Color.FromRgb(31, 41, 55);
        public System.Windows.Media.Color ComboBoxBorder { get; set; } = System.Windows.Media.Color.FromRgb(217, 222, 231);
        public System.Windows.Media.Color ComboBoxPopupBackground { get; set; } = System.Windows.Media.Color.FromRgb(255, 255, 255);
        public System.Windows.Media.Color ComboBoxPopupForeground { get; set; } = System.Windows.Media.Color.FromRgb(31, 41, 55);
        public System.Windows.Media.Color ComboBoxItemHoverBackground { get; set; } = System.Windows.Media.Color.FromRgb(240, 244, 250);
        public System.Windows.Media.Color ComboBoxItemHoverForeground { get; set; } = System.Windows.Media.Color.FromRgb(31, 41, 55);
        public System.Windows.Media.Color ComboBoxItemSelectedBackground { get; set; } = System.Windows.Media.Color.FromRgb(231, 240, 255);
        public System.Windows.Media.Color ComboBoxItemSelectedForeground { get; set; } = System.Windows.Media.Color.FromRgb(31, 41, 55);
        public System.Windows.Media.Color ComboBoxBorderHover { get; set; } = System.Windows.Media.Color.FromRgb(52, 120, 246);
        public System.Windows.Media.Color ComboBoxBorderFocus { get; set; } = System.Windows.Media.Color.FromRgb(52, 120, 246);
        public System.Windows.Media.Color ComboBoxDisabledBackground { get; set; } = System.Windows.Media.Color.FromRgb(235, 237, 241);
        public System.Windows.Media.Color ComboBoxDisabledForeground { get; set; } = System.Windows.Media.Color.FromRgb(120, 125, 135);


        public ThemePreset Clone() => new ThemePreset
        {
            Name = Name,
            Base = Base,
            Panel = Panel,
            Border = Border,
            Text = Text,
            Accent = Accent,
            Hover = Hover,
            Selected = Selected,
            ComboBoxBackground = ComboBoxBackground, ComboBoxForeground = ComboBoxForeground, ComboBoxBorder = ComboBoxBorder,
            ComboBoxPopupBackground = ComboBoxPopupBackground, ComboBoxPopupForeground = ComboBoxPopupForeground,
            ComboBoxItemHoverBackground = ComboBoxItemHoverBackground, ComboBoxItemHoverForeground = ComboBoxItemHoverForeground,
            ComboBoxItemSelectedBackground = ComboBoxItemSelectedBackground, ComboBoxItemSelectedForeground = ComboBoxItemSelectedForeground,
            ComboBoxBorderHover = ComboBoxBorderHover, ComboBoxBorderFocus = ComboBoxBorderFocus,
            ComboBoxDisabledBackground = ComboBoxDisabledBackground, ComboBoxDisabledForeground = ComboBoxDisabledForeground
        };
    }
}

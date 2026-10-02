using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace Configurator
{
    public class AppSettings
    {
        public string LastThemeName { get; set; } = "Светлая";
        public string LastServer { get; set; } = "";
        public string LastUser { get; set; } = "";

        // Полные цвета темы — сохраняются при ApplyTheme
        public string Base { get; set; } = "#F5F5F5";
        public string Panel { get; set; } = "#FFFFFF";
        public string Border { get; set; } = "#CCCCCC";
        public string Text { get; set; } = "#333333";
        public string Accent { get; set; } = "#2C88CC";
        public string Hover { get; set; } = "#EBF0F8";
        public string Selected { get; set; } = "#DCE8F5";
        public string ComboBoxBackground { get; set; } = "#FFFFFF";
        public string ComboBoxForeground { get; set; } = "#1F2937";
        public string ComboBoxBorder { get; set; } = "#D9DEE7";
        public string ComboBoxPopupBackground { get; set; } = "#FFFFFF";
        public string ComboBoxPopupForeground { get; set; } = "#1F2937";
        public string ComboBoxItemHoverBackground { get; set; } = "#F0F4FA";
        public string ComboBoxItemHoverForeground { get; set; } = "#1F2937";
        public string ComboBoxItemSelectedBackground { get; set; } = "#E7F0FF";
        public string ComboBoxItemSelectedForeground { get; set; } = "#1F2937";
        public string ComboBoxBorderHover { get; set; } = "#3478F6";
        public string ComboBoxBorderFocus { get; set; } = "#3478F6";
        public string ComboBoxDisabledBackground { get; set; } = "#EBEDF1";
        public string ComboBoxDisabledForeground { get; set; } = "#787D87";

        private static readonly string SettingsPath = Path.Combine(
            System.AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return new AppSettings();

                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
            }
        }

        // Утилиты для конвертации Color ↔ string
        public static string ColorToHex(Color c) =>
            $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        public static Color HexToColor(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Colors.White;
            if (hex.StartsWith("#")) hex = hex.Substring(1);
            if (hex.Length == 6)
            {
                byte r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                byte g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
                byte b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
                return Color.FromRgb(r, g, b);
            }
            return Colors.White;
        }
    }
}

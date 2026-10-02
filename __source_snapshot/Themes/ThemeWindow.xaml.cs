using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;
using Configurator.Themes;

namespace Configurator
{
    public partial class ThemeWindow : Window
    {
        private ThemePreset _preset;

        public ThemeWindow(ThemePreset preset)
        {
            InitializeComponent();
            _preset = preset?.Clone() ?? new ThemePreset();
            TxtPresetName.Text = _preset.Name;
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            PreviewBase.Background = new SolidColorBrush(_preset.Base);
            PreviewPanel.Background = new SolidColorBrush(_preset.Panel);
            PreviewBorder.Background = new SolidColorBrush(_preset.Border);
            PreviewText.Background = new SolidColorBrush(_preset.Text);
            PreviewAccent.Background = new SolidColorBrush(_preset.Accent);
            PreviewHover.Background = new SolidColorBrush(_preset.Hover);
            PreviewSelected.Background = new SolidColorBrush(_preset.Selected);
            PreviewComboBoxBackground.Background = new SolidColorBrush(_preset.ComboBoxBackground);
            PreviewComboBoxForeground.Background = new SolidColorBrush(_preset.ComboBoxForeground);
            PreviewComboBoxBorder.Background = new SolidColorBrush(_preset.ComboBoxBorder);
            PreviewComboBoxPopupBackground.Background = new SolidColorBrush(_preset.ComboBoxPopupBackground);
            PreviewComboBoxPopupForeground.Background = new SolidColorBrush(_preset.ComboBoxPopupForeground);
            PreviewComboBoxItemHoverBackground.Background = new SolidColorBrush(_preset.ComboBoxItemHoverBackground);
            PreviewComboBoxItemHoverForeground.Background = new SolidColorBrush(_preset.ComboBoxItemHoverForeground);
            PreviewComboBoxItemSelectedBackground.Background = new SolidColorBrush(_preset.ComboBoxItemSelectedBackground);
            PreviewComboBoxItemSelectedForeground.Background = new SolidColorBrush(_preset.ComboBoxItemSelectedForeground);
            PreviewComboBoxBorderHover.Background = new SolidColorBrush(_preset.ComboBoxBorderHover);
            PreviewComboBoxBorderFocus.Background = new SolidColorBrush(_preset.ComboBoxBorderFocus);
            PreviewComboBoxDisabledBackground.Background = new SolidColorBrush(_preset.ComboBoxDisabledBackground);
            PreviewComboBoxDisabledForeground.Background = new SolidColorBrush(_preset.ComboBoxDisabledForeground);
        }

        private void BtnSelectBase_Click(object sender, RoutedEventArgs e)
        {
            if (PickColor(_preset.Base, out Color newColor))
            {
                _preset.Base = newColor;
                UpdatePreview();
            }
        }

        private void BtnSelectPanel_Click(object sender, RoutedEventArgs e)
        {
            if (PickColor(_preset.Panel, out Color newColor))
            {
                _preset.Panel = newColor;
                UpdatePreview();
            }
        }

        private void BtnSelectBorder_Click(object sender, RoutedEventArgs e)
        {
            if (PickColor(_preset.Border, out Color newColor))
            {
                _preset.Border = newColor;
                UpdatePreview();
            }
        }

        private void BtnSelectText_Click(object sender, RoutedEventArgs e)
        {
            if (PickColor(_preset.Text, out Color newColor))
            {
                _preset.Text = newColor;
                UpdatePreview();
            }
        }

        private void BtnSelectAccent_Click(object sender, RoutedEventArgs e)
        {
            if (PickColor(_preset.Accent, out Color newColor))
            {
                _preset.Accent = newColor;
                UpdatePreview();
            }
        }

        private void BtnSelectHover_Click(object sender, RoutedEventArgs e)
        {
            if (PickColor(_preset.Hover, out Color newColor))
            {
                _preset.Hover = newColor;
                UpdatePreview();
            }
        }

        private void BtnSelectSelected_Click(object sender, RoutedEventArgs e)
        {
            if (PickColor(_preset.Selected, out Color newColor))
            {
                _preset.Selected = newColor;
                UpdatePreview();
            }
        }

        private void BtnSelectComboColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string key) return;
            var prop = typeof(ThemePreset).GetProperty(key);
            if (prop == null || prop.PropertyType != typeof(Color)) return;
            var current = (Color)prop.GetValue(_preset);
            if (PickColor(current, out Color newColor))
            {
                prop.SetValue(_preset, newColor);
                UpdatePreview();
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            _preset.Name = string.IsNullOrWhiteSpace(TxtPresetName.Text)
                ? "Custom"
                : TxtPresetName.Text;

            ThemeManager.ApplyTheme(_preset);
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // === Win32 ChooseColor dialog ===

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CHOOSECOLOR
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public uint rgbResult;
            public IntPtr lpCustColors;
            public uint Flags;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public IntPtr lpTemplateName;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ChooseColor(ref CHOOSECOLOR lpChooseColor);

        private const uint CC_FULLOPEN = 0x00000002;
        private const uint CC_RGBINIT = 0x00000001;

        // COLORREF хранится в формате 0x00BBGGRR.
        // Держим массив отдельно, чтобы выбранные пользовательские цвета
        // сохранялись между открытиями диалога.
        private static readonly int[] _customColors = new int[16];

        private bool PickColor(Color initial, out Color result)
        {
            uint rgb = (uint)(initial.R | (initial.G << 8) | (initial.B << 16));

            var cc = new CHOOSECOLOR
            {
                lStructSize = Marshal.SizeOf<CHOOSECOLOR>(),
                hwndOwner = new WindowInteropHelper(this).Handle,
                hInstance = IntPtr.Zero,
                rgbResult = rgb,
                Flags = CC_FULLOPEN | CC_RGBINIT,
                lCustData = IntPtr.Zero,
                lpfnHook = IntPtr.Zero,
                lpTemplateName = IntPtr.Zero
            };

            GCHandle handle = default;
            try
            {
                handle = GCHandle.Alloc(_customColors, GCHandleType.Pinned);
                cc.lpCustColors = handle.AddrOfPinnedObject();

                if (ChooseColor(ref cc))
                {
                    byte r = (byte)(cc.rgbResult & 0xFF);
                    byte g = (byte)((cc.rgbResult >> 8) & 0xFF);
                    byte b = (byte)((cc.rgbResult >> 16) & 0xFF);
                    result = Color.FromRgb(r, g, b);
                    return true;
                }
            }
            finally
            {
                if (handle.IsAllocated)
                    handle.Free();
            }

            result = initial;
            return false;
        }
    }
}

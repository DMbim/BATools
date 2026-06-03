using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Color = System.Windows.Media.Color;

namespace BA.Subcategories.Views
{
    public partial class ColorPickerWindow : Window
    {
        public Color SelectedColor { get; private set; }
        public Autodesk.Revit.DB.Color Current { get; }

        private bool _updating;

        // Common line color presets for BIM subcategories
        private static readonly Color[] Presets =
        {
            Colors.Black,
            Color.FromRgb(80,  80,  80),
            Color.FromRgb(160, 160, 160),
            Colors.White,
            Color.FromRgb(200, 50,  50),
            Color.FromRgb(50,  130, 200),
            Color.FromRgb(50,  160, 80),
            Color.FromRgb(220, 160, 30),
            Color.FromRgb(160, 80,  200),
            Color.FromRgb(200, 120, 50),
            Color.FromRgb(0,   100, 160),
            Color.FromRgb(100, 40,  10),
        };

        public ColorPickerWindow(Color initial)
        {
            InitializeComponent();
            SelectedColor = initial;

            BuildPresets();
            SetSliders(initial);
            UpdatePreview();
        }

        public ColorPickerWindow(Autodesk.Revit.DB.Color current)
        {
            Current = current;
        }

        // ── Preset swatches ───────────────────────────────────────────────────

        private void BuildPresets()
        {
            foreach (var c in Presets)
            {
                var swatch = new Border
                {
                    Width = 28,
                    Height = 28,
                    Margin = new Thickness(3),
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(c),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 70)),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    ToolTip = $"#{c.R:X2}{c.G:X2}{c.B:X2}"
                };

                swatch.MouseLeftButtonDown += (s, e) =>
                {
                    SetSliders(c);
                    UpdatePreview();
                };

                PresetPanel.Children.Add(swatch);
            }
        }

        // ── Slider interaction ────────────────────────────────────────────────

        private void Slider_ValueChanged(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updating) return;
            UpdatePreview();
        }

        private void SetSliders(Color c)
        {
            _updating = true;
            SliderR.Value = c.R;
            SliderG.Value = c.G;
            SliderB.Value = c.B;
            _updating = false;
        }

        private void UpdatePreview()
        {
            if (SliderR == null || SliderG == null || SliderB == null) return;

            byte r = (byte)SliderR.Value;
            byte g = (byte)SliderG.Value;
            byte b = (byte)SliderB.Value;

            SelectedColor = Color.FromRgb(r, g, b);
            PreviewBorder.Background = new SolidColorBrush(SelectedColor);
            TxtHex.Text = $"#{r:X2}{g:X2}{b:X2}";
        }

        // ── Buttons ───────────────────────────────────────────────────────────

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            UpdatePreview();
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
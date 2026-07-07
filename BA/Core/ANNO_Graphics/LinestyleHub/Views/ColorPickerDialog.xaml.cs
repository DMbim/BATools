using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace BA.UI.LineStyleHub
{
    /// <summary>
    /// Minimal WPF color picker dialog.
    /// Shows RGB sliders and a live preview swatch.
    /// Does not depend on any third-party library.
    /// </summary>
    public partial class ColorPickerDialog : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public System.Windows.Media.Color SelectedColor { get; private set; }

        private byte _r, _g, _b;

        public byte R
        {
            get => _r;
            set { _r = value; Raise(nameof(R)); UpdatePreview(); }
        }
        public byte G
        {
            get => _g;
            set { _g = value; Raise(nameof(G)); UpdatePreview(); }
        }
        public byte B
        {
            get => _b;
            set { _b = value; Raise(nameof(B)); UpdatePreview(); }
        }

        private SolidColorBrush _previewBrush = new(Colors.Black);
        public SolidColorBrush PreviewBrush
        {
            get => _previewBrush;
            private set { _previewBrush = value; Raise(nameof(PreviewBrush)); }
        }

        private string _hexText = "#000000";
        public string HexText
        {
            get => _hexText;
            set
            {
                _hexText = value;
                Raise(nameof(HexText));
                // Parse and push back to RGB sliders when user types directly
                var s = (value ?? "").TrimStart('#');
                if (s.Length == 6
                    && byte.TryParse(s[0..2], System.Globalization.NumberStyles.HexNumber, null, out byte r)
                    && byte.TryParse(s[2..4], System.Globalization.NumberStyles.HexNumber, null, out byte g)
                    && byte.TryParse(s[4..6], System.Globalization.NumberStyles.HexNumber, null, out byte b))
                {
                    _r = r; _g = g; _b = b;
                    Raise(nameof(R)); Raise(nameof(G)); Raise(nameof(B));
                    UpdatePreview();
                }
            }
        }

        public ColorPickerDialog(System.Windows.Media.Color initial)
        {
            _r = initial.R;
            _g = initial.G;
            _b = initial.B;
            SelectedColor = initial;
            InitializeComponent();
            DataContext = this;
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            var c = System.Windows.Media.Color.FromRgb(_r, _g, _b);
            SelectedColor = c;
            PreviewBrush = new SolidColorBrush(c);
            _hexText = $"#{_r:X2}{_g:X2}{_b:X2}";
            Raise(nameof(HexText));
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Raise(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

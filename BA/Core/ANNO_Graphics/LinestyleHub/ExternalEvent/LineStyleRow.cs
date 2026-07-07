using Autodesk.Revit.DB;
using System.ComponentModel;
using System.Windows.Media;

namespace BA.UI.LineStyleHub
{
    /// <summary>
    /// Represents a single editable line style row in the grid.
    /// Wraps a Revit Category (subcategory) that owns line graphic overrides.
    /// IsDirty is set whenever any editable property changes.
    /// </summary>
    public sealed class LineStyleRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        // ── Identity (read-only) ────────────────────────────────────────────
        public ElementId CategoryId { get; }
        public string CategoryName { get; }
        public string ParentCategoryName { get; }

        // ── Edit guards ─────────────────────────────────────────────────────
        public bool IsEditable { get; }
        public bool CanRename { get; }
        public bool CanDelete { get; }

        // These are the inverse flags consumed by IsReadOnly bindings in XAML
        public bool IsNameReadOnly => !CanRename;
        public bool IsColorReadOnly => !IsEditable;
        public bool IsWeightReadOnly => !IsEditable;
        public bool IsPatternReadOnly => !IsEditable;
        public bool IsDeleteReadOnly => !CanDelete;

        // ── Dirty tracking ──────────────────────────────────────────────────
        private bool _isDirty;
        public bool IsDirty
        {
            get => _isDirty;
            private set { if (_isDirty == value) return; _isDirty = value; Raise(nameof(IsDirty)); }
        }

        public bool HasNameChange { get; private set; }
        public bool HasColorChange { get; private set; }
        public bool HasWeightChange { get; private set; }
        public bool HasPatternChange { get; private set; }
        public bool MarkedForDelete { get; private set; }

        // ── Original values (for reset / dirty detection) ───────────────────
        private string _originalName;
        private System.Windows.Media.Color _originalColor;
        private int _originalWeight;
        private string _originalPatternName;
        private ElementId _originalPatternId;

        // ── Editable properties ─────────────────────────────────────────────
        private string _styleName;
        public string StyleName
        {
            get => _styleName;
            set
            {
                if (_styleName == value) return;
                _styleName = value;
                HasNameChange = value != _originalName;
                IsDirty = HasNameChange || HasColorChange || HasWeightChange || HasPatternChange || MarkedForDelete;
                Raise(nameof(StyleName));
            }
        }

        private System.Windows.Media.Color _color;
        public System.Windows.Media.Color Color
        {
            get => _color;
            set
            {
                if (_color == value) return;
                _color = value;
                HasColorChange = value != _originalColor;
                IsDirty = HasNameChange || HasColorChange || HasWeightChange || HasPatternChange || MarkedForDelete;
                Raise(nameof(Color));
                Raise(nameof(ColorHex));
                Raise(nameof(ColorBrush));
            }
        }

        public string ColorHex
        {
            get => $"#{_color.R:X2}{_color.G:X2}{_color.B:X2}";
            set
            {
                // Accept "#RRGGBB" or "RRGGBB"
                var s = (value ?? "").TrimStart('#');
                if (s.Length == 6
                    && byte.TryParse(s[0..2], System.Globalization.NumberStyles.HexNumber, null, out byte r)
                    && byte.TryParse(s[2..4], System.Globalization.NumberStyles.HexNumber, null, out byte g)
                    && byte.TryParse(s[4..6], System.Globalization.NumberStyles.HexNumber, null, out byte b))
                {
                    Color = System.Windows.Media.Color.FromRgb(r, g, b);
                }
            }
        }

        public SolidColorBrush ColorBrush => new SolidColorBrush(_color);

        private int _lineWeight;
        public int LineWeight
        {
            get => _lineWeight;
            set
            {
                if (_lineWeight == value) return;
                _lineWeight = value;
                HasWeightChange = value != _originalWeight;
                IsDirty = HasNameChange || HasColorChange || HasWeightChange || HasPatternChange || MarkedForDelete;
                Raise(nameof(LineWeight));
            }
        }

        private string _patternName;
        public string PatternName
        {
            get => _patternName;
            set
            {
                if (_patternName == value) return;
                _patternName = value;
                HasPatternChange = value != _originalPatternName;
                IsDirty = HasNameChange || HasColorChange || HasWeightChange || HasPatternChange || MarkedForDelete;
                Raise(nameof(PatternName));
            }
        }

        // Resolved by the invoker when applying — set from PatternName lookup
        public ElementId? ResolvedPatternId { get; set; }

        // Cached original pattern id for the handler
        public ElementId OriginalPatternId => _originalPatternId;

        private bool _isMarkedForDelete;
        public bool IsMarkedForDelete
        {
            get => _isMarkedForDelete;
            set
            {
                if (_isMarkedForDelete == value) return;
                _isMarkedForDelete = value;
                MarkedForDelete = value;
                IsDirty = HasNameChange || HasColorChange || HasWeightChange || HasPatternChange || MarkedForDelete;
                Raise(nameof(IsMarkedForDelete));
            }
        }

        // ── Constructor ─────────────────────────────────────────────────────
        public LineStyleRow(
            ElementId categoryId,
            string categoryName,
            string parentCategoryName,
            bool isEditable,
            bool canRename,
            bool canDelete,
            System.Windows.Media.Color color,
            int lineWeight,
            string patternName,
            ElementId patternId)
        {
            CategoryId = categoryId;
            CategoryName = categoryName;
            ParentCategoryName = parentCategoryName;
            IsEditable = isEditable;
            CanRename = canRename;
            CanDelete = canDelete;

            _originalName = categoryName;
            _originalColor = color;
            _originalWeight = lineWeight;
            _originalPatternName = patternName;
            _originalPatternId = patternId;

            _styleName = categoryName;
            _color = color;
            _lineWeight = lineWeight;
            _patternName = patternName;
        }

        public void ResetDirty()
        {
            _originalName = _styleName;
            _originalColor = _color;
            _originalWeight = _lineWeight;
            _originalPatternName = _patternName;
            _originalPatternId = ResolvedPatternId ?? _originalPatternId;

            HasNameChange = false;
            HasColorChange = false;
            HasWeightChange = false;
            HasPatternChange = false;
            MarkedForDelete = false;
            _isMarkedForDelete = false;
            IsDirty = false;

            Raise(nameof(IsMarkedForDelete));
        }

        private void Raise(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public override string ToString() => $"{ParentCategoryName} / {CategoryName}";
    }
}

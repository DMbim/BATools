using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace BA.Subcategories.Models
{
    /// <summary>
    /// View-model row representing one subcategory in the manager grid.
    /// Carries both the live Category reference and the pending edits
    /// (name, color, weight) that are written to Revit on Apply.
    /// </summary>
    public class SubcategoryRow : ObservableObject
    {
        // ── Identity ──────────────────────────────────────────────────────────

        /// <summary>Revit ElementId of the subcategory. Null for rows not yet created.</summary>
        public ElementId? CategoryId { get; set; }

        /// <summary>
        /// The name this subcategory is currently known by in Revit. Set once
        /// when the row is loaded, or right after creation for a brand new row,
        /// and only updated again after a successful Apply. Callers must resolve
        /// the live Category by this value, never by the Name property while it
        /// is still being edited, or a pending rename can never find its target.
        /// </summary>
        public string OriginalName { get; set; } = string.Empty; // <- NEW

        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set
            {
                if (SetProperty(ref _name, value))
                    IsDirty = true;
            }
        }

        /// <summary>True when the edited Name no longer matches the live Revit name.</summary>
        public bool IsRenamed => // <- NEW
            !IsNew && !string.Equals(Name?.Trim(), OriginalName, System.StringComparison.OrdinalIgnoreCase);

        // ── Appearance ────────────────────────────────────────────────────────

        private Color _lineColor = Colors.Black;
        /// <summary>WPF color, converted to Revit Color on Apply.</summary>
        public Color LineColor
        {
            get => _lineColor;
            set
            {
                if (SetProperty(ref _lineColor, value))
                {
                    OnPropertyChanged(nameof(LineColorBrush));
                    IsDirty = true;
                }
            }
        }

        public SolidColorBrush LineColorBrush => new(_lineColor);

        private int _lineWeight = 1;
        /// <summary>Projection line weight (1 to 16).</summary>
        public int LineWeight
        {
            get => _lineWeight;
            set
            {
                int clamped = System.Math.Clamp(value, 1, 16);
                if (SetProperty(ref _lineWeight, clamped))
                    IsDirty = true;
            }
        }

        // ── State ─────────────────────────────────────────────────────────────

        private bool _isDirty;
        public bool IsDirty
        {
            get => _isDirty;
            set => SetProperty(ref _isDirty, value);
        }

        /// <summary>Marked for deletion on Apply.</summary>
        private bool _pendingDelete;
        public bool PendingDelete
        {
            get => _pendingDelete;
            set => SetProperty(ref _pendingDelete, value);
        }

        /// <summary>True when CategoryId is null, row was added in this session.</summary>
        public bool IsNew => CategoryId == null;
    }
}
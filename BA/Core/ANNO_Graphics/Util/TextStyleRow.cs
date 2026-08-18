using Autodesk.Revit.DB;
using System;
using System.ComponentModel;

namespace BA.UI.TextHub
{
    public sealed class TextStyleRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string Kind { get; }
        public string FamilyName { get; }
        public string TypeName { get; }
        public ElementId TypeId { get; }
        public string Notes { get; }

        private double? _textSizeMm;
        private string _textFont;

        public bool HasTextSize { get; }
        public bool HasTextFont { get; }

        public bool IsEditable => (HasTextSize || HasTextFont) && TypeId != ElementId.InvalidElementId;

        public bool IsTextSizeReadOnly => !HasTextSize;
        public bool IsTextFontReadOnly => !HasTextFont;

        public bool IsDirty { get; private set; }

        public double? TextSizeMm
        {
            get => _textSizeMm;
            set
            {
                if (_textSizeMm == value) return;
                _textSizeMm = value;
                IsDirty = true;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TextSizeMm)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirty)));
            }
        }

        public string TextFont
        {
            get => _textFont;
            set
            {
                var v = value ?? "";
                if (_textFont == v) return;
                _textFont = v;
                IsDirty = true;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TextFont)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirty)));
            }
        }

        public TextStyleRow(
            string kind,
            string familyName,
            string typeName,
            ElementId typeId,
            double? textSizeMm,
            string textFont,
            bool hasTextSize,
            bool hasTextFont,
            string notes)
        {
            Kind = kind ?? "";
            FamilyName = familyName ?? "";
            TypeName = typeName ?? "";
            TypeId = typeId ?? ElementId.InvalidElementId;

            _textSizeMm = textSizeMm;
            _textFont = textFont ?? "";

            HasTextSize = hasTextSize;
            HasTextFont = hasTextFont;
            Notes = notes ?? "";
        }

        public override string ToString() => $"{Kind} | {FamilyName} | {TypeName}";
    }
}
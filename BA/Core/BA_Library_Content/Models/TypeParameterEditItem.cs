using Autodesk.Revit.DB;

namespace BA.Core.Content.Models
{
    public sealed class TypeParameterEditItem
    {
        public string Name { get; set; } = string.Empty;
        public StorageType StorageType { get; set; } = StorageType.None;
        public bool IsReadOnly { get; set; }
        public bool IsShared { get; set; }
        public string OriginalValueDisplay { get; set; } = string.Empty;
        public string EditedValueText { get; set; } = string.Empty;
        public bool IsDirty => !string.Equals(OriginalValueDisplay, EditedValueText, System.StringComparison.Ordinal);
    }
}
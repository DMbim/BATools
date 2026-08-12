// Path: BA\Materials\Models\MaterialLibraryEntry.cs
using Autodesk.Revit.DB;

namespace BA.Materials.Models
{
    /// <summary>
    /// Lightweight row for the category tree / material list panes. Populated from a
    /// FilteredElementCollector pass over the library document, kept separate from the
    /// full Identity/Graphics/Appearance detail which is only loaded on selection.
    /// </summary>
    public sealed class MaterialLibraryEntry
    {
        public ElementId MaterialId { get; set; } = ElementId.InvalidElementId;
        public string Name { get; set; } = string.Empty;
        public string MaterialClass { get; set; } = string.Empty;
        public bool HasAppearanceAsset { get; set; }
    }
}
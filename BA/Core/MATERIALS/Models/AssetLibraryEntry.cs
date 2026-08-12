// Path: BA\Materials\Models\AssetLibraryEntry.cs
namespace BA.Materials.Models
{
    /// <summary>
    /// One entry from Application.GetAssets(AssetType.Appearance), Revit's built-in
    /// appearance asset library. IsGenericSchema gates whether the live sphere preview
    /// is available for this entry, non-Generic schemas (Metal, Ceramic, Glazing, etc)
    /// can still be applied to a material, just not previewed through our Generic-only
    /// channel reader.
    /// </summary>
    public sealed class AssetLibraryEntry
    {
        public string Name { get; set; } = string.Empty;
        public bool IsGenericSchema { get; set; }
        public bool IsFavorite { get; set; }
    }
}
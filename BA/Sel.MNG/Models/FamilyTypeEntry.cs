namespace BATools.SelectionManager.Models
{
    /// <summary>
    /// Persistent reference to a family type.
    /// UniqueId is a cache hint only — resolution falls back to name search
    /// so favourites work across projects that share the same family files.
    /// </summary>
    public class FamilyTypeEntry
    {
        public string UniqueId { get; set; } = string.Empty; // FamilySymbol.UniqueId
        public string FamilyName { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
    }
}
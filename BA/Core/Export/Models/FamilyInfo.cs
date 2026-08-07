namespace BA.Core.Export.Models
{
    /// <summary>
    /// Plain summary of one loadable Family in the project, for populating
    /// the family picker without exposing the Revit Family type to WPF.
    /// Identified by UniqueId, not ElementId, same stability rule already
    /// used elsewhere in this module for anything that crosses the
    /// WPF/Revit boundary.
    /// </summary>
    public class FamilyInfo
    {
        public string UniqueId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;

        /// <summary>
        /// False for system families and in-place families, neither of
        /// which Document.EditFamily can open. These are still listed, not
        /// hidden, so the user can see why a family they expected isn't
        /// exportable, but they can't be selected for export.
        /// </summary>
        public bool IsEditable { get; set; }
    }
}

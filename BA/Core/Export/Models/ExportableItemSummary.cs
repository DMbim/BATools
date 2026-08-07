namespace BA.Core.Export.Models
{
    /// <summary>
    /// Plain (ElementId, identity, display) data used to populate the picker
    /// without exposing View/ViewSheet or any other Revit API type to WPF.
    /// Covers both sheets and plain views, IsSheet/SheetNumber distinguish
    /// them, SheetNumber is empty for anything that isn't a ViewSheet.
    /// ElementId is stored and compared as a string everywhere (never parsed
    /// back into a typed ElementId), which keeps this feature independent of
    /// exactly which ElementId representation the installed Revit API build
    /// uses.
    /// </summary>
    public class ExportableItemSummary
    {
        public string ElementId { get; set; } = string.Empty;
        public bool IsSheet { get; set; }
        public string SheetNumber { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ViewTypeName { get; set; } = string.Empty;
    }
}

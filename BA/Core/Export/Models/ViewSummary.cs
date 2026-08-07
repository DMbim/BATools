namespace BA.Core.Export.Models
{
    /// <summary>
    /// Plain (Name, ViewType, UniqueId) summary used to populate the view
    /// picker without exposing View or any other Revit API type to WPF.
    /// UniqueId is what actually gets persisted in
    /// ExportJobSettings.SelectedViewUniqueIds, Name/ViewType are display
    /// only.
    /// </summary>
    public class ViewSummary
    {
        public string UniqueId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ViewType { get; set; } = string.Empty;
    }
}
namespace BA.Core.Export.Models
{
    /// <summary>
    /// Plain (SheetNumber, SheetName) pair used to populate the sheet picker
    /// without exposing ViewSheet or any other Revit API type to WPF code.
    /// </summary>
    public class SheetSummary
    {
        public string SheetNumber { get; set; } = string.Empty;
        public string SheetName { get; set; } = string.Empty;
    }
}

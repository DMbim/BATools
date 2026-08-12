namespace BA.Core.Export.Models
{
    public class SheetExportOutcome
    {
        public string SheetNumber { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string FolderPath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }
}

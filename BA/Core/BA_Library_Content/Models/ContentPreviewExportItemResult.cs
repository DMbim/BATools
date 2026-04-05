namespace BA.Core.Content.Models
{
    public sealed class ContentPreviewExportItemResult
    {
        public string FamilyPath { get; set; } = string.Empty;
        public string OutputImagePath { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;

    }
}
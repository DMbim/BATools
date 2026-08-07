namespace BA.Core.Export.Models
{
    /// <summary>
    /// One format's preview result. A job can now have both PDF and DWG
    /// enabled at once, so previewing a job produces a list of these, one
    /// per enabled format, not a single filename/folder pair.
    /// </summary>
    public class NamingPreviewResult
    {
        public ExportFormat Format { get; set; }
        public bool Success { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string Folder { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }
}

namespace BA.Core.Content.Models
{
    public sealed class ContentLibraryRoot
    {
        public string Name { get; set; } = string.Empty;
        public string RootPath { get; set; } = string.Empty;
        public bool IncludeSubfolders { get; set; } = true;
        public bool IsEnabled { get; set; } = true;
        public string ApprovalStateOverride { get; set; } = string.Empty;
    }
}
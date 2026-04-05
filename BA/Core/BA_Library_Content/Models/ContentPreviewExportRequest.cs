using System.Collections.Generic;

namespace BA.Core.Content.Models
{
    public sealed class ContentPreviewExportRequest
    {
        public List<string> FamilyPaths { get; set; } = new();
        public bool OverwriteExisting { get; set; } = true;
    }
}
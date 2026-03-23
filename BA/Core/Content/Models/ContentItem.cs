using System;
using System.Collections.Generic;

namespace BA.Core.Content.Models
{
    public sealed class ContentItem
    {
        public string Id { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string RootName { get; set; } = string.Empty;

        public string Extension { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string ApprovalState { get; set; } = string.Empty;

        public string PreviewPath { get; set; } = string.Empty;
        public string MetadataPath { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;

        public List<string> Tags { get; set; } = new();
        public List<string> Keywords { get; set; } = new();

        public DateTime CreatedUtc { get; set; }
        public DateTime ModifiedUtc { get; set; }
        public long FileSizeBytes { get; set; }

        public bool IsFavorite { get; set; }
        public DateTime? LastUsedUtc { get; set; }

        public string SearchBlob { get; set; } = string.Empty;
    }
}
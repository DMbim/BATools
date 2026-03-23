using System.Collections.Generic;
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace BA.Core.Content.Models
{
    public sealed class ContentBrowserSettings
    {
        public List<ContentLibraryRoot> Roots { get; set; } = new();
        public string CacheFolderPath { get; set; } = string.Empty;
        public bool IncludeRfa { get; set; } = true;
        public bool IncludeRvt { get; set; } = false;
        public bool IncludeImagePreviewPng { get; set; } = true;
        public bool IncludeImagePreviewJpg { get; set; } = true;
    }
}
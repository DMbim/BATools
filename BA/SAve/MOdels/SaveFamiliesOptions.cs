using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BA.Families.Models
{
    public enum OverwriteMode
    {
        Skip = 0,
        Overwrite = 1,
        AddSuffix = 2
    }

    public class SaveFamiliesOptions
    {
        public string OutputFolder { get; set; } = string.Empty;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public OverwriteMode OverwriteMode { get; set; } = OverwriteMode.Skip;

        public bool OrganizeByCategory { get; set; } = false;

        /// <summary>Names of families the user had checked on their last session.</summary>
        public List<string> LastSelectedFamilyNames { get; set; } = new();

        /// <summary>
        /// View name used for the thumbnail when saving .rfa files.
        /// Matched against View.Name inside each family document.
        /// Defaults to the standard default 3D view name.
        /// </summary>
        public string ThumbnailViewName { get; set; } = "{3D}";

        /// <summary>
        /// If true, passes Compact = true to SaveAsOptions, reducing file size.
        /// </summary>
        public bool CompactFile { get; set; } = false;
    }
}

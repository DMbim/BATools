using System.Collections.Generic;

namespace BA.Core.Content.Models
{
    /// <summary>
    /// Plain POCO matching the pattern actually used by ContentBrowserSettings
    /// (manual JSON load/save via a companion service), not AppSettingsBase,
    /// since AppSettingsBase's real shape was not available to verify.
    /// </summary>
    public sealed class LoadedFamilyBrowserSettings
    {
        /// <summary>
        /// Category name -> enabled. A category name absent from this
        /// dictionary is treated as enabled by default.
        /// </summary>
        public Dictionary<string, bool> CategoryFilter { get; set; } = new();
    }
}
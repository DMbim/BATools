// File: BA.Settings/SuperSelectorSettings.cs
using System.Collections.Generic;

namespace BA.Settings
{
    public sealed class SuperSelectorSettings : AppSettingsBase
    {
        // Stored under %AppData%\BA\SuperSelector\SuperSelectorSettings.json
        protected override string SubFolder => "SuperSelector";
        protected override string FileName => "SuperSelectorSettings.json";

        // Matched by category Name on load, not ElementId - this file is
        // shared across every project the user opens, and only built-in
        // category ids are guaranteed stable across documents.
        public List<string> FrequentCategoryNames { get; set; } = new();
        public List<string> CheckedCategoryNames { get; set; } = new();
    }
}
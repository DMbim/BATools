using System.Collections.Generic;

namespace BA_Tools.ScheduleExporter.Models
{
    /// <summary>
    /// Schedule-level metadata captured at export time.
    /// Written to the Excel file in an info block below the data.
    /// On import, filter/sort info is informational only — element matching
    /// is always by ElementId, so filtered-out elements are implicitly excluded.
    /// </summary>
    public class ScheduleExportContext
    {
        public string ScheduleName  { get; set; }
        public string ExportedAt    { get; set; }
        public bool   IsItemized    { get; set; }
        public int    TotalElements { get; set; }

        /// <summary>Human-readable filter descriptions, e.g. "Family = \"BA_Door\""</summary>
        public List<string> FilterDescriptions { get; set; } = new List<string>();

        /// <summary>Human-readable sort descriptions, e.g. "Family ↑", "Type ↓"</summary>
        public List<string> SortDescriptions { get; set; } = new List<string>();

        public string FiltersDisplay =>
            FilterDescriptions.Count > 0
                ? string.Join("   |   ", FilterDescriptions)
                : "None";

        public string SortDisplay =>
            SortDescriptions.Count > 0
                ? string.Join(",   ", SortDescriptions)
                : "None";
    }
}

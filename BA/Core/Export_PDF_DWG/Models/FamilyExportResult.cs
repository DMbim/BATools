using System.Collections.Generic;
using System.Linq;

namespace BA.Core.Export.Models
{
    public class FamilyExportOutcome
    {
        public string FamilyName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;

        public bool Skipped { get; set; }
        public string SkippedReason { get; set; } = string.Empty;

        public bool RfaSuccess { get; set; }
        public string RfaPath { get; set; } = string.Empty;
        public string RfaErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// Only meaningful when ExportPreviewImage was requested. A family
        /// can have RfaSuccess true and ImageAttempted true with
        /// ImageSuccess false, that's not a failed export overall, it just
        /// means none of the preferred view names existed in that family.
        /// </summary>
        public bool ImageAttempted { get; set; }
        public bool ImageSuccess { get; set; }
        public string ImagePath { get; set; } = string.Empty;
        public string ImageErrorMessage { get; set; } = string.Empty;
    }

    public class FamilyExportResult
    {
        public List<FamilyExportOutcome> Outcomes { get; } = new List<FamilyExportOutcome>();

        public int SuccessCount => Outcomes.Count(o => !o.Skipped && o.RfaSuccess);
        public int FailureCount => Outcomes.Count(o => !o.Skipped && !o.RfaSuccess);
        public int SkippedCount => Outcomes.Count(o => o.Skipped);
    }
}

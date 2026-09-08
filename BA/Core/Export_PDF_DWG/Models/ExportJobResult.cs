using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.Core.Export.Models
{
    public class ExportJobResult
    {
        public Guid JobId { get; set; }
        public string JobName { get; set; } = string.Empty;
        public ExportFormat Format { get; set; }
        public DateTime RunTimestamp { get; set; }
        public string JobLevelError { get; set; } = string.Empty;
        public List<SheetExportOutcome> Outcomes { get; } = new List<SheetExportOutcome>();

        /// <summary>
        /// Populated only on the first result a given RunJob() call
        /// returns for a job, one bump happens once per job run, not once
        /// per format, so a job with both PDF and DWG enabled does not
        /// show this twice. Empty when BumpDateRevisionOnRun is false,
        /// SourceMode is Views, or the job had no sheets to bump.
        /// </summary>
        public string DateRevisionBumpSummary { get; set; } = string.Empty;

        public bool HasJobLevelError => !string.IsNullOrEmpty(JobLevelError);
        public int SuccessCount => Outcomes.Count(o => o.Success);
        public int FailureCount => Outcomes.Count(o => !o.Success);
    }
}
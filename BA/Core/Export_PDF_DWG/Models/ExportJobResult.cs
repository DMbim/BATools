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

        public bool HasJobLevelError => !string.IsNullOrEmpty(JobLevelError);
        public int SuccessCount => Outcomes.Count(o => o.Success);
        public int FailureCount => Outcomes.Count(o => !o.Success);
    }
}

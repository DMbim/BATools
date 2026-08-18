// File: BA_Tools/CadPurge/Models/PurgeBatchResult.cs
using System;
using System.Collections.Generic;

namespace BA.CadPurge.Models
{
    /// <summary>
    /// Outcome of PurgeBatchExecutor.ExecuteBatch. Candidates carry their own per-item
    /// Status/StatusDetail; this class holds the batch-level rollup and any warnings Revit raised
    /// that were auto-resolved during the run (see CadPurgeFailuresPreprocessor).
    /// </summary>
    public sealed class PurgeBatchResult
    {
        public int Succeeded { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        public List<string> Warnings { get; } = new();
        public List<PurgeCandidate> Candidates { get; }

        public PurgeBatchResult(List<PurgeCandidate> candidates)
        {
            Candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
        }
    }
}
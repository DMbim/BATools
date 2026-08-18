// File: BA_Tools/CadPurge/Services/CadPurgeFailuresPreprocessor.cs
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace BA.CadPurge.Services
{
    /// <summary>
    /// Auto-dismisses Warning-severity failures raised during a CAD Purge batch Transaction, for
    /// example a stray view-specific graphic override that referenced a deleted LinePatternElement.
    /// Their text is recorded so PurgeBatchExecutor can attribute them to the candidate whose
    /// action raised them. Error-severity failures are deliberately left untouched so Revit's
    /// default handling (rolling back the failing operation) still applies. This class never
    /// attempts to auto-resolve a hard failure.
    /// </summary>
    public sealed class CadPurgeFailuresPreprocessor : IFailuresPreprocessor
    {
        public List<string> ResolvedWarnings { get; } = new();

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            IList<FailureMessageAccessor> failures = failuresAccessor.GetFailureMessages();

            foreach (FailureMessageAccessor failure in failures)
            {
                if (failure.GetSeverity() == FailureSeverity.Warning)
                {
                    ResolvedWarnings.Add(failure.GetDescriptionText());
                    failuresAccessor.DeleteWarning(failure);
                }
            }

            return FailureProcessingResult.Continue;
        }
    }
}
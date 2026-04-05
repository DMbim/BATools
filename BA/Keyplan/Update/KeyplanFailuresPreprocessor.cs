using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace BA.Keyplan
{
    public sealed class KeyplanFailuresPreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            IList<FailureMessageAccessor> failures = failuresAccessor.GetFailureMessages();
            if (failures == null || failures.Count == 0)
                return FailureProcessingResult.Continue;

            foreach (FailureMessageAccessor f in failures)
            {
                if (f == null) continue;

                if (f.GetSeverity() == FailureSeverity.Warning)
                    failuresAccessor.DeleteWarning(f);
            }

            return FailureProcessingResult.Continue;
        }
    }
}
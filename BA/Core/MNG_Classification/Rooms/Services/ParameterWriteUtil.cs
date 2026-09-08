using Autodesk.Revit.DB;
using System;
using System.Linq;
using CoreParameterUtils = BA.Core.Classification.ParameterUtils;

namespace BA.RoomClassification.Services
{
    // Thin wrapper around the shared BA.Core.Classification.ParameterUtils, so Room Classification
    // does not keep a second, independently-behaving copy of parameter lookup/read/write logic.
    // The actual LookupParameter / StorageType handling now lives in exactly one place
    // (BA.Core.Classification.ParameterUtils, already used by the BA.Classification engine).
    //
    // This class exists only to add the "throw loudly on failure" contract that
    // RoomClassificationSyncService depends on. That is deliberately different from
    // ParameterUtils.SetString's own contract (returns false silently on failure), which the
    // BA.Classification engine relies on for its own report-counter-based skip semantics -
    // changing that contract was out of scope here and would have risked changing behavior for
    // code that already works and does not belong to this module.
    //
    // GetParam's fallback to element.GetParameters(name).FirstOrDefault() is kept here rather than
    // added to the shared ParameterUtils.GetParam, for the same reason: ParameterUtils.GetParam is
    // already used as-is (LookupParameter only) throughout RuleEvaluator/ClassificationEngine, and
    // broadening its behavior there was not asked for and was not needed to fix anything.
    internal static class ParameterWriteUtil
    {
        public static string GetString(Element element, string parameterName)
        {
            Parameter p = FindParameter(element, parameterName);
            return CoreParameterUtils.GetString(p);
        }

        public static void SetString(Element element, string parameterName, string value)
        {
            Parameter p = FindParameter(element, parameterName)
                ?? throw new InvalidOperationException(
                    $"Parameter '{parameterName}' was not found on element {element.Id.Value}.");

            if (p.IsReadOnly)
                throw new InvalidOperationException(
                    $"Parameter '{parameterName}' is read-only on element {element.Id.Value}.");

            if (p.StorageType != StorageType.String)
                throw new InvalidOperationException(
                    $"Parameter '{parameterName}' is not a text parameter on element {element.Id.Value}.");

            if (!CoreParameterUtils.SetString(p, value))
                throw new InvalidOperationException(
                    $"Failed to set parameter '{parameterName}' on element {element.Id.Value}.");
        }

        private static Parameter FindParameter(Element element, string parameterName)
        {
            Parameter p = CoreParameterUtils.GetParam(element, parameterName);
            if (p != null) return p;
            return element.GetParameters(parameterName).FirstOrDefault();
        }
    }
}

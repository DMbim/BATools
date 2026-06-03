using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BA.Core.AreaSchemes.Constants;
using BA.Core.AreaSchemes.Models;

namespace BA.Core.AreaSchemes.Services
{
    public static class AreaValueService
    {
        /// <summary>
        /// Reads the total area in m² from all Area elements in a view.
        /// Returns 0 if the view doesn't exist or has no areas.
        /// </summary>
        public static double ReadTotalAreaM2(Document doc, ViewPlan? view)
        {
            if (view == null) return 0;

            return new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(Area))
                .Cast<Area>()
                .Where(a => a.Area > 0)
                .Sum(a => UnitUtils.ConvertFromInternalUnits(
                    a.Area, UnitTypeId.SquareMeters));
        }

        /// <summary>
        /// Computes the full area chain for a level from the individual view areas.
        /// </summary>
        public static AreaSchemeResult ComputeResult(
            Document doc,
            Level level,
            Dictionary<string, ViewPlan?> viewsByScheme)
        {
            var result = new AreaSchemeResult { Level = level };

            result.LA = ReadTotalAreaM2(doc,
                viewsByScheme.GetValueOrDefault(AreaSchemeConstants.LA));
            result.NLA = ReadTotalAreaM2(doc,
                viewsByScheme.GetValueOrDefault(AreaSchemeConstants.NLA));
            result.ECA = ReadTotalAreaM2(doc,
                viewsByScheme.GetValueOrDefault(AreaSchemeConstants.ECA));
            result.ICA = ReadTotalAreaM2(doc,
                viewsByScheme.GetValueOrDefault(AreaSchemeConstants.ICA));
            result.PWA = ReadTotalAreaM2(doc,
                viewsByScheme.GetValueOrDefault(AreaSchemeConstants.PWA));

            // Computed values
            result.Compute();

            return result;
        }

        /// <summary>
        /// Writes the computed result to shared parameters on the Level element.
        /// Must be called inside an open Transaction.
        /// </summary>
        public static void WriteToLevel(Document doc, AreaSchemeResult result)
        {
            var level = result.Level;

            SetAreaParam(level, AreaSchemeConstants.ParamLA, result.LA);
            SetAreaParam(level, AreaSchemeConstants.ParamNLA, result.NLA);
            SetAreaParam(level, AreaSchemeConstants.ParamGFA, result.GFA);
            SetAreaParam(level, AreaSchemeConstants.ParamECA, result.ECA);
            SetAreaParam(level, AreaSchemeConstants.ParamIFA, result.IFA);
            SetAreaParam(level, AreaSchemeConstants.ParamICA, result.ICA);
            SetAreaParam(level, AreaSchemeConstants.ParamNFA, result.NFA);
            SetAreaParam(level, AreaSchemeConstants.ParamPWA, result.PWA);
            SetAreaParam(level, AreaSchemeConstants.ParamNRA, result.NRA);
        }

        private static void SetAreaParam(Level level, string paramName, double valueM2)
        {
            var param = level.LookupParameter(paramName);
            if (param == null || param.IsReadOnly) return;

            if (param.StorageType == StorageType.Double)
            {
                param.Set(UnitUtils.ConvertToInternalUnits(
                    valueM2, UnitTypeId.SquareMeters));
            }
        }
    }
}
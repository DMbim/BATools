using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace BA.UI.KeyplanGrid
{
    /// <summary>
    /// Writes KeyplanZoneAssignment values into Revit instance parameters.
    /// Must be called inside an open Revit Transaction.
    /// Does not open or commit its own transaction.
    /// </summary>
    public static class KeyplanZoneParameterWriter
    {
        /// <summary>
        /// Writes all assignments to their respective FilledRegion elements.
        /// Caller is responsible for wrapping this in a Transaction.
        /// </summary>
        public static ZoneWriteResult WriteAssignments(
            Document doc,
            IReadOnlyList<KeyplanZoneAssignment> assignments)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            ZoneWriteResult result = new ZoneWriteResult();

            if (assignments == null || assignments.Count == 0)
                return result;

            foreach (KeyplanZoneAssignment assignment in assignments)
            {
                if (assignment == null)
                    continue;

                try
                {
                    WriteOne(doc, assignment, result);
                }
                catch (Exception ex)
                {
                    result.Errors.Add(
                        $"Unhandled exception for '{assignment.ParameterName}' " +
                        $"on element '{assignment.RegionUniqueId}': {ex.Message}");
                    result.Skipped++;
                }
            }

            return result;
        }

        // -------------------------------------------------------------------------
        // Private
        // -------------------------------------------------------------------------

        private static void WriteOne(
            Document doc,
            KeyplanZoneAssignment assignment,
            ZoneWriteResult result)
        {
            if (string.IsNullOrWhiteSpace(assignment.RegionUniqueId))
            {
                result.Errors.Add(
                    $"Assignment for '{assignment.ParameterName}' has no RegionUniqueId. Skipped.");
                result.Skipped++;
                return;
            }

            Element element = doc.GetElement(assignment.RegionUniqueId);

            if (element == null)
            {
                result.Errors.Add(
                    $"Element with UniqueId '{assignment.RegionUniqueId}' not found. " +
                    $"Parameter '{assignment.ParameterName}' not written.");
                result.Skipped++;
                return;
            }

            if (string.IsNullOrWhiteSpace(assignment.ParameterName))
            {
                result.Errors.Add(
                    $"Assignment on element '{assignment.RegionUniqueId}' has no parameter name. Skipped.");
                result.Skipped++;
                return;
            }

            Parameter param = element.LookupParameter(assignment.ParameterName);

            if (param == null)
            {
                result.MissingParameters.Add(
                    $"'{assignment.ParameterName}' not found on element '{assignment.RegionUniqueId}'.");
                result.Skipped++;
                return;
            }

            if (param.IsReadOnly)
            {
                result.ReadOnlyParameters.Add(
                    $"'{assignment.ParameterName}' is read-only on element '{assignment.RegionUniqueId}'.");
                result.Skipped++;
                return;
            }

            if (param.StorageType != StorageType.String)
            {
                result.Errors.Add(
                    $"'{assignment.ParameterName}' on element '{assignment.RegionUniqueId}' " +
                    $"has StorageType {param.StorageType}, expected String.");
                result.Skipped++;
                return;
            }

            param.Set(assignment.Label ?? string.Empty);
            result.Written++;
        }
    }
}

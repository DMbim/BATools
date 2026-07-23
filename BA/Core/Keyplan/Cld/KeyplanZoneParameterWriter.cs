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
        public const string ZoneParameterName = "BA.Tls_Zone";

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

        /// <summary>
        /// Clears BA.Tls_Zone (sets to empty string) on all given FilledRegion
        /// elements, identified by UniqueId. Used before re-applying a new
        /// zone label session so stale labels from a previous session don't
        /// persist on regions no longer included in the new sequence.
        /// Caller is responsible for wrapping this in a Transaction.
        /// Elements that cannot be found, have no such parameter, or have a
        /// read-only parameter are silently skipped — this is a best-effort
        /// cleanup, not a user-facing operation with its own result reporting.
        /// </summary>
        public static void ClearZoneLabels(Document doc, IEnumerable<string> regionUniqueIds)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (regionUniqueIds == null)
                return;

            foreach (string uniqueId in regionUniqueIds)
            {
                if (string.IsNullOrWhiteSpace(uniqueId))
                    continue;

                Element element = doc.GetElement(uniqueId);
                if (element == null)
                    continue;

                Parameter param = element.LookupParameter(ZoneParameterName);
                if (param == null || param.IsReadOnly || param.StorageType != StorageType.String)
                    continue;

                // Avoid unnecessary writes if already empty.
                string current = param.AsString();
                if (!string.IsNullOrEmpty(current))
                    param.Set(string.Empty);
            }
        }

        // -------------------------------------------------------------------------
        // Private
        // -------------------------------------------------------------------------
        public static bool EnsureZoneParameterBound(Document doc, out string error)
        {
            error = string.Empty;

            if (doc == null) throw new ArgumentNullException(nameof(doc));

            // Already bound? Check the binding map by definition name.
            DefinitionBindingMapIterator it = doc.ParameterBindings.ForwardIterator();
            while (it.MoveNext())
            {
                if (it.Key is Definition existing &&
                    string.Equals(existing.Name, ZoneParameterName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            Autodesk.Revit.ApplicationServices.Application app = doc.Application;

            DefinitionFile defFile;
            try
            {
                defFile = app.OpenSharedParameterFile();
            }
            catch (Exception ex)
            {
                error = "Could not open the shared parameter file: " + ex.Message;
                return false;
            }

            if (defFile == null)
            {
                error = "No shared parameter file is configured in Revit " +
                        "(Manage > Shared Parameters). Point Revit at the BA shared " +
                        "parameter file and try again.";
                return false;
            }

            ExternalDefinition definition = defFile.Groups
                .SelectMany(g => g.Definitions)
                .OfType<ExternalDefinition>()
                .FirstOrDefault(d => string.Equals(d.Name, ZoneParameterName, StringComparison.Ordinal));

            if (definition == null)
            {
                error = $"The shared parameter '{ZoneParameterName}' was not found in the " +
                        $"configured shared parameter file ('{defFile.Filename}'). " +
                        "Add it to the BA shared parameter file (Text type) first.";
                return false;
            }

            Category detailItems = doc.Settings.Categories.get_Item(BuiltInCategory.OST_DetailComponents);
            if (detailItems == null)
            {
                error = "Detail Items category not found in this document.";
                return false;
            }

            CategorySet categories = app.Create.NewCategorySet();
            categories.Insert(detailItems);

            InstanceBinding binding = app.Create.NewInstanceBinding(categories);

            bool inserted = doc.ParameterBindings.Insert(definition, binding, GroupTypeId.IdentityData);

            if (!inserted)
                inserted = doc.ParameterBindings.ReInsert(definition, binding, GroupTypeId.IdentityData);

            if (!inserted)
            {
                error = $"Failed to bind '{ZoneParameterName}' to the Detail Items category.";
                return false;
            }

            return true;
        }
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
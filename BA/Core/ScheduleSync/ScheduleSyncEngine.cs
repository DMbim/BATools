using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA
{
    public static class ScheduleSyncEngine
    {
        /// <summary>
        /// Called from IExternalEventHandler.Execute — Revit API thread only.
        /// Reads SourceColumn parameter value from each element and writes it
        /// to DestinationParameter on the same element.
        /// </summary>
        public static string Execute(
            Document doc,
            ViewSchedule schedule,
            List<ScheduleMappingRow> mappings)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (schedule == null) throw new ArgumentNullException(nameof(schedule));
            if (mappings == null || mappings.Count == 0)
                return "No mappings configured.";

            // Strip incomplete mappings up front so we can report them.
            var validMappings = mappings
                .Where(m => !string.IsNullOrWhiteSpace(m.SourceColumn)
                         && !string.IsNullOrWhiteSpace(m.DestinationParameter))
                .ToList();

            if (validMappings.Count == 0)
                return "All mappings are incomplete (missing source or destination).";

            // Collect elements that appear in this schedule.
            // The schedule's category drives the collector.
            ElementId categoryId = schedule.Definition.CategoryId;

            List<Element> elements;
            try
            {
                elements = new FilteredElementCollector(doc, schedule.Id)
                    .WhereElementIsNotElementType()
                    .ToList();
            }
            catch
            {
                // Fallback: collect by category if schedule-scoped collector fails
                // (e.g. multi-category schedules).
                elements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(new ElementCategoryFilter(categoryId))
                    .ToList();
            }

            if (elements.Count == 0)
                return "No elements found in schedule.";

            int ok = 0;
            int skipped = 0;
            var errors = new List<string>();

            using (var tx = new Transaction(doc, "BA Schedule Sync"))
            {
                tx.Start();

                foreach (var el in elements)
                {
                    foreach (var mapping in validMappings)
                    {
                        try
                        {
                            Parameter src = el.LookupParameter(mapping.SourceColumn);
                            if (src == null) { skipped++; continue; }

                            Parameter dest = el.LookupParameter(mapping.DestinationParameter);
                            if (dest == null || dest.IsReadOnly) { skipped++; continue; }

                            bool written = CopyParameter(src, dest);
                            if (written) ok++;
                            else skipped++;
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"El {el.Id}: {ex.Message}");
                        }
                    }
                }

                tx.Commit();
            }

            var msg = $"Sync complete. Written: {ok}, Skipped: {skipped}";
            if (errors.Count > 0)
                msg += $" | Errors: {errors.Count} (first: {errors[0]})";

            return msg;
        }

        /// <summary>
        /// Copies a parameter value from src to dest.
        /// Handles StorageType mismatch between string and numeric gracefully.
        /// Returns true if the write succeeded.
        /// </summary>
        private static bool CopyParameter(Parameter src, Parameter dest)
        {
            // Same storage type — direct copy.
            if (src.StorageType == dest.StorageType)
            {
                switch (src.StorageType)
                {
                    case StorageType.String:
                        dest.Set(src.AsString() ?? "");
                        return true;

                    case StorageType.Double:
                        dest.Set(src.AsDouble());
                        return true;

                    case StorageType.Integer:
                        dest.Set(src.AsInteger());
                        return true;

                    case StorageType.ElementId:
                        dest.Set(src.AsElementId());
                        return true;

                    default:
                        return false;
                }
            }

            // Cross-type: source is string, dest is numeric — try parse.
            if (src.StorageType == StorageType.String)
            {
                var raw = src.AsString() ?? "";

                if (dest.StorageType == StorageType.Double
                    && double.TryParse(raw, System.Globalization.NumberStyles.Any,
                                       System.Globalization.CultureInfo.InvariantCulture, out double d))
                {
                    dest.Set(d);
                    return true;
                }

                if (dest.StorageType == StorageType.Integer
                    && int.TryParse(raw, out int i))
                {
                    dest.Set(i);
                    return true;
                }

                return false;
            }

            // Cross-type: numeric source to string dest.
            if (dest.StorageType == StorageType.String)
            {
                switch (src.StorageType)
                {
                    case StorageType.Double:
                        dest.Set(src.AsDouble().ToString(
                            System.Globalization.CultureInfo.InvariantCulture));
                        return true;

                    case StorageType.Integer:
                        dest.Set(src.AsInteger().ToString());
                        return true;
                }
            }

            return false;
        }
    }
}
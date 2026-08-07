using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BA.Core.Export.Models;

namespace BA.Core.Export.Services
{
    /// <summary>
    /// Resolves formatted parameter values for a set of columns across a
    /// set of sheets, for populating the dynamic columns in
    /// SheetPickerWindow. Must be called from a valid Revit API thread
    /// context, never directly from WPF UI code.
    ///
    /// Lookup order per column: BuiltIn by BuiltInParameter id directly on
    /// the sheet, Shared by Element.get_Parameter(Guid) once the Guid is
    /// confirmed against a SharedParameterElement, Project by
    /// LookupParameter name as a fallback, same discipline
    /// NamingTemplateEngine already uses. A parameter missing on a given
    /// sheet resolves to an empty string rather than throwing, per-sheet
    /// binding differences are expected, not exceptional.
    /// </summary>
    public static class ParameterColumnValueService
    {
        /// <returns>sheetNumber -> (columnKey -> formatted value)</returns>
        public static Dictionary<string, Dictionary<string, string>> ResolveValues(
            Document doc,
            IList<string> sheetNumbers,
            IList<ParameterColumnDescriptor> columns)
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            if (sheetNumbers == null || sheetNumbers.Count == 0 || columns == null || columns.Count == 0)
            {
                return result;
            }

            var sheets = ResolveSheets(doc, sheetNumbers);

            var sharedGuids = columns
                .Where(c => c.Source == ParameterColumnSource.Shared && c.SharedParamGuid.HasValue)
                .Select(c => c.SharedParamGuid.Value)
                .Distinct();

            // One collector pass total for every shared column, not one
            // pass per guid, matters once there are several sheets times
            // several shared columns.
            var sharedGuidLookup = BuildSharedParameterLookup(doc, sharedGuids);

            foreach (var sheet in sheets)
            {
                var rowValues = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (var column in columns)
                {
                    rowValues[column.ColumnKey] = ResolveOneValue(sheet, column, sharedGuidLookup);
                }

                result[sheet.SheetNumber ?? string.Empty] = rowValues;
            }

            return result;
        }

        private static string ResolveOneValue(
            ViewSheet sheet,
            ParameterColumnDescriptor column,
            Dictionary<Guid, SharedParameterElement> sharedGuidLookup)
        {
            Parameter parameter = null;

            switch (column.Source)
            {
                case ParameterColumnSource.BuiltIn:
                    if (column.BuiltInParameterId.HasValue)
                    {
                        parameter = sheet.get_Parameter(column.BuiltInParameterId.Value);
                    }
                    break;

                case ParameterColumnSource.Shared:
                    if (column.SharedParamGuid.HasValue &&
                        sharedGuidLookup.TryGetValue(column.SharedParamGuid.Value, out var spElement) &&
                        spElement != null)
                    {
                        parameter = sheet.get_Parameter(spElement.GuidValue);
                    }
                    break;

                case ParameterColumnSource.Project:
                    parameter = sheet.LookupParameter(column.ProjectParameterName);
                    break;
            }

            if (parameter == null || !parameter.HasValue)
            {
                return string.Empty;
            }

            return FormatValue(parameter);
        }

        private static string FormatValue(Parameter parameter)
        {
            switch (parameter.StorageType)
            {
                case StorageType.String:
                    return parameter.AsString() ?? string.Empty;

                case StorageType.Integer:
                    return parameter.AsValueString() ?? parameter.AsInteger().ToString();

                case StorageType.Double:
                    return parameter.AsValueString() ?? parameter.AsDouble().ToString("0.##");

                case StorageType.ElementId:
                    return parameter.AsValueString() ?? parameter.AsElementId().ToString();

                default:
                    return string.Empty;
            }
        }

        private static Dictionary<Guid, SharedParameterElement> BuildSharedParameterLookup(Document doc, IEnumerable<Guid> guids)
        {
            var wanted = new HashSet<Guid>(guids);
            var lookup = new Dictionary<Guid, SharedParameterElement>();

            if (wanted.Count == 0)
            {
                return lookup;
            }

            foreach (var element in new FilteredElementCollector(doc).OfClass(typeof(SharedParameterElement)))
            {
                if (element is SharedParameterElement spElement && wanted.Contains(spElement.GuidValue))
                {
                    lookup[spElement.GuidValue] = spElement;
                }
            }

            return lookup;
        }

        private static List<ViewSheet> ResolveSheets(Document doc, IList<string> sheetNumbers)
        {
            var wanted = new HashSet<string>(sheetNumbers, StringComparer.OrdinalIgnoreCase);

            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Sheets)
                .WhereElementIsNotElementType()
                .OfType<ViewSheet>()
                .Where(s => wanted.Contains(s.SheetNumber ?? string.Empty))
                .ToList();
        }
    }
}

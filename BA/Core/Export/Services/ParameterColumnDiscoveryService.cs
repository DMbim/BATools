using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BA.Core.Export.Models;

namespace BA.Core.Export.Services
{
    /// <summary>
    /// Scans a set of sheets and aggregates the distinct parameters found
    /// across them into ParameterColumnCandidate entries for the Add
    /// Parameter Column picker. Must be called from a valid Revit API
    /// thread context, never directly from WPF UI code.
    ///
    /// Shared parameters are identified through Parameter.IsShared and
    /// Parameter.GUID directly on the instance, not by iterating
    /// doc.ParameterBindings for ExternalDefinition casts, that iterator is
    /// confirmed not to yield them correctly in this codebase. Built-in
    /// parameters are identified through InternalDefinition.BuiltInParameter.
    /// Everything else is treated as a Project Parameter identified by name.
    ///
    /// Only instance parameters are covered in this pass, ViewSheet does
    /// not expose a type to source type parameters from without also
    /// resolving the placed title block, that is a separate piece of work
    /// shared with paper size detection, not duplicated here.
    /// </summary>
    public static class ParameterColumnDiscoveryService
    {
        public static List<ParameterColumnCandidate> DiscoverColumns(Document doc, IList<string> sheetNumbers)
        {
            var sheets = ResolveSheets(doc, sheetNumbers);

            if (sheets.Count == 0)
            {
                return new List<ParameterColumnCandidate>();
            }

            var found = new Dictionary<string, (ParameterColumnDescriptor Descriptor, int Count)>(StringComparer.Ordinal);

            foreach (var sheet in sheets)
            {
                foreach (Parameter p in sheet.Parameters)
                {
                    var descriptor = BuildDescriptor(p);

                    if (descriptor == null)
                    {
                        continue;
                    }

                    var key = descriptor.ColumnKey;

                    if (found.TryGetValue(key, out var existing))
                    {
                        found[key] = (existing.Descriptor, existing.Count + 1);
                    }
                    else
                    {
                        found[key] = (descriptor, 1);
                    }
                }
            }

            var totalSheets = sheets.Count;

            return found.Values
                .Select(f => new ParameterColumnCandidate
                {
                    DisplayName = f.Descriptor.DisplayName,
                    Source = f.Descriptor.Source,
                    IsInstance = f.Descriptor.IsInstance,
                    ValueKind = f.Descriptor.ValueKind,
                    BuiltInParameterId = f.Descriptor.BuiltInParameterId,
                    SharedParamGuid = f.Descriptor.SharedParamGuid,
                    ProjectParameterName = f.Descriptor.ProjectParameterName,
                    Occurrence = f.Count == totalSheets
                        ? ParameterColumnOccurrence.All
                        : f.Count == 1
                            ? ParameterColumnOccurrence.One
                            : ParameterColumnOccurrence.Some
                })
                .OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static ParameterColumnDescriptor BuildDescriptor(Parameter p)
        {
            var definition = p.Definition;

            if (definition == null || string.IsNullOrWhiteSpace(definition.Name))
            {
                return null;
            }

            var valueKind = MapValueKind(p.StorageType, definition);

            if (p.IsShared)
            {
                return new ParameterColumnDescriptor
                {
                    DisplayName = definition.Name,
                    Source = ParameterColumnSource.Shared,
                    IsInstance = true,
                    ValueKind = valueKind,
                    SharedParamGuid = p.GUID
                };
            }

            if (definition is InternalDefinition internalDefinition &&
                internalDefinition.BuiltInParameter != BuiltInParameter.INVALID)
            {
                return new ParameterColumnDescriptor
                {
                    DisplayName = definition.Name,
                    Source = ParameterColumnSource.BuiltIn,
                    IsInstance = true,
                    ValueKind = valueKind,
                    BuiltInParameterId = internalDefinition.BuiltInParameter
                };
            }

            return new ParameterColumnDescriptor
            {
                DisplayName = definition.Name,
                Source = ParameterColumnSource.Project,
                IsInstance = true,
                ValueKind = valueKind,
                ProjectParameterName = definition.Name
            };
        }

        private static ParameterValueKind MapValueKind(StorageType storageType, Definition definition)
        {
            switch (storageType)
            {
                case StorageType.String:
                    return ParameterValueKind.Text;
                case StorageType.Integer:
                    return IsYesNo(definition) ? ParameterValueKind.YesNo : ParameterValueKind.Integer;
                case StorageType.Double:
                    return ParameterValueKind.Number;
                case StorageType.ElementId:
                    return ParameterValueKind.ElementReference;
                default:
                    return ParameterValueKind.Unsupported;
            }
        }

        private static bool IsYesNo(Definition definition)
        {
            try
            {
                var specId = definition.GetDataType();
                return specId != null && specId.Equals(SpecTypeId.Boolean.YesNo);
            }
            catch
            {
                return false;
            }
        }

        private static List<ViewSheet> ResolveSheets(Document doc, IList<string> sheetNumbers)
        {
            if (sheetNumbers == null || sheetNumbers.Count == 0)
            {
                return new List<ViewSheet>();
            }

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

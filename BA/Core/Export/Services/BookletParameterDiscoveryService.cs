using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BA.Core.Export.Models;

namespace BA.Core.Export.Services
{
    /// <summary>
    /// Discovers distinct parameters across a set of element types, for
    /// the booklet info field picker. Mirrors ParameterColumnDiscoveryService
    /// exactly, same identity rules (BuiltIn by BuiltInParameter, Shared by
    /// GUID, everything else by name), just scanning ElementType.Parameters
    /// instead of ViewSheet.Parameters, since the info block is per type,
    /// not per sheet. Must be called from a valid Revit API thread context.
    /// </summary>
    public static class BookletParameterDiscoveryService
    {
        public static List<ParameterColumnCandidate> DiscoverColumns(Document doc, IList<string> typeUniqueIds)
        {
            var types = ResolveTypes(doc, typeUniqueIds);

            if (types.Count == 0)
            {
                return new List<ParameterColumnCandidate>();
            }

            var found = new Dictionary<string, (ParameterColumnDescriptor Descriptor, int Count)>(StringComparer.Ordinal);

            foreach (var type in types)
            {
                foreach (Parameter p in type.Parameters)
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

            var totalTypes = types.Count;

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
                    Occurrence = f.Count == totalTypes
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
                    IsInstance = false,
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
                    IsInstance = false,
                    ValueKind = valueKind,
                    BuiltInParameterId = internalDefinition.BuiltInParameter
                };
            }

            return new ParameterColumnDescriptor
            {
                DisplayName = definition.Name,
                Source = ParameterColumnSource.Project,
                IsInstance = false,
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

        private static List<ElementType> ResolveTypes(Document doc, IList<string> typeUniqueIds)
        {
            var result = new List<ElementType>();

            if (typeUniqueIds == null)
            {
                return result;
            }

            foreach (var uniqueId in typeUniqueIds)
            {
                if (doc.GetElement(uniqueId) is ElementType type)
                {
                    result.Add(type);
                }
            }

            return result;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BA.Core.Export.Models;

namespace BA.Core.Export.Services
{
    /// <summary>
    /// Lists types available for booklet generation, either every type in
    /// a given Revit category, or every type carrying a non-empty value on
    /// a given parameter name, and marks which ones have at least one
    /// placed instance somewhere in the model, since a booklet needs a
    /// real instance to cut an elevation and section through. Must be
    /// called from a valid Revit API thread context.
    /// </summary>
    public static class BookletTypeDiscoveryService
    {
        public static List<BookletTypeInfo> GetTypesByCategory(Document doc, BuiltInCategory category)
        {
            var types = new FilteredElementCollector(doc)
                .OfCategory(category)
                .WhereElementIsElementType()
                .Cast<ElementType>()
                .ToList();

            var placedFamilyNames = GetFamilyNamesWithPlacedInstances(doc, category);

            return types
                .Select(t => BuildInfo(t, placedFamilyNames))
                .OrderBy(t => t.FamilyName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.TypeName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Every element type in the document carrying a non-empty value
        /// on the named parameter, regardless of category. Placed instance
        /// detection here checks instances of that specific type directly
        /// by type id, since categories aren't known up front in this mode.
        /// </summary>
        public static List<BookletTypeInfo> GetTypesByParameterValue(Document doc, string parameterName)
        {
            var allTypes = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .ToElements();

            var matching = new List<BookletTypeInfo>();

            foreach (var element in allTypes)
            {
                var parameter = element.LookupParameter(parameterName);

                if (parameter == null || !parameter.HasValue)
                {
                    continue;
                }

                var valueText = FormatParameterValue(parameter);

                if (string.IsNullOrWhiteSpace(valueText))
                {
                    continue;
                }

                if (!(element is ElementType elementType))
                {
                    continue;
                }

                var hasInstance = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e => e.GetTypeId() == elementType.Id)
                    .Any();

                matching.Add(new BookletTypeInfo
                {
                    UniqueId = elementType.UniqueId,
                    FamilyName = (elementType as FamilySymbol)?.Family?.Name ?? string.Empty,
                    TypeName = elementType.Name ?? string.Empty,
                    CategoryName = elementType.Category?.Name ?? string.Empty,
                    HasPlacedInstance = hasInstance
                });
            }

            return matching
                .OrderBy(t => t.CategoryName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.FamilyName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.TypeName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static BookletTypeInfo BuildInfo(ElementType type, HashSet<ElementId> placedTypeIds)
        {
            return new BookletTypeInfo
            {
                UniqueId = type.UniqueId,
                FamilyName = (type as FamilySymbol)?.Family?.Name ?? string.Empty,
                TypeName = type.Name ?? string.Empty,
                CategoryName = type.Category?.Name ?? string.Empty,
                HasPlacedInstance = placedTypeIds.Contains(type.Id)
            };
        }

        private static HashSet<ElementId> GetFamilyNamesWithPlacedInstances(Document doc, BuiltInCategory category)
        {
            var instanceTypeIds = new FilteredElementCollector(doc)
                .OfCategory(category)
                .WhereElementIsNotElementType()
                .Select(e => e.GetTypeId())
                .Where(id => id != ElementId.InvalidElementId);

            return new HashSet<ElementId>(instanceTypeIds);
        }

        private static string FormatParameterValue(Parameter parameter)
        {
            switch (parameter.StorageType)
            {
                case StorageType.String:
                    return parameter.AsString();
                case StorageType.Integer:
                    return parameter.AsValueString() ?? parameter.AsInteger().ToString();
                case StorageType.Double:
                    return parameter.AsValueString() ?? parameter.AsDouble().ToString("0.##");
                case StorageType.ElementId:
                    return parameter.AsValueString();
                default:
                    return null;
            }
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BA.Core.Export.Models;

namespace BA.Core.Export.Services
{
    /// <summary>
    /// Lists loadable families in the document for the family export
    /// picker. Must be called from a valid Revit API thread context.
    /// </summary>
    public static class FamilyDiscoveryService
    {
        public static List<FamilyInfo> GetFamilies(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Select(f => new FamilyInfo
                {
                    UniqueId = f.UniqueId,
                    Name = f.Name ?? string.Empty,
                    CategoryName = f.FamilyCategory?.Name ?? string.Empty,
                    // IsEditable is the documented way to check whether
                    // Document.EditFamily can open this family before
                    // attempting it, rather than relying on catching the
                    // exception it throws for system and in-place families.
                    IsEditable = f.IsEditable
                })
                .OrderBy(f => f.CategoryName, System.StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.Name, System.StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}

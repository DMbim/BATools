using System.Linq;
using Autodesk.Revit.DB;

namespace BA.Core.Rooms
{
    public static class FamilySymbolUtils
    {
        /// <summary>
        /// Finds a FamilySymbol by family name (and optionally symbol name) in the current document.
        /// Assumes the family is already loaded (best practice for office templates).
        /// </summary>
        public static FamilySymbol? FindDetailSymbol(Document doc, string familyName, string? symbolName = null)
        {
            var symbols = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .ToList();

            var match = symbols.FirstOrDefault(s =>
                s.Family != null &&
                s.Family.Name == familyName &&
                (symbolName == null || s.Name == symbolName));

            return match;
        }
    }
}

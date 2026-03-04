using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace BA.Core.Rooms
{
    public static class FamilySymbolUtils
    {
        public static FamilySymbol? FindDetailSymbol(
            Document doc,
            string familyName,
            string? symbolName = null,
            bool loadIfMissing = true,
            string? familyFileNameOrRelativePath = null,
            bool activateIfFound = false)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (string.IsNullOrWhiteSpace(familyName)) throw new ArgumentException("familyName is empty.", nameof(familyName));

            // 1) Find already loaded
            var loaded = FindLoadedSymbol(doc, familyName, symbolName);
            if (loaded != null)
            {
                if (activateIfFound && !loaded.IsActive) loaded.Activate(); // requires doc.IsModifiable
                return loaded;
            }

            if (!loadIfMissing) return null;

            // We REQUIRE caller to be in an open transaction for load/activate (safer, predictable)
            if (!doc.IsModifiable)
                throw new InvalidOperationException("FindDetailSymbol(loadIfMissing:true) must be called inside an open Transaction.");

            // 2) Load from disk (Assets\Families)
            var rel = string.IsNullOrWhiteSpace(familyFileNameOrRelativePath)
                ? familyName.Trim() + ".rfa"
                : familyFileNameOrRelativePath.Trim();

            var fullPath = GetFamilyAssetPath(rel);
            if (!File.Exists(fullPath))
                return null;

            // Load family into project
            if (!doc.LoadFamily(fullPath, new AlwaysLoadFamilyOptions(), out var fam) || fam == null)
                return null;

            // 3) Pick symbol from loaded family (robust even if internal family name differs)
            var sym = fam.GetFamilySymbolIds()
                         .Select(id => doc.GetElement(id) as FamilySymbol)
                         .FirstOrDefault(s => s != null && (symbolName == null || s.Name.Equals(symbolName, StringComparison.OrdinalIgnoreCase)));

            if (sym == null)
                sym = fam.GetFamilySymbolIds().Select(id => doc.GetElement(id) as FamilySymbol).FirstOrDefault(s => s != null);

            if (sym == null) return null;

            if (activateIfFound && !sym.IsActive)
                sym.Activate();

            return sym;
        }

        private static FamilySymbol? FindLoadedSymbol(Document doc, string familyName, string? symbolName)
        {
            var collector = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol));
            foreach (FamilySymbol s in collector)
            {
                if (s.Family == null) continue;
                if (!string.Equals(s.Family.Name, familyName, StringComparison.OrdinalIgnoreCase)) continue;

                if (symbolName == null || string.Equals(s.Name, symbolName, StringComparison.OrdinalIgnoreCase))
                    return s;
            }
            return null;
        }

        private static string GetFamilyAssetPath(string familyFileNameOrRelativePath)
        {
            var asmDir = Path.GetDirectoryName(typeof(FamilySymbolUtils).Assembly.Location) ?? "";

            // supports both:
            //  - <Install>\Assets\Families\...
            //  - bundle layout: <Contents\2026>\..\..\Assets\Families\...
            var p1 = Path.Combine(asmDir, "Assets", "Families", familyFileNameOrRelativePath);
            if (File.Exists(p1)) return p1;

            var bundleRoot = Path.GetFullPath(Path.Combine(asmDir, "..", ".."));
            var p2 = Path.Combine(bundleRoot, "Assets", "Families", familyFileNameOrRelativePath);
            return p2;
        }

        private sealed class AlwaysLoadFamilyOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = false;
                return true;
            }

            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Project;
                overwriteParameterValues = false;
                return true;
            }
        }
    }
}

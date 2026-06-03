using System;
using System.IO;
using Autodesk.Revit.DB;

namespace BA.Resources
{
    internal static class FamilyLoader
    {
        /// <summary>
        /// Loads a family from BA\Assets\Families\... into the current document.
        /// Returns the loaded (or already existing) Family, if possible.
        /// </summary>
        public static Family? LoadFamilyFromAssets(Document doc, string familyFileNameOrRelativePath)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            if (!BaResourcePaths.FamilyExists(familyFileNameOrRelativePath, out var fullPath))
                throw new FileNotFoundException("Family file not found in Assets\\Families.", fullPath);

            // If family already loaded, we can skip loading from disk (optional but nice).
            // NOTE: Family.Name is not always identical to file name. If you want exact matching,
            // pass expectedFamilyName and check that too.
            var familyNameGuess = Path.GetFileNameWithoutExtension(fullPath);
            var existing = FindFamilyByName(doc, familyNameGuess);
            if (existing != null)
                return existing;

            using var t = new Transaction(doc, "Load BA Family");
            t.Start();

            Family? loadedFamily = null;
            var ok = doc.LoadFamily(fullPath, new AlwaysLoadFamilyOptions(), out loadedFamily);

            t.Commit();

            return ok ? loadedFamily : loadedFamily;
        }

        private static Family? FindFamilyByName(Document doc, string familyName)
        {
            // Fast enough for occasional use. If you do this a lot, cache results.
            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(Family));

            foreach (Family f in collector)
            {
                if (string.Equals(f.Name, familyName, StringComparison.OrdinalIgnoreCase))
                    return f;
            }
            return null;
        }

        private sealed class AlwaysLoadFamilyOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = false; // safer default
                return true; // keep going
            }

            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Project;     // usually correct for office-shipped content
                overwriteParameterValues = false;  // safer default
                return true;
            }
        }
    }
}

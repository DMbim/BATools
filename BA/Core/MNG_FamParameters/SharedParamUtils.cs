using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Appp = Autodesk.Revit.ApplicationServices.Application;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using Autodesk.Revit.DB;
using System.IO;
using Autodesk.Revit.ApplicationServices;

namespace BA.Core
{ 
    public static class SharedParamUtils
    {
        private static DefinitionFile _sharedParamFile;

        public static void LoadSharedParameterFile(Appp app, string overridePath = null)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                if (!File.Exists(overridePath))
                    throw new FileNotFoundException("Shared parameter file not found.", overridePath);
                app.SharedParametersFilename = overridePath;
            }

            _sharedParamFile = app.OpenSharedParameterFile();
            if (_sharedParamFile == null)
                throw new InvalidOperationException("Failed to open shared parameter file. Check Revit Options or the provided path.");
        }

        public static Dictionary<string, Definition> BuildExternalDefinitionLookup()
        {
            if (_sharedParamFile == null)
                throw new InvalidOperationException("Shared parameter file not loaded. Call LoadSharedParameterFile(...) first.");

            var dict = new Dictionary<string, Definition>(StringComparer.OrdinalIgnoreCase);
            foreach (DefinitionGroup g in _sharedParamFile.Groups)
                foreach (Definition d in g.Definitions)
                    if (!dict.ContainsKey(d.Name)) dict.Add(d.Name, d);

            return dict;
        }
        public static ExternalDefinition FindBestSharedDefinition(string familyParamName, Dictionary<string, Definition> lookup, out string matchedName, double minScore = 0.66)
        {
            matchedName = null;
            if (string.IsNullOrWhiteSpace(familyParamName) || lookup == null || lookup.Count == 0) return null;

            // 1) exact (case-insensitive)
            if (lookup.TryGetValue(familyParamName, out Definition exact) && exact is ExternalDefinition ex1)
            {
                matchedName = exact.Name;
                return ex1;
            }

            // 2) token-key exact (ignores order, BA_, spaces, camelCase)
            var familyKey = NameMatcher.TokenKey(familyParamName);
            foreach (var kvp in lookup)
            {
                if (NameMatcher.TokenKey(kvp.Key) == familyKey && kvp.Value is ExternalDefinition ex2)
                {
                    matchedName = kvp.Key;
                    return ex2;
                }
            }

            // 3) fuzzy best score
            var famTokens = NameMatcher.Tokens(familyParamName);
            double bestScore = 0.0;
            ExternalDefinition best = null;
            string bestName = null;

            foreach (var kvp in lookup)
            {
                var spTokens = NameMatcher.Tokens(kvp.Key);
                double score = NameMatcher.ScoreTokens(famTokens, spTokens);
                if (score > bestScore && kvp.Value is ExternalDefinition ex3)
                {
                    bestScore = score;
                    best = ex3;
                    bestName = kvp.Key;
                }
            }

            if (best != null && bestScore >= minScore)
            {
                matchedName = bestName;
                return best;
            }

            return null;
        }
        
        public static Dictionary<string, Definition> BuildExternalDefinitionLookup(UIApplication uiapp)
        {
            LoadSharedParameterFile(uiapp.Application);
            return BuildExternalDefinitionLookup();
        }
        public static ExternalDefinition FindExternalDefinitionByGuidOrName(
        Application app,
        string sharedParamFilePath,
        string defName,
        Guid guidHint)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            LoadSharedParameterFile(app, sharedParamFilePath);

            var spf = app.OpenSharedParameterFile();
            if (spf == null) return null;

            // 1) GUID match (best)
            if (guidHint != Guid.Empty)
            {
                foreach (DefinitionGroup g in spf.Groups)
                {
                    foreach (Definition d in g.Definitions)
                    {
                        if (d is ExternalDefinition ext && ext.GUID == guidHint)
                            return ext;
                    }
                }
            }
            
            // 2) Name match (fallback)
            if (!string.IsNullOrWhiteSpace(defName))
            {
                foreach (DefinitionGroup g in spf.Groups)
                {
                    foreach (Definition d in g.Definitions)
                    {
                        if (d is ExternalDefinition ext &&
                            d.Name.Equals(defName, StringComparison.OrdinalIgnoreCase))
                            return ext;
                            
                    }
                }
            }

            return null;
        }
    }
}

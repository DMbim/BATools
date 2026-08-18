// BA/Core/SharedParamUtils.cs
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using Appp = Autodesk.Revit.ApplicationServices.Application;
using Autodesk.Revit.DB;
using System.IO;
using Application = Autodesk.Revit.ApplicationServices.Application;

namespace BA.Core
{
    /// <summary>
    /// Fuzzy family-parameter to shared-parameter matcher. Distinct feature from
    /// BA.Core.Parameters.SharedParameterBindingService, this is for matching an existing family
    /// parameter's name against candidates in the shared parameter file (not for binding BA_
    /// project parameters).
    ///
    /// The DefinitionFile cache is keyed by resolved file path rather than a single static field.
    /// The single-field version meant any two features loading different shared parameter file
    /// paths in the same Revit session would clobber each other's cached handle with no warning.
    /// _lastLoadedPath tracks which entry the parameterless BuildExternalDefinitionLookup() call
    /// should use, matching the previous "operate on whatever was loaded most recently" behavior,
    /// but without discarding earlier entries.
    /// </summary>
    public static class SharedParamUtils
    {
        private static readonly Dictionary<string, DefinitionFile> _cache =
            new(StringComparer.OrdinalIgnoreCase);
        private static string _lastLoadedPath;

        public static void LoadSharedParameterFile(Appp app, string overridePath = null)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            string resolvedPath;
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                if (!File.Exists(overridePath))
                    throw new FileNotFoundException("Shared parameter file not found.", overridePath);
                app.SharedParametersFilename = overridePath;
                resolvedPath = overridePath;
            }
            else
            {
                resolvedPath = app.SharedParametersFilename;
            }

            DefinitionFile file = app.OpenSharedParameterFile();
            if (file == null)
                throw new InvalidOperationException("Failed to open shared parameter file. Check Revit Options or the provided path.");

            string cacheKey = string.IsNullOrWhiteSpace(resolvedPath) ? string.Empty : resolvedPath;
            _cache[cacheKey] = file;
            _lastLoadedPath = cacheKey;
        }

        public static Dictionary<string, Definition> BuildExternalDefinitionLookup()
        {
            if (_lastLoadedPath == null || !_cache.TryGetValue(_lastLoadedPath, out DefinitionFile sharedParamFile))
                throw new InvalidOperationException("Shared parameter file not loaded. Call LoadSharedParameterFile(...) first.");

            var dict = new Dictionary<string, Definition>(StringComparer.OrdinalIgnoreCase);
            foreach (DefinitionGroup g in sharedParamFile.Groups)
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

        /// <summary>
        /// GUID lookup with name fallback. Previously reimplemented the group/definition scan
        /// loop inline, now delegates to BA.Core.Parameters.SharedParameterFileReader so there is
        /// exactly one place that walks a DefinitionFile's groups and definitions.
        /// </summary>
        public static ExternalDefinition FindExternalDefinitionByGuidOrName(
            Application app,
            string sharedParamFilePath,
            string defName,
            Guid guidHint)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            if (guidHint != Guid.Empty)
            {
                var byGuid = Parameters.SharedParameterFileReader.FindExternalDefinitionByGuid(app, sharedParamFilePath, guidHint);
                if (byGuid != null) return byGuid;
            }

            if (!string.IsNullOrWhiteSpace(defName))
                return Parameters.SharedParameterFileReader.FindExternalDefinitionByName(app, sharedParamFilePath, defName);

            return null;
        }
    }
}
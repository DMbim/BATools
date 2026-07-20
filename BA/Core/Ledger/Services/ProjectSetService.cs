using System;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using BA.BAApplication;

namespace BA.Core.Ledger
{
    /// <summary>
    /// Resolves which "Project Set" a central model belongs to, which determines which Main
    /// Ledger file LedgerFileService reads/writes for that document. Two independent things
    /// can never write to each other's data as long as they resolve to different project set
    /// names: they end up in different physical files, not filtered rows in one shared file.
    ///
    /// This is INDEPENDENT of CentralIdentifierService/PersonalLedgerService, which control
    /// each user's own per-central baseline file. Do not conflate the two: a project set
    /// controls the shared Main Ledger destination, a central identifier controls one user's
    /// merge-base tracking for one central.
    ///
    /// Resolution order:
    /// 1. Manual override, if explicitly set on this document via SetProjectSetName. Rare;
    ///    exists for the case auto-detection fails or a deliberate cross-project grouping is
    ///    needed.
    /// 2. Auto-detected from the workshared central's file path: the first folder segment
    ///    after the drive letter is taken as the project number, matching the convention
    ///    N:\{ProjectNumber}\CAD\{Building}\... e.g. "N:\17-081\CAD\5-1 VE\Central.rvt" ->
    ///    "17-081". Drive letter itself is irrelevant to the result, only the folder
    ///    structure matters, so this is stable even if different users have different drive
    ///    mappings to the same network location.
    /// 3. Null if neither resolves (not workshared, central path unavailable, or the path
    ///    doesn't match the expected convention). Callers must treat null as "use the legacy
    ///    fallback ledger", not as an error.
    ///
    /// LIMITATION: the auto-detect regex assumes a mapped drive letter path
    /// ("X:\ProjectNumber\..."). If centrals are ever accessed via raw UNC paths
    /// ("\\server\share\ProjectNumber\...") instead of a mapped drive, this needs a second
    /// pattern added; flag it if that's how paths actually resolve in practice.
    /// </summary>
    public static class ProjectSetService
    {
        private static readonly Guid SchemaGuid = new Guid("8F3E2C7A-1B4D-4A9E-9C6F-2E8B7D4A1F53");
        private const string FieldName = "ManualProjectSetName";
        private static Schema _schema;

        private static readonly Regex ProjectNumberPattern =
            new Regex(@"^[A-Za-z]:\\([^\\]+)\\", RegexOptions.Compiled);

        private static readonly Regex ProjectNumberPatternUnc =
            new Regex(@"^\\\\[^\\]+\\[^\\]+\\([^\\]+)\\", RegexOptions.Compiled);

        private static readonly Regex ProjectNumberShapePattern =
            new Regex(@"^\d{2}-\d{3}$", RegexOptions.Compiled);

        public static string GetProjectSetName(Document doc)
        {
            string manual = GetManualOverride(doc);
            if (!string.IsNullOrWhiteSpace(manual))
            {
                return manual;
            }

            return TryAutoDetectFromCentralPath(doc);
        }

        /// <summary>
        /// Must be called from within an active Transaction on doc. Caller's responsibility,
        /// same convention as CentralIdentifierService.SetIdentifier.
        /// </summary>
        public static void SetProjectSetName(Document doc, string projectSetName)
        {
            ProjectInfo info = doc?.ProjectInformation;
            if (info == null)
            {
                throw new InvalidOperationException("Document has no ProjectInformation element to store the project set on.");
            }

            var entity = new Entity(GetSchema());
            entity.Set(FieldName, projectSetName ?? string.Empty);
            info.SetEntity(entity);
        }

        private static string GetManualOverride(Document doc)
        {
            ProjectInfo info = doc?.ProjectInformation;
            if (info == null)
            {
                return null;
            }

            Entity entity = info.GetEntity(GetSchema());
            if (!entity.IsValid())
            {
                return null;
            }

            string value = entity.Get<string>(FieldName);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static string TryAutoDetectFromCentralPath(Document doc)
        {
            try
            {
                if (doc == null || !doc.IsWorkshared)
                {
                    return null;
                }

                ModelPath modelPath = doc.GetWorksharingCentralModelPath();
                if (modelPath == null)
                {
                    return null;
                }

                string userVisiblePath = ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath);
                if (string.IsNullOrWhiteSpace(userVisiblePath))
                {
                    return null;
                }

                string[] segments = userVisiblePath.Split(
                    new[] { '\\', '/' },
                    StringSplitOptions.RemoveEmptyEntries);

                foreach (string segment in segments)
                {
                    if (ProjectNumberShapePattern.IsMatch(segment))
                    {
                        return segment;
                    }
                }

                AppLogger.LogInfo($"ProjectSetService: central path '{userVisiblePath}' contained no segment matching the project number convention (\\d{{2}}-\\d{{3}}) for '{doc.Title}'.");
                return null;
            }
            catch (Exception ex)
            {
                AppLogger.LogInfo($"ProjectSetService: could not resolve central path for '{doc?.Title}': {ex.Message}");
                return null;
            }
        }

        private static Schema GetSchema()
        {
            if (_schema != null)
            {
                return _schema;
            }

            _schema = Schema.Lookup(SchemaGuid);
            if (_schema != null)
            {
                return _schema;
            }

            var builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName("BA_LedgerManualProjectSet");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField(FieldName, typeof(string));

            _schema = builder.Finish();
            return _schema;
        }
    }
}
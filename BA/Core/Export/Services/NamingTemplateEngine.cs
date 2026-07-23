using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using BA.Core.Export.Models;
using BA.Settings;

namespace BA.Core.Export.Services
{
    /// <summary>
    /// Resolves {Token} and {Token:format} placeholders against a specific
    /// ViewSheet. Read-only Document access, must still be called from a
    /// valid Revit API thread context, never directly from WPF UI code.
    ///
    /// The {Revision} token deliberately does not point at a hardcoded
    /// parameter. It reads BA.Settings.DateToolSettings.SelectedRevParam,
    /// the same per-user configured parameter name that Cmd_SheetDateAndRevision
    /// already reads and writes, resolved generically across storage types the
    /// same way SheetUpdateService.TryIncrement does (Integer or numeric-string),
    /// so export naming always reflects whatever revision value the title
    /// block actually shows, not a second, disconnected counter.
    ///
    /// DateToolSettings is loaded once per job run by the caller and passed
    /// in as revisionParamName, not re-read from disk on every token
    /// resolution, resolving a naming and folder template for every sheet
    /// in a large set would otherwise mean one JSON file read per sheet.
    /// </summary>
    public static class NamingTemplateEngine
    {
        private static readonly Regex TokenPattern =
            new Regex(@"\{([A-Za-z0-9_]+)(?::([^{}]+))?\}", RegexOptions.Compiled);

        private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

        /// <summary>
        /// Loads the currently configured revision parameter name from
        /// DateToolSettings. Call this once per job run (or once per preview),
        /// not per sheet, and pass the result into ResolveFileName/ResolveFolder.
        /// </summary>
        public static string LoadCurrentRevisionParamName()
        {
            var settings = DateToolSettings.LoadWithMigration();
            return settings.SelectedRevParam;
        }

        public static string ResolveFileName(string template, ViewSheet sheet, ExportJobSettings jobSettings, DateTime exportDate, string revisionParamName)
        {
            return Resolve(template, sheet, jobSettings, exportDate, revisionParamName);
        }

        /// <summary>
        /// Resolves the output folder template for one sheet. Literal path
        /// separators written in the template are preserved, only resolved
        /// token VALUES are sanitized, so a sheet name containing "/" cannot
        /// corrupt the folder structure.
        /// </summary>
        public static string ResolveFolder(string template, ViewSheet sheet, ExportJobSettings jobSettings, DateTime exportDate, string revisionParamName)
        {
            return Resolve(template, sheet, jobSettings, exportDate, revisionParamName);
        }

        private static string Resolve(string template, ViewSheet sheet, ExportJobSettings jobSettings, DateTime exportDate, string revisionParamName)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                throw new ArgumentException("Template cannot be empty.", nameof(template));
            }

            var doc = sheet.Document;

            return TokenPattern.Replace(template, match =>
            {
                var tokenName = match.Groups[1].Value;
                var formatOverride = match.Groups[2].Success ? match.Groups[2].Value : null;

                var resolvedValue = ResolveToken(tokenName, formatOverride, sheet, doc, jobSettings, exportDate, revisionParamName);

                return SanitizeToken(resolvedValue);
            });
        }

        private static string ResolveToken(string tokenName, string formatOverride, ViewSheet sheet, Document doc, ExportJobSettings jobSettings, DateTime exportDate, string revisionParamName)
        {
            switch (tokenName)
            {
                case "SheetNumber":
                    return sheet.SheetNumber ?? string.Empty;

                case "SheetName":
                    return sheet.Name ?? string.Empty;

                case "ProjectNumber":
                    return GetProjectInfoParameterAsString(doc, BuiltInParameter.PROJECT_NUMBER, tokenName, sheet.SheetNumber);

                case "ProjectName":
                    return GetProjectInfoParameterAsString(doc, BuiltInParameter.PROJECT_NAME, tokenName, sheet.SheetNumber);

                case "Revision":
                    if (string.IsNullOrWhiteSpace(revisionParamName))
                    {
                        throw new NamingTemplateResolutionException(tokenName, sheet.SheetNumber,
                            "No revision parameter is configured. Run 'Sheet Date + Rev > Settings' first to set one.");
                    }

                    return ResolveParameterByName(revisionParamName, sheet, doc, formatOverride, tokenName);

                case "Date":
                    var dateFormat = string.IsNullOrEmpty(formatOverride) ? jobSettings.DateFormat : formatOverride;
                    return exportDate.ToString(dateFormat);

                default:
                    return ResolveParameterByName(tokenName, sheet, doc, formatOverride, tokenName);
            }
        }

        private static string GetProjectInfoParameterAsString(Document doc, BuiltInParameter builtInParameter, string tokenName, string sheetNumber)
        {
            var projectInfo = doc.ProjectInformation;
            var parameter = projectInfo?.get_Parameter(builtInParameter);

            if (parameter == null || !parameter.HasValue)
            {
                throw new NamingTemplateResolutionException(tokenName, sheetNumber,
                    $"Project Information parameter for token '{{{tokenName}}}' has no value.");
            }

            return parameter.AsString() ?? parameter.AsValueString() ?? string.Empty;
        }

        /// <summary>
        /// Looks up a parameter by its real Revit name (either the literal
        /// token name for arbitrary tokens, or the DateToolSettings-configured
        /// name for {Revision}) and resolves it generically across storage
        /// types. tokenName is only used for error messages, paramNameToLookUp
        /// is what's actually passed to LookupParameter.
        /// </summary>
        private static string ResolveParameterByName(string paramNameToLookUp, ViewSheet sheet, Document doc, string formatOverride, string tokenName)
        {
            var parameter = sheet.LookupParameter(paramNameToLookUp) ?? doc.ProjectInformation?.LookupParameter(paramNameToLookUp);

            if (parameter == null)
            {
                throw new NamingTemplateResolutionException(tokenName, sheet.SheetNumber,
                    $"No parameter named '{paramNameToLookUp}' was found on sheet {sheet.SheetNumber} or in Project Information. " +
                    "Check spelling, this must match a real parameter name, not a display label.");
            }

            if (!parameter.HasValue)
            {
                return string.Empty;
            }

            switch (parameter.StorageType)
            {
                case StorageType.String:
                    return parameter.AsString() ?? string.Empty;

                case StorageType.Integer:
                    return string.IsNullOrEmpty(formatOverride)
                        ? parameter.AsInteger().ToString()
                        : parameter.AsInteger().ToString(formatOverride);

                case StorageType.Double:
                    return string.IsNullOrEmpty(formatOverride)
                        ? (parameter.AsValueString() ?? parameter.AsDouble().ToString())
                        : parameter.AsDouble().ToString(formatOverride);

                case StorageType.ElementId:
                    return parameter.AsValueString() ?? parameter.AsElementId().ToString();

                default:
                    throw new NamingTemplateResolutionException(tokenName, sheet.SheetNumber,
                        $"Parameter '{paramNameToLookUp}' has an unsupported storage type '{parameter.StorageType}'.");
            }
        }

        private static string SanitizeToken(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            var builder = new StringBuilder(value.Length);

            foreach (var c in value)
            {
                builder.Append(Array.IndexOf(InvalidFileNameChars, c) >= 0 ? '_' : c);
            }

            return builder.ToString();
        }
    }
}

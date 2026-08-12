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
    /// Resolves {Token} and {Token:format} placeholders against either a
    /// specific ViewSheet or an arbitrary View, depending on the job's
    /// SourceMode. Read-only Document access, must still be called from a
    /// valid Revit API thread context, never directly from WPF UI code.
    ///
    /// {Revision} only exists for the Sheets path. Revit tracks revisions
    /// per sheet, not per view, a bare view exported directly has no
    /// revision of its own, so {Revision} throws a clear, actionable
    /// exception if used against a view rather than silently resolving to
    /// something misleading, the same "fail loudly, not silently" approach
    /// already used everywhere else in this class (missing revision
    /// parameter, missing Project Information value, and so on).
    /// {SheetNumber}/{SheetName} are the same, sheet-only, throwing
    /// clearly if used in Views mode. {ViewName}/{ViewType} are the view
    /// equivalents.
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
        // Token name allows any character except { } or :, not just
        // [A-Za-z0-9_]. Confirmed bug: many real Revit parameter names
        // contain spaces (Project Issue Date, Client Name, and so on),
        // the old pattern silently failed to match those at all, meaning
        // {Project Issue Date} passed through as literal unresolved text
        // in the output filename rather than throwing or resolving,
        // Regex.Replace only touches what actually matches. Colon still
        // has to stay reserved to separate the name from an optional
        // :format override; a parameter name that itself contains a
        // colon (extremely rare in practice) would misparse, an
        // acknowledged limitation rather than something worth
        // overengineered escaping logic for.
        private static readonly Regex TokenPattern =
            new Regex(@"\{([^{}:]+)(?::([^{}]+))?\}", RegexOptions.Compiled);

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

        // ---- Sheets mode ----

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

                    return ResolveParameterByName(revisionParamName, sheet, doc, formatOverride, tokenName, sheet.SheetNumber);

                case "Date":
                    var dateFormat = string.IsNullOrEmpty(formatOverride) ? jobSettings.DateFormat : formatOverride;
                    return exportDate.ToString(dateFormat);

                default:
                    return ResolveParameterByName(tokenName, sheet, doc, formatOverride, tokenName, sheet.SheetNumber);
            }
        }

        // ---- Views mode ----

        /// <summary>
        /// Resolves a naming or folder template against an arbitrary View
        /// (a plan, section, elevation, 3D view, and so on, not exported as
        /// part of a sheet). {SheetNumber}, {SheetName}, and {Revision} are
        /// deliberately not available here, they throw a clear, actionable
        /// exception rather than silently resolving to something
        /// misleading, since none of them mean anything for a bare view.
        /// {ViewName} and {ViewType} are the view equivalents.
        /// </summary>
        public static string ResolveFileNameForView(string template, View view, ExportJobSettings jobSettings, DateTime exportDate)
        {
            return ResolveForView(template, view, jobSettings, exportDate);
        }

        public static string ResolveFolderForView(string template, View view, ExportJobSettings jobSettings, DateTime exportDate)
        {
            return ResolveForView(template, view, jobSettings, exportDate);
        }

        private static string ResolveForView(string template, View view, ExportJobSettings jobSettings, DateTime exportDate)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                throw new ArgumentException("Template cannot be empty.", nameof(template));
            }

            var doc = view.Document;

            return TokenPattern.Replace(template, match =>
            {
                var tokenName = match.Groups[1].Value;
                var formatOverride = match.Groups[2].Success ? match.Groups[2].Value : null;

                var resolvedValue = ResolveViewToken(tokenName, formatOverride, view, doc, jobSettings, exportDate);

                return SanitizeToken(resolvedValue);
            });
        }

        private static string ResolveViewToken(string tokenName, string formatOverride, View view, Document doc, ExportJobSettings jobSettings, DateTime exportDate)
        {
            switch (tokenName)
            {
                case "ViewName":
                    return view.Name ?? string.Empty;

                case "ViewType":
                    return view.ViewType.ToString();

                case "ProjectNumber":
                    return GetProjectInfoParameterAsString(doc, BuiltInParameter.PROJECT_NUMBER, tokenName, view.Name);

                case "ProjectName":
                    return GetProjectInfoParameterAsString(doc, BuiltInParameter.PROJECT_NAME, tokenName, view.Name);

                case "Date":
                    var dateFormat = string.IsNullOrEmpty(formatOverride) ? jobSettings.DateFormat : formatOverride;
                    return exportDate.ToString(dateFormat);

                case "Revision":
                    throw new NamingTemplateResolutionException(tokenName, view.Name,
                        "{Revision} is not available when exporting views directly, Revit tracks revisions per sheet, not per view. Use {ViewName} or {ViewType} instead, or export this content as part of a sheet job.");

                case "SheetNumber":
                case "SheetName":
                    throw new NamingTemplateResolutionException(tokenName, view.Name,
                        $"{{{tokenName}}} is not available when exporting views directly, this view is not being exported as part of a sheet. Use {{ViewName}} instead.");

                default:
                    return ResolveParameterByName(tokenName, view, doc, formatOverride, tokenName, view.Name);
            }
        }

        // ---- Shared ----

        private static string GetProjectInfoParameterAsString(Document doc, BuiltInParameter builtInParameter, string tokenName, string identifier)
        {
            var projectInfo = doc.ProjectInformation;
            var parameter = projectInfo?.get_Parameter(builtInParameter);

            if (parameter == null || !parameter.HasValue)
            {
                throw new NamingTemplateResolutionException(tokenName, identifier,
                    $"Project Information parameter for token '{{{tokenName}}}' has no value.");
            }

            return parameter.AsString() ?? parameter.AsValueString() ?? string.Empty;
        }

        /// <summary>
        /// Looks up a parameter by its real Revit name (either the literal
        /// token name for arbitrary tokens, or the DateToolSettings-configured
        /// name for {Revision}) and resolves it generically across storage
        /// types. Works against any Element with LookupParameter, ViewSheet
        /// and View both qualify, identifier is only used for error
        /// messages (sheet number for sheets, view name for views).
        /// </summary>
        private static string ResolveParameterByName(string paramNameToLookUp, Element element, Document doc, string formatOverride, string tokenName, string identifier)
        {
            var parameter = element.LookupParameter(paramNameToLookUp) ?? doc.ProjectInformation?.LookupParameter(paramNameToLookUp);

            if (parameter == null)
            {
                var elementKind = element is ViewSheet ? "sheet" : "view";

                throw new NamingTemplateResolutionException(tokenName, identifier,
                    $"No parameter named '{paramNameToLookUp}' was found on {elementKind} {identifier} or in Project Information. " +
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
                    throw new NamingTemplateResolutionException(tokenName, identifier,
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
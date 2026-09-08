using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using BA.BAApplication;
using BA.Core.Export.Models;
using BA.Core.Sheets;
using BA.Settings;

namespace BA.Core.Export.Services
{
    /// <summary>
    /// Bumps the globally configured issue date and revision parameters
    /// (BA.Settings.DateToolSettings.SelectedDateParam/SelectedRevParam/
    /// SelectedFormat, the same global config Cmd_SheetDateAndRevision
    /// used to write before being retired in favor of this export job
    /// integration) across every sheet in an export job's resolved sheet
    /// set, once per job run, before any format is exported. Reuses
    /// SheetUpdateService.TrySetText/TryIncrement/GetNowText directly
    /// rather than re-implementing the parameter write logic.
    ///
    /// The idempotency guard is read live off the document, not tracked
    /// in any settings file. ExportSettingsStore is explicitly local per
    /// Windows user, not synced through the central model (see its own
    /// class remarks); a JSON-tracked "last bumped" flag would silently
    /// fail to prevent a double bump whenever a second person on a
    /// shared, workshared model ran an overlapping job the same day from
    /// their own machine. Comparing the sheet's own date parameter text
    /// against today's formatted text instead works correctly regardless
    /// of which user or which job performed the original bump, since the
    /// document is the single shared source of truth once synced.
    ///
    /// Must be called from a valid Revit API thread context, opens and
    /// commits its own Transaction. Safe to call from either a manual
    /// command or the idling scheduler, same as ExportJobRunner itself.
    /// </summary>
    public static class DateRevisionBumpService
    {
        public static DateRevisionBumpOutcome BumpIfNeeded(Document doc, IList<ViewSheet> sheets, ExportJobSettings jobSettings)
        {
            var outcome = new DateRevisionBumpOutcome();

            if (doc == null || sheets == null || sheets.Count == 0)
            {
                return outcome;
            }

            if (!jobSettings.BumpDateRevisionOnRun || jobSettings.SourceMode != ExportSourceMode.Sheets)
            {
                return outcome;
            }

            var settings = DateToolSettings.LoadWithMigration();

            if (string.IsNullOrWhiteSpace(settings.SelectedDateParam) || string.IsNullOrWhiteSpace(settings.SelectedRevParam))
            {
                outcome.SkippedNoGlobalConfig = true;
                AppLogger.LogInfo($"Export job '{jobSettings.JobName}': date/revision bump requested but no date/revision parameter is configured globally. Configure it in Export Settings first.");
                return outcome;
            }

            var nowText = SheetUpdateService.GetNowText(settings.SelectedFormat);

            using (var t = new Transaction(doc, "BA \u2013 Bump Date/Revision (Export Job)"))
            {
                t.Start();

                if (jobSettings.RevisionBumpScope == RevisionBumpScope.WholeJob)
                {
                    BumpWholeJob(sheets, settings, nowText, outcome);
                }
                else
                {
                    BumpPerSheet(sheets, settings, nowText, outcome);
                }

                t.Commit();
            }

            AppLogger.LogInfo(
                $"Export job '{jobSettings.JobName}': date/revision bump ({jobSettings.RevisionBumpScope}) " +
                $"updated {outcome.SheetsUpdated} sheet(s), {outcome.SheetsAlreadyUpToDateToday} already up to date today, " +
                $"{outcome.SheetsSkippedMissingParam} skipped (parameter missing or read only).");

            return outcome;
        }

        private static void BumpWholeJob(IList<ViewSheet> sheets, DateToolSettings settings, string nowText, DateRevisionBumpOutcome outcome)
        {
            var firstSheet = sheets[0];

            if (IsAlreadyUpToDateToday(firstSheet, settings.SelectedDateParam, nowText))
            {
                outcome.SheetsAlreadyUpToDateToday = sheets.Count;
                return;
            }

            foreach (var sheet in sheets)
            {
                BumpOneSheet(sheet, settings, nowText, outcome);
            }
        }

        private static void BumpPerSheet(IList<ViewSheet> sheets, DateToolSettings settings, string nowText, DateRevisionBumpOutcome outcome)
        {
            foreach (var sheet in sheets)
            {
                if (IsAlreadyUpToDateToday(sheet, settings.SelectedDateParam, nowText))
                {
                    outcome.SheetsAlreadyUpToDateToday++;
                    continue;
                }

                BumpOneSheet(sheet, settings, nowText, outcome);
            }
        }

        private static void BumpOneSheet(ViewSheet sheet, DateToolSettings settings, string nowText, DateRevisionBumpOutcome outcome)
        {
            var dateParam = sheet.LookupParameter(settings.SelectedDateParam);
            var revParam = sheet.LookupParameter(settings.SelectedRevParam);

            bool dateOk = SheetUpdateService.TrySetText(dateParam, nowText);
            bool revOk = SheetUpdateService.TryIncrement(revParam);

            if (dateOk && revOk)
            {
                outcome.SheetsUpdated++;
            }
            else
            {
                outcome.SheetsSkippedMissingParam++;
                AppLogger.LogInfo(
                    $"Date/revision bump: sheet {sheet.SheetNumber} skipped " +
                    $"(date param '{settings.SelectedDateParam}' {(dateOk ? "ok" : "missing or read only")}, " +
                    $"revision param '{settings.SelectedRevParam}' {(revOk ? "ok" : "missing or read only")}).");
            }
        }

        /// <summary>
        /// True when the sheet's date parameter already reads exactly
        /// today's formatted text. This is the entire idempotency guard,
        /// deliberately not a separately tracked flag, see class remarks.
        /// A missing or unreadable date parameter is never considered
        /// "already up to date", it falls through to BumpOneSheet, which
        /// reports it as skipped via TrySetText's own return value rather
        /// than being silently swallowed here.
        /// </summary>
        private static bool IsAlreadyUpToDateToday(ViewSheet sheet, string dateParamName, string nowText)
        {
            var dateParam = sheet.LookupParameter(dateParamName);

            if (dateParam == null || !dateParam.HasValue)
            {
                return false;
            }

            var currentText = dateParam.StorageType == StorageType.String
                ? dateParam.AsString()
                : dateParam.AsValueString();

            return string.Equals(currentText, nowText, StringComparison.Ordinal);
        }
    }

    public sealed class DateRevisionBumpOutcome
    {
        public int SheetsUpdated { get; set; }
        public int SheetsAlreadyUpToDateToday { get; set; }
        public int SheetsSkippedMissingParam { get; set; }
        public bool SkippedNoGlobalConfig { get; set; }

        public bool HasAnyActivity =>
            SheetsUpdated > 0 || SheetsAlreadyUpToDateToday > 0 || SheetsSkippedMissingParam > 0 || SkippedNoGlobalConfig;

        public string ToSummaryText()
        {
            if (SkippedNoGlobalConfig)
            {
                return "Date/Revision: skipped, no date/revision parameter configured globally. Set one in Export Settings.";
            }

            if (!HasAnyActivity)
            {
                return string.Empty;
            }

            return $"Date/Revision: {SheetsUpdated} sheet(s) updated, {SheetsAlreadyUpToDateToday} already up to date today, {SheetsSkippedMissingParam} skipped (parameter missing or read only).";
        }
    }
}
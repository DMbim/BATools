using System;
using System.Collections.Generic;

namespace BA.Core.Export.Models
{
    /// <summary>
    /// Sheets and Views are genuinely different sources, not a flag on the
    /// same picker: sheets have SheetNumber/SheetName, views have
    /// ViewName/ViewType, and Revision only makes sense for sheets. A job
    /// is one or the other, not both at once.
    /// </summary>
    public enum ExportSourceMode
    {
        Sheets,
        Views
    }

    /// <summary>
    /// Plain data settings for a single export job. A job covers one
    /// sheet set, naming template, schedule and output folder, exported to
    /// any combination of the enabled formats (PDF and/or DWG) in one run,
    /// rather than one format locking the whole job at creation. Persisted
    /// to JSON, not Revit-bound, so this stays a plain class rather than
    /// an observable one for now.
    /// </summary>
    public class ExportJobSettings
    {
        public Guid JobId { get; set; } = Guid.NewGuid();

        public string JobName { get; set; } = string.Empty;

        public bool ExportPdf { get; set; } = true;
        public bool ExportDwg { get; set; }

        public bool Enabled { get; set; } = true;

        public ExportSourceMode SourceMode { get; set; } = ExportSourceMode.Sheets;

        /// <summary>
        /// When true, ignores SelectedSheetNumbers/SelectedViewUniqueIds
        /// entirely and exports whatever view or sheet is active in Revit
        /// at the moment the job runs instead. In Sheets mode the active
        /// view must actually be a sheet, in Views mode it must not be, a
        /// mismatch is reported as an error rather than silently falling
        /// back to the configured selection.
        /// </summary>
        public bool UseActiveViewOrSheet { get; set; }

        /// <summary>
        /// Sheet numbers chosen via the in-app sheet picker (BA.Views.Export.SheetPickerWindow),
        /// resolved against the live document by ExportJobRunner at export time, not a
        /// Revit ViewSheetSet. A sheet number here that no longer exists (renamed or
        /// deleted) is skipped and reported, it does not fail the whole job. Only
        /// relevant when SourceMode is Sheets.
        /// </summary>
        public List<string> SelectedSheetNumbers { get; set; } = new List<string>();

        /// <summary>
        /// UniqueIds of views chosen via the view picker, resolved against
        /// the live document at export time. UniqueId rather than Name,
        /// views don't have a sheet-number-equivalent stable identifier
        /// the way sheets do. Only relevant when SourceMode is Views.
        /// </summary>
        public List<string> SelectedViewUniqueIds { get; set; } = new List<string>();

        /// <summary>
        /// Filename template, extension appended automatically per format
        /// at export time, not stored here. In Sheets mode, tokens include
        /// {SheetNumber}/{SheetName}/{Revision}. In Views mode, those throw
        /// a clear error if used, {ViewName}/{ViewType} are the equivalents.
        /// Example: "{ProjectNumber}_{SheetNumber}_{SheetName}_Rev{Revision}_{Date}"
        /// </summary>
        public string NamingTemplate { get; set; } = "{SheetNumber}_{SheetName}_Rev{Revision}_{Date}";

        /// <summary>
        /// .NET custom date format string applied to {Date} when the template
        /// has no inline format override, e.g. "yyyyMMdd" or "dd.MM.yyyy".
        /// </summary>
        public string DateFormat { get; set; } = "yyyyMMdd";

        /// <summary>
        /// Output folder template, tokens resolve the same way as NamingTemplate.
        /// Literal path separators you write are preserved, only resolved token
        /// VALUES are sanitized. Example:
        /// @"\\server02\Projects\{ProjectNumber}\Export\{Date}"
        /// </summary>
        public string OutputFolderTemplate { get; set; } = string.Empty;

        /// <summary>
        /// Custom DWG export settings, applied directly, no dependency on a
        /// predefined setup existing in the document. Only relevant when
        /// ExportDwg is true.
        /// </summary>
        public DwgSettings DwgSettings { get; set; } = new DwgSettings();

        /// <summary>
        /// Custom PDF export settings, applied directly. Only relevant when
        /// ExportPdf is true.
        /// </summary>
        public PdfSettings PdfSettings { get; set; } = new PdfSettings();

        public bool ScheduleEnabled { get; set; }

        /// <summary>
        /// Local time of day the idling scheduler checks against.
        /// </summary>
        public TimeSpan ScheduledTimeOfDay { get; set; } = new TimeSpan(18, 0, 0);

        /// <summary>
        /// How long after ScheduledTimeOfDay the idling scheduler will still
        /// treat the job as due. If Revit was not open during this window on
        /// a given day, the job is skipped entirely for that day, it never
        /// fires late, it waits for its next scheduled day.
        /// </summary>
        public TimeSpan CatchUpWindow { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Days this job is allowed to auto run. Empty list means every day.
        /// </summary>
        public List<DayOfWeek> ScheduledDays { get; set; } = new List<DayOfWeek>();

        /// <summary>
        /// Set by the scheduler after a successful auto run so the same job
        /// does not fire again later the same day once the target time has
        /// passed. Never touched by a manual export.
        /// </summary>
        public DateTime? LastAutoRunDate { get; set; }
    }
}
using System;
using System.Collections.Generic;

namespace BA.Core.Export.Models
{
    /// <summary>
    /// Plain data settings for a single export job. PDF and DWG jobs are
    /// fully independent, each has its own sheet set, naming template,
    /// schedule and output folder. Persisted to JSON, not Revit-bound,
    /// so this stays a plain class rather than an observable one for now.
    /// </summary>
    public class ExportJobSettings
    {
        public Guid JobId { get; set; } = Guid.NewGuid();

        public string JobName { get; set; } = string.Empty;

        public ExportFormat Format { get; set; } = ExportFormat.Pdf;

        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Name of the Revit ViewSheetSet that defines which sheets belong
        /// to this job. Must match an existing named set in the document
        /// (Manage tab > View Sheet Set, saved from the Print dialog).
        /// </summary>
        public string SheetSetName { get; set; } = string.Empty;

        /// <summary>
        /// Filename template, extension appended automatically based on Format.
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
        /// For DWG: name of a predefined export setup saved in the document
        /// (Manage tab, Additional Settings, Export Setups DWG/DXF). Required
        /// for DWG jobs. For PDF: unused until pass 2 confirms the current API shape.
        /// </summary>
        public string ExportSetupName { get; set; } = string.Empty;

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

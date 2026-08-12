using System.Collections.Generic;

namespace BA.Core.Export.Models
{
    /// <summary>
    /// Configuration for one on-demand family export run. Unlike sheet
    /// export jobs, this is not persisted as a saved, schedulable job,
    /// family libraries aren't issued on a recurring schedule the way
    /// sheets are, this is a one-shot batch tool: pick families, configure
    /// once, run.
    /// </summary>
    public class FamilyExportSettings
    {
        /// <summary>
        /// UniqueIds of the families to export, resolved against the live
        /// document at export time, matching the SelectedSheetNumbers
        /// pattern used for sheet jobs.
        /// </summary>
        public List<string> SelectedFamilyUniqueIds { get; set; } = new List<string>();

        public string OutputFolder { get; set; } = string.Empty;

        /// <summary>
        /// When true, families are written into a subfolder named after
        /// their category rather than all landing directly in
        /// OutputFolder.
        /// </summary>
        public bool GroupByCategory { get; set; }

        /// <summary>
        /// Skip export entirely if the target RFA already exists, rather
        /// than overwriting it. Overwrite is the default since this is an
        /// explicit, user-initiated export, not a background job.
        /// </summary>
        public bool SkipExistingFiles { get; set; }

        public bool ExportPreviewImage { get; set; }

        /// <summary>
        /// Which format to export the preview image as, only relevant
        /// when ExportPreviewImage is true.
        /// </summary>
        public ExportFormat ImageFormat { get; set; } = ExportFormat.Png;

        /// <summary>
        /// View names to look for inside each family document, in
        /// priority order, e.g. "{3D}" then "Front". The first match found
        /// in a given family is used, not every family template includes
        /// the same views. A family with none of these names present
        /// still exports its RFA, the image is skipped and reported for
        /// that family specifically.
        /// </summary>
        public List<string> PreferredImageViewNames { get; set; } = new List<string> { "{3D}" };

        public ImageSettings ImageSettings { get; set; } = new ImageSettings();
    }
}

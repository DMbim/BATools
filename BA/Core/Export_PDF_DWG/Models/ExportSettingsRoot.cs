using System.Collections.Generic;

namespace BA.Core.Export.Models
{
    /// <summary>
    /// Root persisted settings file for one project, keyed by project number
    /// using the same \d{2}-\d{3} content matching already used for the
    /// Type Data Ledger project set differentiation, so it works identically
    /// for UNC paths, mapped drives and future office locations.
    /// </summary>
    public class ExportSettingsRoot
    {
        public int SchemaVersion { get; set; } = 1;

        public string ProjectNumber { get; set; } = string.Empty;

        public List<ExportJobSettings> Jobs { get; set; } = new List<ExportJobSettings>();
    }
}

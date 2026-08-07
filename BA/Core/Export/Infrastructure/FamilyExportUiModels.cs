using System.Collections.Generic;
using BA.Core.Export.Models;

namespace BA.Core.Export.Infrastructure
{
    public enum FamilyExportUiAction
    {
        GetFamilies,
        RunFamilyExport
    }

    public class FamilyExportUiRequest
    {
        public FamilyExportUiAction Action { get; set; }
        public FamilyExportSettings SettingsForRun { get; set; }
    }

    public class FamilyExportUiResponse
    {
        public FamilyExportUiAction Action { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public List<FamilyInfo> Families { get; set; } = new List<FamilyInfo>();
        public FamilyExportResult RunResult { get; set; }
    }
}

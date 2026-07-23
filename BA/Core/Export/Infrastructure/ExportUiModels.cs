using System.Collections.Generic;
using BA.Core.Export.Models;

namespace BA.Core.Export.Infrastructure
{
    public enum ExportUiAction
    {
        LoadSettings,
        SaveSettings,
        GetSheetSetNames,
        GetDwgExportSetupNames,
        PreviewNaming,
        RunJobNow
    }

    /// <summary>
    /// Carries a request from the WPF ViewModel across to the Revit API
    /// thread via ExportExternalEventHandler. Only the fields relevant to
    /// the given Action are expected to be populated.
    /// </summary>
    public class ExportUiRequest
    {
        public ExportUiAction Action { get; set; }
        public ExportSettingsRoot SettingsToSave { get; set; }
        public ExportJobSettings JobForPreviewOrRun { get; set; }
    }

    /// <summary>
    /// Carries the result of an ExportUiRequest back to the WPF ViewModel.
    /// </summary>
    public class ExportUiResponse
    {
        public ExportUiAction Action { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public ExportSettingsRoot LoadedSettings { get; set; }
        public List<string> StringList { get; set; } = new List<string>();
        public string PreviewFileName { get; set; } = string.Empty;
        public string PreviewFolder { get; set; } = string.Empty;
        public ExportJobResult JobResult { get; set; }
    }
}

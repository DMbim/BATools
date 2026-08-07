using BA.Core.Export.Models;
using System.Collections.Generic;

namespace BA.Core.Export.Infrastructure
{
    public enum ExportUiAction
    {
        LoadSettings,
        SaveSettings,
        GetAllSheets,
        GetAllViews,
        GetSheetParameterNames,
        GetViewParameterNames,
        GetPredefinedDwgSetups,
        GetPredefinedDwgSetupDetails,
        PreviewNaming,
        RunJobNow,
        DiscoverParameterColumns,
        ResolveParameterColumnValues,
        GetPaperSizeInfo
    }

    /// <summary>
    /// Carries a request from the WPF ViewModel across to the Revit API
    /// thread via ExportUiBridge. Only the fields relevant to
    /// the given Action are expected to be populated.
    /// </summary>
    public class ExportUiRequest
    {
        public ExportUiAction Action { get; set; }
        public ExportSettingsRoot SettingsToSave { get; set; }
        public ExportJobSettings JobForPreviewOrRun { get; set; }
        public string SampleSheetNumber { get; set; }

        /// <summary>
        /// Used by GetViewParameterNames, the Views-mode equivalent of
        /// SampleSheetNumber.
        /// </summary>
        public string SampleViewUniqueId { get; set; }

        /// <summary>
        /// Used by GetPredefinedDwgSetupDetails.
        /// </summary>
        public string SetupNameToInspect { get; set; }

        /// <summary>
        /// Used by DiscoverParameterColumns and ResolveParameterColumnValues,
        /// the full set of sheet numbers currently shown in the picker, not
        /// just the checked ones, parameter discovery should reflect the
        /// whole browsed set.
        /// </summary>
        public IList<string> SheetNumbersForColumns { get; set; }

        /// <summary>
        /// Used by ResolveParameterColumnValues, the columns to resolve
        /// values for.
        /// </summary>
        public IList<ParameterColumnDescriptor> ColumnsToResolve { get; set; }
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
        public List<SheetSummary> Sheets { get; set; } = new List<SheetSummary>();

        /// <summary>
        /// Result of GetAllViews.
        /// </summary>
        public List<ViewSummary> Views { get; set; } = new List<ViewSummary>();

        /// <summary>
        /// Result of GetPredefinedDwgSetupDetails, every setting the named
        /// setup actually carries, so the UI can readjust its own controls
        /// to match and display what will really be used, rather than
        /// showing stale or misleading values while that setup is active.
        /// </summary>
        public PredefinedDwgSetupDetails? PredefinedSetupDetails { get; set; }

        /// <summary>
        /// Result of GetPredefinedDwgSetups, names of DWG export setups
        /// already saved in this project (Export Setups DWG/DXF).
        /// </summary>
        public List<string> PredefinedDwgSetupNames { get; set; } = new List<string>();

        /// <summary>
        /// Result of PreviewNaming, one entry per format enabled on the
        /// job being previewed.
        /// </summary>
        public List<NamingPreviewResult> PreviewResults { get; set; } = new List<NamingPreviewResult>();

        /// <summary>
        /// Result of RunJobNow, one entry per format enabled on the job.
        /// </summary>
        public List<ExportJobResult> JobResults { get; set; } = new List<ExportJobResult>();

        /// <summary>
        /// Result of DiscoverParameterColumns.
        /// </summary>
        public List<ParameterColumnCandidate> ParameterColumnCandidates { get; set; } = new List<ParameterColumnCandidate>();

        /// <summary>
        /// Result of ResolveParameterColumnValues, sheetNumber -> (columnKey -> value).
        /// </summary>
        public Dictionary<string, Dictionary<string, string>> ParameterColumnValues { get; set; } = new Dictionary<string, Dictionary<string, string>>();

        /// <summary>
        /// Result of GetPaperSizeInfo, keyed by sheet number.
        /// </summary>
        public Dictionary<string, PaperSizeInfo> PaperSizeInfoBySheet { get; set; } = new Dictionary<string, PaperSizeInfo>();
    }
}
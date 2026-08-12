using System.Collections.Generic;
using Autodesk.Revit.DB;
using BA.Core.Export.Models;

namespace BA.Core.Export.Infrastructure
{
    public enum BookletUiAction
    {
        GetTypesByCategory,
        GetTypesByParameter,
        DiscoverInfoParameters,
        GetTitleBlocks,
        GetLegendViews,
        RunBooklets
    }

    public class BookletUiRequest
    {
        public BookletUiAction Action { get; set; }
        public BuiltInCategory Category { get; set; }
        public string ParameterName { get; set; }
        public IList<string> TypeUniqueIdsForParameterDiscovery { get; set; }
        public BookletSettings SettingsForRun { get; set; }
    }

    public class BookletUiResponse
    {
        public BookletUiAction Action { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public List<BookletTypeInfo> Types { get; set; } = new List<BookletTypeInfo>();
        public List<ParameterColumnCandidate> ParameterCandidates { get; set; } = new List<ParameterColumnCandidate>();
        public List<string> TitleBlockNames { get; set; } = new List<string>();
        public List<string> TitleBlockUniqueIds { get; set; } = new List<string>();
        public List<string> LegendViewNames { get; set; } = new List<string>();
        public List<string> LegendViewUniqueIds { get; set; } = new List<string>();
        public List<BookletOutcome> RunOutcomes { get; set; } = new List<BookletOutcome>();
    }
}

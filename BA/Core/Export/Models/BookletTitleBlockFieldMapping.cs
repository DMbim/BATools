namespace BA.Core.Export.Models
{
    /// <summary>
    /// Maps one source parameter (picked from the selected types, via the
    /// same picker already built for export column selection) to the
    /// literal instance parameter name on the user's own title block
    /// family. Not a fixed set of named slots, title block parameter
    /// names are office specific, this stays fully configurable.
    /// </summary>
    public class BookletTitleBlockFieldMapping
    {
        public string TitleBlockParameterName { get; set; } = string.Empty;
        public ParameterColumnDescriptor SourceField { get; set; }
    }
}

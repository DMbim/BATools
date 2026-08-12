namespace BA.Core.Export.Models
{
    public enum ParameterColumnOccurrence
    {
        All,
        Some,
        One
    }

    /// <summary>
    /// A ParameterColumnDescriptor found while scanning the current sheet
    /// set, plus how widely it occurs across that set. Only used inside the
    /// Add Parameter Column picker, never persisted, ParameterColumnLayout
    /// stores plain ParameterColumnDescriptor entries.
    /// </summary>
    public class ParameterColumnCandidate : ParameterColumnDescriptor
    {
        public ParameterColumnOccurrence Occurrence { get; set; }
    }
}

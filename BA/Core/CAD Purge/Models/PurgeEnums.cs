// File: BA_Tools/CadPurge/Models/PurgeEnums.cs
namespace BA.CadPurge.Models
{
    /// <summary>
    /// Classifies what kind of purge/mapping candidate a scanned Revit element represents.
    /// </summary>
    public enum PurgeItemType
    {
        LinePattern,
        TextStyle,
        DwgImport
    }

    /// <summary>
    /// The action a BIM manager has requested for a given PurgeCandidate. Set by the ViewModel
    /// in response to UI selection; consumed by PurgeBatchExecutor (Stage 3).
    /// </summary>
    public enum PurgeAction
    {
        None,
        Delete,
        MapToStandard
    }

    /// <summary>
    /// Where the resolved mapping target element came from, or whether resolution failed.
    /// </summary>
    public enum MappingTargetSource
    {
        Unresolved,
        AlreadyInProject,
        LoadedFromTemplate,
        NotFoundInTemplate
    }

    /// <summary>
    /// Lifecycle status of a PurgeCandidate through the scan -&gt; resolve -&gt; execute pipeline.
    /// </summary>
    public enum PurgeCandidateStatus
    {
        Scanned,
        ActionApplied,
        ActionFailed
    }
}
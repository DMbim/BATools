// File: BA_Tools/VisibilityDiagnostics/Models/CheckStatus.cs
namespace BA.VisibilityDiagnostics.Models
{
    /// <summary>
    /// Outcome of a single visibility check against one element/view pair.
    /// </summary>
    public enum CheckStatus
    {
        /// <summary>This check does not explain the element being hidden - it is visible with respect to this check.</summary>
        Visible,

        /// <summary>This check found a confirmed cause of the element being hidden.</summary>
        Hidden,

        /// <summary>The check ran but could not reach a firm conclusion (e.g. missing geometry, ambiguous state).</summary>
        Warning,

        /// <summary>This check does not apply to this element/view combination (e.g. View Range on a 3D view).</summary>
        NotApplicable,

        /// <summary>The check itself threw an exception while evaluating.</summary>
        Error
    }
}

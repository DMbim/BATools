// File: BA_Tools/VisibilityDiagnostics/Services/IVisibilityCheck.cs
using Autodesk.Revit.DB;
using BA.VisibilityDiagnostics.Models;

namespace BA.VisibilityDiagnostics.Services
{
    /// <summary>
    /// One independent visibility check (View Range, Category/VG, Phase Filter,
    /// Design Option, Workset, ...). Implementations MUST only be evaluated from
    /// within a valid Revit API context (inside IExternalCommand.Execute, an
    /// IExternalEventHandler.Execute callback, or Idling) - never from WPF UI code
    /// directly, and must never start a Transaction (read-only diagnostics only).
    /// </summary>
    public interface IVisibilityCheck
    {
        /// <summary>Short, stable display name for this check (used as a UI column value).</summary>
        string CheckName { get; }

        /// <summary>
        /// Evaluate this single check for the given element in the given view.
        /// Must not throw for expected/ordinary conditions (return
        /// VisibilityCheckItem.Warning/NotApplicable/Error instead) - the
        /// coordinating service treats an escaping exception as a bug in the check
        /// itself, not a diagnostic outcome.
        /// </summary>
        VisibilityCheckItem Evaluate(Document document, View view, Element element);
    }
}

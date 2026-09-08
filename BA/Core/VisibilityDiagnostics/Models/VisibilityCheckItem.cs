// File: BA_Tools/VisibilityDiagnostics/Models/VisibilityCheckItem.cs
using System;

namespace BA.VisibilityDiagnostics.Models
{
    /// <summary>
    /// Result of one visibility check (View Range, Category, Phase Filter, Design
    /// Option, Workset, ...) evaluated against a single element/view pair.
    /// Immutable POCO - safe to pass across the ExternalEvent/UI-thread boundary.
    /// </summary>
    public sealed class VisibilityCheckItem
    {
        public string CheckName { get; }
        public CheckStatus Status { get; }
        public string Detail { get; }
        public string? Recommendation { get; }

        public VisibilityCheckItem(string checkName, CheckStatus status, string detail, string? recommendation = null)
        {
            CheckName = checkName ?? throw new ArgumentNullException(nameof(checkName));
            Status = status;
            Detail = detail ?? throw new ArgumentNullException(nameof(detail));
            Recommendation = recommendation;
        }

        public static VisibilityCheckItem Ok(string checkName, string detail)
            => new(checkName, CheckStatus.Visible, detail);

        public static VisibilityCheckItem Hidden(string checkName, string detail, string? recommendation = null)
            => new(checkName, CheckStatus.Hidden, detail, recommendation);

        public static VisibilityCheckItem Warning(string checkName, string detail, string? recommendation = null)
            => new(checkName, CheckStatus.Warning, detail, recommendation);

        public static VisibilityCheckItem NotApplicable(string checkName, string detail)
            => new(checkName, CheckStatus.NotApplicable, detail);

        public static VisibilityCheckItem Error(string checkName, string detail)
            => new(checkName, CheckStatus.Error, detail);
    }
}

// File: BA_Tools/UI/BimHub/Models/BimHubHealthSnapshot.cs
using System;

namespace BA.UI.BimHub.Models
{
    /// <summary>
    /// Immutable snapshot of project health data shown in the hub header card.
    /// Populate via BimHubHealthService.Collect(). All fields are safe defaults
    /// so the card renders even when collection partially fails.
    /// </summary>
    public sealed class BimHubHealthSnapshot
    {
        public int ParamsLoaded { get; init; } = 0;
        public int QaWarnings { get; init; } = 0;
        public int QaErrors { get; init; } = 0;
        public string TemplateVersion { get; init; } = "—";
        public DateTime CheckedAt { get; init; } = DateTime.Now;

        /// <summary>
        /// True when QaErrors > 0. Drives status dot colour in the view.
        /// </summary>
        public bool HasErrors => QaErrors > 0;

        /// <summary>
        /// True when QaWarnings > 0 and no errors. Drives amber dot.
        /// </summary>
        public bool HasWarnings => QaWarnings > 0 && !HasErrors;

        public string StatusSummary =>
            HasErrors
                ? $"{QaErrors} error{(QaErrors != 1 ? "s" : "")} require attention"
                : HasWarnings
                    ? $"{QaWarnings} warning{(QaWarnings != 1 ? "s" : "")} found"
                    : "No issues found";

        public string CheckedAtFormatted =>
            CheckedAt.Date == DateTime.Today
                ? $"today {CheckedAt:HH:mm}"
                : CheckedAt.ToString("dd MMM HH:mm");
    }
}
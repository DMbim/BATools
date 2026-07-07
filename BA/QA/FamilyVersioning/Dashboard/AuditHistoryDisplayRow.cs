using System;
using BA.QA.FamilyVersioning.Models;

namespace BA.QA.FamilyVersioning.Dashboard
{
    public sealed class AuditHistoryDisplayRow
    {
        public string BuildingName { get; }
        public string EventType { get; }
        public string EventUser { get; }
        public DateTime EventLocalTime { get; }
        public string EventLocalTimeDisplay => EventLocalTime.ToString("dd MMM yyyy HH:mm");

        /// <summary>
        /// User-provided comment from the confirm dialog. Empty string if no comment
        /// was entered. For pre-v3 rows where Detail contained the combined string,
        /// the full Detail is surfaced here since splitting is not attempted.
        /// </summary>
        public string Comment { get; }

        /// <summary>
        /// Structural diff summary from FamilyMetadataDiff.ToSummaryString().
        /// Null for pre-v3 rows. Shown as a tooltip on hover over the comment cell.
        /// </summary>
        public string? DiffSummary { get; }

        public bool HasDiffSummary => !string.IsNullOrWhiteSpace(DiffSummary);

        public AuditHistoryDisplayRow(AuditLogEntry entry, string buildingName)
        {
            BuildingName = buildingName ?? "Unknown";
            EventType = entry.EventType;
            EventUser = entry.EventUser;
            EventLocalTime = entry.EventUtc.ToLocalTime();
            Comment = entry.Detail ?? string.Empty;
            DiffSummary = entry.DiffSummary;
        }
    }
}

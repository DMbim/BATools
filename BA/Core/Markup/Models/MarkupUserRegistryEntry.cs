// BA/Markup/Models/MarkupUserRegistryEntry.cs
using System;

namespace BA.Markup.Models
{
    /// <summary>
    /// One entry in the per-central markup assignee registry, stored at
    /// S:\CAD\Autodesk Revit\_admin\BA_tools\MarkupUsers\{ProjectSet}\.
    /// A user is considered active if LastSeenUtc is within the configured
    /// retention window (see MarkupSettings); MarkupCleanupCommand purges
    /// entries older than that window and clears any BA_Tls_AssignedUser
    /// values pointing at the purged username.
    /// </summary>
    public sealed class MarkupUserRegistryEntry
    {
        /// <summary>
        /// Revit username as returned by Application.Username. This is the
        /// exact string written into BA_Tls_AssignedUser at assignment time,
        /// and the exact string MarkupScanService filters against, so it
        /// must never be normalized, trimmed of case, or display-formatted
        /// here — do that only in the WPF layer if ever needed.
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// UTC timestamp of the most recent successful SynchronizeWithCentral
        /// recorded for this user on this central model. Updated in place by
        /// MarkupUserRegistryService.RecordParticipation on every sync, not
        /// appended as a new entry.
        /// </summary>
        public DateTime LastSeenUtc { get; set; }
    }
}
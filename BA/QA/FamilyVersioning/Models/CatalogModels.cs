using System;

namespace BA.QA.FamilyVersioning.Models
{
    /// <summary>
    /// Semantic version bump classification applied to a family change.
    /// Stored as TEXT in the catalog (enum name), not as an integer, so the database
    /// remains human-readable when inspected directly with a SQLite browser.
    /// </summary>
    public enum FamilyBumpKind
    {
        Unknown = 0,
        Patch = 1,
        Minor = 2,
        Major = 3
    }

    /// <summary>
    /// Status of a queued reload request. See PendingRequests table.
    /// </summary>
    public enum PendingRequestStatus
    {
        Pending,
        Completed,
        Rejected
    }

    /// <summary>
    /// Single-row table holding project-level metadata for this catalog database.
    /// One catalog database corresponds to one project, which may span multiple
    /// building central models.
    /// </summary>
    public sealed class ProjectInfo
    {
        public string ProjectName { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; }
        public string? SharedParameterFilePath { get; set; }
    }

    /// <summary>
    /// A single building's central model within the project. CentralModelPath is a
    /// local/UNC filesystem path to the central RVT. Enabled = false excludes this
    /// building from active scanning without deleting its history.
    /// </summary>
    public sealed class Building
    {
        public int BuildingId { get; set; }
        public string BuildingName { get; set; } = string.Empty;
        public string CentralModelPath { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime ModifiedUtc { get; set; }
    }

    /// <summary>
    /// A tracked family. CanonicalVersion/CanonicalHash represent the office's
    /// designated "should be" state, distinct from what is actually loaded in any
    /// one building (see FamilyBuildingState).
    /// </summary>
    public sealed class TrackedFamily
    {
        public int FamilyId { get; set; }
        public string FamilyName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string CanonicalVersion { get; set; } = "0.0.0";
        public string CanonicalHash { get; set; } = string.Empty;
        public string? CanonicalSourcePath { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime ModifiedUtc { get; set; }
    }

    /// <summary>
    /// The state of a specific family as last observed loaded into a specific building's
    /// central model. This is what Phase 1's DocumentChanged hook writes to.
    /// </summary>
    public sealed class FamilyBuildingState
    {
        public int StateId { get; set; }
        public int FamilyId { get; set; }
        public int BuildingId { get; set; }
        public string LoadedVersion { get; set; } = "0.0.0";
        public string LoadedHash { get; set; } = string.Empty;
        public string? LastLoadedByUser { get; set; }
        public DateTime? LastLoadedUtc { get; set; }
        public FamilyBumpKind LastBumpKind { get; set; } = FamilyBumpKind.Unknown;
        public string? LastDiffSummary { get; set; }
    }

    /// <summary>
    /// An approved, intentional divergence of a family's state in a specific building
    /// from the canonical version (e.g. a fire-rated door variant unique to one building).
    /// Only one Active=true row may exist per (FamilyId, BuildingId) pair, enforced by
    /// a partial unique index in the schema. The repository layer must check for an
    /// existing active exception before inserting a new one, the database constraint
    /// is the backstop, not the primary guard.
    /// </summary>
    public sealed class FamilyException
    {
        public int ExceptionId { get; set; }
        public int FamilyId { get; set; }
        public int BuildingId { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string ApprovedByUser { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; }
        public bool Active { get; set; }
    }

    /// <summary>
    /// A queued request, raised from the coordination dashboard, asking a specific
    /// building to reload a family to a specific version. The reload itself is executed
    /// later, inside that building's own session, never directly by the requester.
    /// See architecture notes: this table exists specifically to avoid any code path
    /// that opens or writes to another building's central directly from the coordination
    /// model's session.
    /// </summary>
    public sealed class PendingRequest
    {
        public int RequestId { get; set; }
        public int FamilyId { get; set; }
        public int TargetBuildingId { get; set; }
        public string RequestedVersion { get; set; } = string.Empty;
        public string RequestedHash { get; set; } = string.Empty;
        public string RequestedByUser { get; set; } = string.Empty;
        public DateTime RequestedUtc { get; set; }
        public PendingRequestStatus Status { get; set; } = PendingRequestStatus.Pending;
        public string? ResolvedByUser { get; set; }
        public DateTime? ResolvedUtc { get; set; }
        public string? ResolutionNote { get; set; }
    }

    /// <summary>
    /// Append-only audit trail entry. Every meaningful catalog event is logged here
    /// regardless of which other tables it also touches, so the full history of a
    /// family's life across the project can be reconstructed even if other rows
    /// are later updated in place.
    /// </summary>
    public sealed class AuditLogEntry
    {
        public int AuditId { get; set; }
        public int? FamilyId { get; set; }
        public int BuildingId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public DateTime EventUtc { get; set; }
        public string EventUser { get; set; } = string.Empty;

        /// <summary>
        /// User-provided comment entered in the confirm dialog. Stored separately
        /// from DiffSummary since v3. Pre-v3 rows have the combined string here.
        /// </summary>
        public string? Detail { get; set; }

        /// <summary>
        /// Structural diff summary generated by FamilyMetadataDiff.ToSummaryString().
        /// Null for rows written before the v3 schema migration.
        /// </summary>
        public string? DiffSummary { get; set; }
    }

    /// <summary>
    /// A Revit family category that is actively tracked by the detection hook.
    /// BuiltInCategoryId is the integer value of Autodesk.Revit.DB.BuiltInCategory
    /// and is the authoritative filter key used in DocumentChanged. CategoryLabel
    /// is the display name cached at add time, used only for UI, not for filtering.
    /// </summary>
    public sealed class TrackedCategory
    {
        public int TrackedCategoryId { get; set; }
        public int BuiltInCategoryId { get; set; }
        public string CategoryLabel { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public DateTime CreatedUtc { get; set; }
    }

    /// <summary>
    /// Well-known audit event type strings. Kept as constants rather than an enum
    /// because AuditLog.EventType is intentionally free-text in the schema, future
    /// event types can be added without a schema or enum change, this class just
    /// gives compile-time safety for the known set.
    /// </summary>
    public static class AuditEventType
    {
        public const string Detected = "Detected";
        public const string Confirmed = "Confirmed";
        public const string Overridden = "Overridden";
        public const string ExceptionMarked = "ExceptionMarked";
        public const string ReloadRequested = "ReloadRequested";
        public const string ReloadCompleted = "ReloadCompleted";
        public const string ReloadRejected = "ReloadRejected";
    }
}

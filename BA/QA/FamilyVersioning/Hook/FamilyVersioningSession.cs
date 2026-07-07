using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using BA.QA.FamilyVersioning.Data;
using BA.QA.FamilyVersioning.Engine;
using BA.QA.FamilyVersioning.Models;

namespace BA.QA.FamilyVersioning.Hook
{
    /// <summary>
    /// Holds the resolved catalog context for one open Revit document. Created by
    /// FamilyVersioningDocumentHook when a document opens successfully (catalog path
    /// resolves, building row found in catalog), and discarded when the document
    /// closes. Cached keyed by document path so DocumentChanged events can look up
    /// the session without re-resolving the catalog on every event.
    ///
    /// Thread safety: PendingDetections is a ConcurrentQueue since the ExternalEvent
    /// handler that drains it runs on a different scheduling cycle than the
    /// DocumentChanged handler that enqueues to it, even though both nominally run
    /// on the Revit API thread, the queue boundary makes the handoff explicit and
    /// safe against any future threading model changes.
    /// </summary>
    public sealed class FamilyVersioningSession
    {
        public string DocumentPath { get; }
        public int BuildingId { get; }
        public string BuildingName { get; }
        public CatalogConnectionFactory CatalogFactory { get; }
        public ConcurrentQueue<PendingDetection> PendingDetections { get; } = new();

        /// <summary>
        /// Set of enabled BuiltInCategory integer values loaded once at session start.
        /// The DocumentChanged hook checks family.FamilyCategory.Id.Value against this
        /// set before queuing a detection. Empty set means no filter is configured yet
        /// (first run before categories are seeded), in which case all families pass.
        /// </summary>
        public HashSet<int> TrackedCategoryIds { get; }

        public FamilyVersioningSession(
            string documentPath,
            int buildingId,
            string buildingName,
            CatalogConnectionFactory catalogFactory,
            HashSet<int> trackedCategoryIds)
        {
            DocumentPath = documentPath ?? throw new ArgumentNullException(nameof(documentPath));
            BuildingId = buildingId;
            BuildingName = buildingName ?? throw new ArgumentNullException(nameof(buildingName));
            CatalogFactory = catalogFactory ?? throw new ArgumentNullException(nameof(catalogFactory));
            TrackedCategoryIds = trackedCategoryIds ?? new HashSet<int>();
        }
    }

    /// <summary>
    /// A single detected family change event waiting for user confirmation. Holds
    /// everything the confirm dialog needs to display and everything the repositories
    /// need to persist the result. Created by the DocumentChanged handler, consumed
    /// by the ExternalEvent confirm dialog handler.
    /// </summary>
    public sealed class PendingDetection
    {
        public Autodesk.Revit.DB.ElementId FamilyElementId { get; }
        public string FamilyName { get; }
        public string CategoryName { get; }
        public FamilyMetadataSnapshot NewSnapshot { get; }
        public FamilyMetadataSnapshot? PreviousSnapshot { get; }
        public FamilyMetadataDiff Diff { get; }
        public FamilyBumpKind InferredBumpKind { get; }
        public string SuggestedVersion { get; }
        public string CurrentCatalogVersion { get; }
        public int FamilyId { get; }
        public DateTime DetectedUtc { get; }

        public PendingDetection(
            Autodesk.Revit.DB.ElementId familyElementId,
            string familyName,
            string categoryName,
            FamilyMetadataSnapshot newSnapshot,
            FamilyMetadataSnapshot? previousSnapshot,
            FamilyMetadataDiff diff,
            FamilyBumpKind inferredBumpKind,
            string suggestedVersion,
            string currentCatalogVersion,
            int familyId)
        {
            FamilyElementId = familyElementId;
            FamilyName = familyName;
            CategoryName = categoryName;
            NewSnapshot = newSnapshot;
            PreviousSnapshot = previousSnapshot;
            Diff = diff;
            InferredBumpKind = inferredBumpKind;
            SuggestedVersion = suggestedVersion;
            CurrentCatalogVersion = currentCatalogVersion;
            FamilyId = familyId;
            DetectedUtc = DateTime.UtcNow;
        }
    }
}

// File: BA/Core/CurveToElement/Services/WallGenerationService.cs
// Action: REPLACE (full file)

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using BA.BAApplication;
using BA.Core.CurveToElement.Models;

namespace BA.Core.CurveToElement.Services
{
    /// <summary>
    /// Performs the actual document write: creates Wall elements from classified/chained
    /// curve groups inside a single Transaction. Must be called from a valid Revit API
    /// execution context (ExternalEvent handler), never directly from UI code.
    ///
    /// Per-wall failures are caught individually and reported as warnings rather than
    /// aborting the whole operation - one bad curve should not cost the user 40 other
    /// successfully created walls. The entire operation is still one Transaction, so it is
    /// one undo step regardless of how many individual walls succeeded or failed.
    ///
    /// Optional source-line deletion (deleteSourceLinesAfterCreation) happens at GROUP
    /// granularity only, not per curve. CurveChainBuilder does not thread ClassifiableCurve's
    /// SourceElementId through CurveChain.OrderedSegments (those are pure Curve geometry), so
    /// there is no reliable way to map an individual failed Wall.Create back to the specific
    /// source ElementId that produced it. Rather than guess, a group's source lines are only
    /// deleted if every curve in that group produced a wall; a partial failure leaves the
    /// whole group's lines untouched and reports why in the warnings list.
    /// </summary>
    public class WallGenerationService
    {
        private readonly WallLocationLineResolver _locationLineResolver;
        private readonly WallOrientationResolver _orientationResolver;

        public WallGenerationService()
        {
            _locationLineResolver = new WallLocationLineResolver();
            _orientationResolver = new WallOrientationResolver();
        }

        public GenerationResult Execute(Document doc, IReadOnlyList<GroupGenerationRequest> requests, bool deleteSourceLinesAfterCreation)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (requests == null) throw new ArgumentNullException(nameof(requests));

            if (requests.Count == 0)
                return new GenerationResult(false, "No groups to generate.", 0, new List<string>(), 0);

            var warnings = new List<string>();
            int createdCount = 0;
            int deletedLineCount = 0;

            using (Transaction transaction = new Transaction(doc, "Generate Walls from Curves"))
            {
                try
                {
                    transaction.Start();

                    var elementIdsToDelete = new List<ElementId>();

                    foreach (GroupGenerationRequest request in requests)
                    {
                        int createdInGroup = ProcessGroup(doc, request, warnings);
                        createdCount += createdInGroup;

                        if (deleteSourceLinesAfterCreation)
                        {
                            CollectGroupCurvesForDeletion(request, createdInGroup, elementIdsToDelete, warnings);
                        }
                    }

                    if (deleteSourceLinesAfterCreation && elementIdsToDelete.Count > 0)
                    {
                        deletedLineCount = TryDeleteSourceLines(doc, elementIdsToDelete, warnings);
                    }

                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("WallGenerationService.Execute: unhandled exception, rolling back transaction", ex);

                    if (transaction.GetStatus() == TransactionStatus.Started)
                        transaction.RollBack();

                    return new GenerationResult(
                        false,
                        $"Wall generation failed and was rolled back: {ex.Message}",
                        0,
                        warnings,
                        0);
                }
            }

            string message = BuildSuccessMessage(createdCount, deletedLineCount, deleteSourceLinesAfterCreation);
            return new GenerationResult(createdCount > 0, message, createdCount, warnings, deletedLineCount);
        }

        /// <summary>
        /// Adds a group's source ElementIds to the delete list only if every curve in the
        /// group produced a wall. If some curves failed (createdInGroup less than the group's
        /// curve count, but greater than zero) nothing in the group is queued for deletion, and
        /// a warning explains why - deleting a line whose wall never got created would silently
        /// destroy the user's only remaining reference to that geometry.
        /// </summary>
        private void CollectGroupCurvesForDeletion(
            GroupGenerationRequest request,
            int createdInGroup,
            List<ElementId> elementIdsToDelete,
            List<string> warnings)
        {
            int totalCurvesInGroup = request.Group.Curves.Count;

            if (createdInGroup == totalCurvesInGroup && totalCurvesInGroup > 0)
            {
                foreach (ClassifiableCurve curve in request.Group.Curves)
                {
                    elementIdsToDelete.Add(curve.SourceElementId);
                }
            }
            else if (createdInGroup > 0)
            {
                int failedCount = totalCurvesInGroup - createdInGroup;
                warnings.Add(
                    $"Group '{request.Group.StyleName}': {failedCount} of {totalCurvesInGroup} source line(s) did not " +
                    "produce a wall. None of this group's source lines were deleted, to avoid losing geometry that " +
                    "has no corresponding wall.");
            }
        }

        /// <summary>
        /// Batch-deletes the collected source detail lines in a single Document.Delete call.
        /// Wrapped in its own try/catch so a delete failure (unlikely, but Document.Delete can
        /// throw for ids Revit refuses to remove) reports a warning instead of rolling back the
        /// whole transaction and discarding walls that already committed successfully.
        /// </summary>
        private int TryDeleteSourceLines(Document doc, List<ElementId> elementIdsToDelete, List<string> warnings)
        {
            var validIds = new List<ElementId>(elementIdsToDelete.Count);
            foreach (ElementId id in elementIdsToDelete)
            {
                if (doc.GetElement(id) != null)
                    validIds.Add(id);
            }

            if (validIds.Count == 0)
                return 0;

            try
            {
                doc.Delete(validIds);
                return validIds.Count;
            }
            catch (Exception ex)
            {
                string warning = $"Wall(s) were created successfully, but deleting the source detail line(s) failed: {ex.Message}";
                AppLogger.LogError("WallGenerationService.TryDeleteSourceLines: " + warning, ex);
                warnings.Add(warning);
                return 0;
            }
        }

        private static string BuildSuccessMessage(int createdCount, int deletedLineCount, bool deleteRequested)
        {
            if (createdCount == 0)
                return "No walls were created. Check warnings for details.";

            if (!deleteRequested || deletedLineCount == 0)
                return $"Created {createdCount} wall(s).";

            return $"Created {createdCount} wall(s) and removed {deletedLineCount} source detail line(s).";
        }

        private int ProcessGroup(Document doc, GroupGenerationRequest request, List<string> warnings)
        {
            int createdInGroup = 0;
            string groupLabel = request.Group.StyleName;

            foreach (CurveChain chain in request.Chains)
            {
                createdInGroup += ProcessChain(doc, chain, request.Settings, groupLabel, warnings);
            }

            return createdInGroup;
        }

        private int ProcessChain(Document doc, CurveChain chain, WallGroupSettings settings, string groupLabel, List<string> warnings)
        {
            XYZ loopNormal = chain.IsClosed ? chain.ComputeNewellNormal() : null;

            var createdWalls = new List<Wall>();

            foreach (Curve segmentCurve in chain.OrderedSegments)
            {
                Wall wall = TryCreateSingleWall(doc, segmentCurve, settings, groupLabel, warnings);
                if (wall == null)
                    continue;

                doc.Regenerate(); // <- NEW: Wall.Orientation and face-dependent parameter writes
                                  // require computed geometry, which does not exist until
                                  // regeneration happens - Wall.Create() alone does not trigger it
                                  // within the same transaction. Must regenerate before touching
                                  // orientation or location-line parameters on this wall.

                ApplyLocationLine(wall, settings, groupLabel, warnings);
                ApplyOrientation(wall, segmentCurve, chain, loopNormal, settings);

                createdWalls.Add(wall);
            }

            if (createdWalls.Count == 0)
                return 0;

            ApplyEndJoinConditions(chain, settings, createdWalls, groupLabel, warnings);

            return createdWalls.Count;
        }

        private Wall TryCreateSingleWall(Document doc, Curve segmentCurve, WallGroupSettings settings, string groupLabel, List<string> warnings)
        {
            try
            {
                Wall wall = Wall.Create(
                    doc,
                    segmentCurve,
                    settings.WallTypeId,
                    settings.BaseLevelId,
                    settings.HeightMode == WallHeightMode.Unconnected ? settings.UnconnectedHeight : settings.UnconnectedHeight,
                    settings.BaseOffset,
                    false,   // flip is resolved and applied explicitly afterward, not passed here
                    settings.StructuralUsage);

                if (settings.HeightMode == WallHeightMode.UpToLevel)
                {
                    ApplyUpToLevelConstraint(wall, settings, groupLabel, warnings);
                }

                return wall;
            }
            catch (Exception ex)
            {
                string warning = $"Group '{groupLabel}': failed to create wall from curve - {ex.Message}";
                AppLogger.LogError($"WallGenerationService.TryCreateSingleWall: {warning}", ex);
                warnings.Add(warning);
                return null;
            }
        }

        private void ApplyUpToLevelConstraint(Wall wall, WallGroupSettings settings, string groupLabel, List<string> warnings)
        {
            Parameter heightTypeParam = wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE);
            Parameter topOffsetParam = wall.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET);

            if (heightTypeParam == null || heightTypeParam.IsReadOnly)
            {
                warnings.Add($"Group '{groupLabel}': could not set top-level constraint on wall {wall.Id.Value} (WALL_HEIGHT_TYPE unavailable). Wall left at unconnected height.");
                return;
            }

            bool typeSet = heightTypeParam.Set(settings.TopLevelId);
            if (!typeSet)
            {
                warnings.Add($"Group '{groupLabel}': Revit rejected top-level constraint on wall {wall.Id.Value}. Wall left at unconnected height.");
                return;
            }

            if (topOffsetParam != null && !topOffsetParam.IsReadOnly)
            {
                topOffsetParam.Set(settings.TopOffset);
            }
        }

        private void ApplyLocationLine(Wall wall, WallGroupSettings settings, string groupLabel, List<string> warnings)
        {
            bool success = _locationLineResolver.TryApplyLocationLine(wall, settings.LocationLine, out string failureReason);
            if (!success && failureReason != null)
            {
                warnings.Add($"Group '{groupLabel}': {failureReason}");
            }
        }

        private void ApplyOrientation(Wall wall, Curve segmentCurve, CurveChain chain, XYZ loopNormal, WallGroupSettings settings)
        {
            bool shouldFlip;

            if (chain.IsClosed)
            {
                shouldFlip = _orientationResolver.ShouldFlipForClosedLoop(wall, segmentCurve, loopNormal);
            }
            else
            {
                shouldFlip = settings.FlipSide;
            }

            if (shouldFlip)
            {
                wall.Flip();
            }
        }

        /// <summary>
        /// AllowEndJoins governs only the two exposed ends of an OPEN chain (the very start of
        /// the first segment and the very end of the last segment) - joints between consecutive
        /// segments within the chain are always left free to join, since that is what makes the
        /// chain read as one continuous run. Closed chains have no exposed ends, so this is a
        /// no-op for them regardless of the setting.
        /// </summary>
        private void ApplyEndJoinConditions(CurveChain chain, WallGroupSettings settings, List<Wall> createdWalls, string groupLabel, List<string> warnings)
        {
            if (chain.IsClosed || settings.AllowEndJoins || createdWalls.Count == 0)
                return;

            try
            {
                WallUtils.DisallowWallJoinAtEnd(createdWalls[0], 0);
                WallUtils.DisallowWallJoinAtEnd(createdWalls[createdWalls.Count - 1], 1);
            }
            catch (Exception ex)
            {
                string warning = $"Group '{groupLabel}': failed to apply end-join restriction - {ex.Message}";
                AppLogger.LogError($"WallGenerationService.ApplyEndJoinConditions: {warning}", ex);
                warnings.Add(warning);
            }
        }
    }
}
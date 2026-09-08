using System;
using System.Collections.Generic;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.Core;

namespace BA.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_WindowOrientation : IExternalCommand
    {
        private const string OrientationParameterName = "BA_Orientation";
        private const string SharedParamFilePath =
            @"S:\CAD\Autodesk Revit\BA_Resources\BA_Shared parameters\BA_SharedParametersWIP2.txt";

        // Flip this to true only after validating against a window or wall of known
        // compass orientation. ProjectPosition.Angle sign convention (clockwise from
        // True North to Project North, or the reverse) must be confirmed per project,
        // not assumed.
        private const bool InvertTrueNorthCorrectionSign = false;

        // Margin added beyond the host wall's face when probing for a Room, so the
        // probe point clearly lands past the wall solid rather than inside it.
        private const double RoomProbeMarginMillimeters = 300.0;

        // Used only if a host wall's width cannot be read, which should not normally
        // happen for a wall hosted window.
        private const double FallbackHalfWallWidthFeet = 0.5;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return Run(commandData.Application, ref message);
        }

        public static Result Run(UIApplication uiApp, ref string message)
        {
            var uiDoc = uiApp.ActiveUIDocument;
            if (uiDoc == null)
            {
                message = "No active document.";
                return Result.Failed;
            }

            var doc = uiDoc.Document;
            var app = uiApp.Application;

            // ---------------------------------------------------------- //
            //  Target set: an explicit selection is honored first, for
            //  spot checking a handful of windows. With nothing selected,
            //  every window in the whole document is processed.
            // ---------------------------------------------------------- //
            var targetIds = new List<ElementId>();
            var selectedIds = uiDoc.Selection.GetElementIds();

            if (selectedIds.Count > 0)
            {
                targetIds.AddRange(selectedIds);
            }
            else
            {
                var docCollector = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Windows)
                    .WhereElementIsNotElementType();

                foreach (var el in docCollector)
                    targetIds.Add(el.Id);

                if (targetIds.Count == 0)
                {
                    TaskDialog.Show("Window Orientation", "No windows found in this document.");
                    return Result.Cancelled;
                }
            }

            int updatedCount = 0;
            int unvalidatedCount = 0;
            double trueNorthCorrectionDegrees = 0.0;
            var skippedNotWindow = new List<string>();
            var skippedNoParameter = new List<string>();
            var skippedReadOnly = new List<string>();
            var skippedNoOrientation = new List<string>();
            var correctedFacingMismatch = new List<string>();

            using var tx = new Transaction(doc, "BA Window Orientation");
            tx.Start();

            try
            {
                // ---------------------------------------------------------- //
                //  Ensure BA_Orientation is bound to the Windows category.
                //  If it is not present we load it from the shared parameter
                //  file by name and create an instance binding.
                // ---------------------------------------------------------- //
                if (!EnsureParameterBound(doc, app, out string bindError))
                {
                    tx.RollBack();
                    message = bindError;
                    return Result.Failed;
                }

                trueNorthCorrectionDegrees = GetTrueNorthCorrectionDegrees(doc);

                // ---------------------------------------------------------- //
                //  Pre pass: resolve a facing vector for every target
                //  window, then classify each against Room data first,
                //  falling back to host wall peers where the room probe
                //  cannot resolve.
                // ---------------------------------------------------------- //
                var candidates = new List<(FamilyInstance Instance, XYZ Facing)>();
                foreach (var id in targetIds)
                {
                    var el = doc.GetElement(id);
                    if (el is not FamilyInstance candidateInstance ||
                        candidateInstance.Category == null ||
                        candidateInstance.Category.Id.Value != (long)BuiltInCategory.OST_Windows)
                        continue;

                    if (TryGetFacingOrientation(candidateInstance, out XYZ candidateFacing))
                        candidates.Add((candidateInstance, candidateFacing));
                }

                var alignmentByInstance = ClassifyFacingAlignment(doc, candidates);

                // ---------------------------------------------------------- //
                //  Process target elements
                // ---------------------------------------------------------- //
                foreach (var id in targetIds)
                {
                    var element = doc.GetElement(id);
                    if (element == null) continue;

                    if (element is not FamilyInstance familyInstance ||
                        familyInstance.Category == null ||
                        familyInstance.Category.Id.Value != (long)BuiltInCategory.OST_Windows)
                    {
                        skippedNotWindow.Add(DescribeElement(element));
                        continue;
                    }

                    var orientationParam = familyInstance.LookupParameter(OrientationParameterName);
                    if (orientationParam == null)
                    {
                        skippedNoParameter.Add(DescribeElement(familyInstance));
                        continue;
                    }

                    if (orientationParam.IsReadOnly)
                    {
                        skippedReadOnly.Add(DescribeElement(familyInstance));
                        continue;
                    }

                    if (!TryGetFacingOrientation(familyInstance, out XYZ facing))
                    {
                        skippedNoOrientation.Add(DescribeElement(familyInstance));
                        continue;
                    }

                    // ---------------------------------------------------------- //
                    //  A window whose current facing lands inside a Room (or
                    //  disagrees with a Room confirmed peer) is treated as
                    //  flipped. The value is written from the MIRRORED
                    //  facing vector so the parameter matches the actual
                    //  facade direction. This does NOT change FacingFlipped
                    //  or any model geometry, the instance itself still
                    //  needs its flip corrected separately.
                    // ---------------------------------------------------------- //
                    HostAlignmentResult alignment = alignmentByInstance.TryGetValue(
                        familyInstance.Id, out var resolvedAlignment)
                        ? resolvedAlignment
                        : HostAlignmentResult.Unvalidated;

                    if (alignment == HostAlignmentResult.Mismatched)
                    {
                        string hostDescription = familyInstance.Host != null
                            ? $"host Wall Id {familyInstance.Host.Id.Value}"
                            : "no host";
                        AppLogger.LogInfo(
                            $"Cmd_WindowOrientation: facing mismatch on " +
                            $"{DescribeElement(familyInstance)}, {hostDescription}. " +
                            "Value written from the mirrored direction; the instance's flip " +
                            "itself was left untouched and still needs correcting.");
                        facing = new XYZ(-facing.X, -facing.Y, facing.Z);
                        correctedFacingMismatch.Add(DescribeElement(familyInstance));
                    }
                    else if (alignment == HostAlignmentResult.Unvalidated)
                    {
                        unvalidatedCount++;
                    }

                    double degrees = VectorToDegreesFromNorth(facing, trueNorthCorrectionDegrees);
                    if (!TryWriteOrientationValue(orientationParam, degrees))
                    {
                        skippedNoOrientation.Add(DescribeElement(familyInstance));
                        continue;
                    }

                    updatedCount++;
                }

                tx.Commit();
            }
            catch (Exception ex)
            {
                if (tx.GetStatus() == TransactionStatus.Started)
                    tx.RollBack();
                message = $"Failed to write orientation values: {ex.Message}";
                AppLogger.LogError("Cmd_WindowOrientation.Execute", ex);
                return Result.Failed;
            }

            // ---------------------------------------------------------------- //
            //  Summary
            // ---------------------------------------------------------------------- //
            var summary = new StringBuilder();
            summary.AppendLine($"Updated {updatedCount} window(s).");
            summary.AppendLine(
                $"True North correction applied: {trueNorthCorrectionDegrees:F2} deg " +
                "(from Manage tab, Position, Rotate True North). Verify against a known wall " +
                "before trusting this on a project where True North differs from Project North.");
            if (skippedNotWindow.Count > 0)
                summary.AppendLine($"{skippedNotWindow.Count} skipped — not a window.");
            if (skippedNoParameter.Count > 0)
                summary.AppendLine(
                    $"{skippedNoParameter.Count} skipped — '{OrientationParameterName}' " +
                    "parameter not found on instance. The family may not expose it.");
            if (skippedReadOnly.Count > 0)
                summary.AppendLine(
                    $"{skippedReadOnly.Count} skipped — '{OrientationParameterName}' is read-only.");
            if (skippedNoOrientation.Count > 0)
                summary.AppendLine(
                    $"{skippedNoOrientation.Count} skipped — could not resolve a facing direction.");
            if (correctedFacingMismatch.Count > 0)
                summary.AppendLine(
                    $"{correctedFacingMismatch.Count} window(s) had their exterior side landing " +
                    "inside a Room (or disagreed with a confirmed peer) and were written using the " +
                    "mirrored direction. The model geometry itself was NOT changed, these instances " +
                    "still need their flip corrected.");
            if (unvalidatedCount > 0)
                summary.AppendLine(
                    $"{unvalidatedCount} updated window(s) could not be cross checked against a " +
                    "Room or a peer on the same host wall. Verify these manually.");

            AppLogger.LogInfo(
                $"Cmd_WindowOrientation: updated {updatedCount}, " +
                $"not window {skippedNotWindow.Count}, " +
                $"no param {skippedNoParameter.Count}, " +
                $"read only {skippedReadOnly.Count}, " +
                $"no orientation {skippedNoOrientation.Count}, " +
                $"facing mismatch corrected {correctedFacingMismatch.Count}, " +
                $"unvalidated {unvalidatedCount}, " +
                $"true north correction {trueNorthCorrectionDegrees:F2} deg.");

            TaskDialog.Show("Window Orientation", summary.ToString());
            return Result.Succeeded;
        }

        // ------------------------------------------------------------------ //
        //  PARAMETER BINDING
        // ------------------------------------------------------------------ //
        /// <summary>
        /// Checks whether BA_Orientation is already bound to the Windows
        /// category as an instance parameter. If not, loads it from the
        /// shared parameter file by name and creates the binding.
        /// Must be called inside an active transaction.
        /// Returns true if the parameter is ready to use, false with an
        /// error message if the binding could not be established.
        /// </summary>
        private static bool EnsureParameterBound(
            Document doc,
            Autodesk.Revit.ApplicationServices.Application app,
            out string error)
        {
            error = string.Empty;
            var bindingMap = doc.ParameterBindings;
            var windowsCategory = doc.Settings.Categories
                .get_Item(BuiltInCategory.OST_Windows);

            // Walk existing bindings — look for a definition whose name
            // matches BA_Orientation that is already bound to Windows.
            var it = bindingMap.ForwardIterator();
            while (it.MoveNext())
            {
                var def = it.Key as Definition;
                if (def == null) continue;
                if (!def.Name.Equals(OrientationParameterName,
                        StringComparison.OrdinalIgnoreCase)) continue;

                // Definition found — check that Windows is in its category set.
                if (it.Current is InstanceBinding existing)
                {
                    foreach (Category c in existing.Categories)
                    {
                        if (c.Id.Value == (long)BuiltInCategory.OST_Windows)
                            return true; // already bound correctly
                    }

                    // Bound but Windows category missing — add it.
                    var cats = existing.Categories;
                    cats.Insert(windowsCategory);
                    bindingMap.ReInsert(def, existing);
                    return true;
                }
            }

            // Not bound at all — load from the shared parameter file.
            ExternalDefinition extDef;
            try
            {
                extDef = SharedParamUtils.FindExternalDefinitionByGuidOrName(
                    app,
                    SharedParamFilePath,
                    OrientationParameterName,
                    Guid.Empty);
            }
            catch (Exception ex)
            {
                error = $"Could not open shared parameter file:\n{ex.Message}\n\n" +
                        $"Expected path:\n{SharedParamFilePath}";
                AppLogger.LogError("Cmd_WindowOrientation.EnsureParameterBound", ex);
                return false;
            }

            if (extDef == null)
            {
                error = $"'{OrientationParameterName}' was not found in the shared " +
                        $"parameter file at:\n{SharedParamFilePath}\n\n" +
                        "Add the parameter to the file and retry.";
                return false;
            }

            // Build a category set containing only Windows.
            var categorySet = app.Create.NewCategorySet();
            categorySet.Insert(windowsCategory);

            var binding = app.Create.NewInstanceBinding(categorySet);
            bool inserted = bindingMap.Insert(extDef, binding,
                GroupTypeId.Data);

            if (!inserted)
            {
                error = $"Failed to bind '{OrientationParameterName}' to the " +
                        "Windows category. The parameter may already exist with " +
                        "a conflicting binding.";
                return false;
            }

            AppLogger.LogInfo(
                $"Cmd_WindowOrientation: bound '{OrientationParameterName}' " +
                "to Windows category from shared parameter file.");
            return true;
        }

        // ------------------------------------------------------------------ //
        //  FACING RESOLUTION
        // ------------------------------------------------------------------ //
        private static bool TryGetFacingOrientation(FamilyInstance familyInstance, out XYZ facing)
        {
            try
            {
                facing = familyInstance.FacingOrientation;
            }
            catch (Exception ex)
            {
                AppLogger.LogError(
                    $"Cmd_WindowOrientation.FacingOrientation [{familyInstance.Id.Value}]", ex);
                facing = null;
                return false;
            }

            if (facing == null || (facing.X == 0 && facing.Y == 0))
            {
                facing = null;
                return false;
            }

            return true;
        }

        // ------------------------------------------------------------------ //
        //  ROOM BASED FLIP DETECTION
        // ------------------------------------------------------------------ //
        private enum RoomProbeResult
        {
            FacingSideHasNoRoom,
            FacingSideHasRoom,
            Indeterminate
        }

        /// <summary>
        /// Probes a point past the host wall's face, in the direction the
        /// instance currently reports as its facing, and checks whether a
        /// Room exists there. If the side the window currently calls its
        /// facing lands inside a Room, the window is flipped: what it
        /// thinks is exterior is actually interior space. The opposite
        /// side is checked as a secondary signal when the facing side
        /// comes back clear. Requires Rooms to already be placed and
        /// bounded near this wall, and uses the document's default
        /// (last) phase, since no explicit phase is passed here.
        /// </summary>
        private static RoomProbeResult ProbeRoomAlignment(
            Document doc, FamilyInstance familyInstance, XYZ facing)
        {
            try
            {
                if (familyInstance.Location is not LocationPoint locationPoint)
                    return RoomProbeResult.Indeterminate;

                double halfWallWidth = FallbackHalfWallWidthFeet;
                if (familyInstance.Host is Wall hostWall)
                    halfWallWidth = hostWall.Width / 2.0;

                double margin = UnitUtils.ConvertToInternalUnits(
                    RoomProbeMarginMillimeters, UnitTypeId.Millimeters);
                double offset = halfWallWidth + margin;

                XYZ unitFacing = facing.Normalize();
                XYZ origin = locationPoint.Point;
                XYZ facingSidePoint = origin + unitFacing * offset;
                XYZ oppositeSidePoint = origin - unitFacing * offset;

                Room facingRoom = SafeGetRoomAtPoint(doc, facingSidePoint);
                if (facingRoom != null)
                    return RoomProbeResult.FacingSideHasRoom;

                Room oppositeRoom = SafeGetRoomAtPoint(doc, oppositeSidePoint);
                if (oppositeRoom != null)
                    return RoomProbeResult.FacingSideHasNoRoom;

                return RoomProbeResult.Indeterminate;
            }
            catch (Exception ex)
            {
                AppLogger.LogError(
                    $"Cmd_WindowOrientation.ProbeRoomAlignment [{familyInstance.Id.Value}]", ex);
                return RoomProbeResult.Indeterminate;
            }
        }

        private static Room SafeGetRoomAtPoint(Document doc, XYZ point)
        {
            try
            {
                return doc.GetRoomAtPoint(point);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Cmd_WindowOrientation.SafeGetRoomAtPoint", ex);
                return null;
            }
        }

        // ------------------------------------------------------------------ //
        //  HOST ALIGNMENT VALIDATION
        //  Room probe first (ground truth, when it resolves). For anything
        //  the room probe cannot resolve, fall back to host wall peers,
        //  preferring a room confirmed peer as the reference direction,
        //  and only using blind peer majority when no confirmed peer
        //  exists on that wall. A window with neither a room result nor
        //  any peer at all is reported Unvalidated rather than guessed at.
        // ------------------------------------------------------------------ //
        private enum HostAlignmentResult
        {
            Aligned,
            Mismatched,
            Unvalidated
        }

        private static Dictionary<ElementId, HostAlignmentResult> ClassifyFacingAlignment(
            Document doc, List<(FamilyInstance Instance, XYZ Facing)> candidates)
        {
            var result = new Dictionary<ElementId, HostAlignmentResult>();
            var facingByInstance = new Dictionary<ElementId, XYZ>();
            foreach (var c in candidates)
                facingByInstance[c.Instance.Id] = c.Facing;

            // Pass 1: room probe, authoritative wherever it resolves.
            var unresolved = new List<(FamilyInstance Instance, XYZ Facing)>();
            foreach (var candidate in candidates)
            {
                RoomProbeResult probe = ProbeRoomAlignment(doc, candidate.Instance, candidate.Facing);
                if (probe == RoomProbeResult.FacingSideHasRoom)
                    result[candidate.Instance.Id] = HostAlignmentResult.Mismatched;
                else if (probe == RoomProbeResult.FacingSideHasNoRoom)
                    result[candidate.Instance.Id] = HostAlignmentResult.Aligned;
                else
                    unresolved.Add(candidate);
            }

            // Group ALL candidates (not just the unresolved ones) by host
            // wall, so an unresolved instance can still find a room
            // confirmed peer on the same wall.
            var byHost = new Dictionary<ElementId, List<FamilyInstance>>();
            foreach (var candidate in candidates)
            {
                if (candidate.Instance.Host is not Wall wall) continue;
                if (!byHost.TryGetValue(wall.Id, out var list))
                {
                    list = new List<FamilyInstance>();
                    byHost[wall.Id] = list;
                }
                list.Add(candidate.Instance);
            }

            // Pass 2: resolve whatever the room probe left undecided.
            foreach (var candidate in unresolved)
            {
                if (candidate.Instance.Host is not Wall wall)
                {
                    result[candidate.Instance.Id] = HostAlignmentResult.Unvalidated;
                    continue;
                }

                var wallPeers = byHost[wall.Id];

                XYZ confirmedAlignedReference = FindConfirmedAlignedReference(
                    candidate.Instance.Id, wallPeers, result, facingByInstance);

                if (confirmedAlignedReference != null)
                {
                    result[candidate.Instance.Id] = ClassifyAgainstReference(
                        candidate.Facing, confirmedAlignedReference, referenceIsCorrect: true);
                    continue;
                }

                // No room confirmed peer on this wall — fall back to a
                // blind majority vote among the OTHER still unresolved
                // peers on the same wall, if there are any.
                var unresolvedPeerFacings = new List<XYZ> { candidate.Facing };
                foreach (var other in unresolved)
                {
                    if (other.Instance.Id == candidate.Instance.Id) continue;
                    if (other.Instance.Host is Wall otherWall && otherWall.Id == wall.Id)
                        unresolvedPeerFacings.Add(other.Facing);
                }

                if (unresolvedPeerFacings.Count == 1)
                {
                    result[candidate.Instance.Id] = HostAlignmentResult.Unvalidated;
                    continue;
                }

                var reference = unresolvedPeerFacings[0];
                int agreeCount = 0;
                foreach (var f in unresolvedPeerFacings)
                {
                    double dot = f.X * reference.X + f.Y * reference.Y;
                    if (dot > 0.0) agreeCount++;
                }
                bool referenceSideIsMajority = agreeCount * 2 >= unresolvedPeerFacings.Count;

                result[candidate.Instance.Id] = ClassifyAgainstReference(
                    candidate.Facing, reference, referenceIsCorrect: referenceSideIsMajority);
            }

            return result;
        }

        private static XYZ FindConfirmedAlignedReference(
            ElementId selfId,
            List<FamilyInstance> wallPeers,
            Dictionary<ElementId, HostAlignmentResult> result,
            Dictionary<ElementId, XYZ> facingByInstance)
        {
            foreach (var peer in wallPeers)
            {
                if (peer.Id == selfId) continue;
                if (result.TryGetValue(peer.Id, out var peerStatus) &&
                    peerStatus == HostAlignmentResult.Aligned &&
                    facingByInstance.TryGetValue(peer.Id, out var peerFacing))
                {
                    return peerFacing;
                }
            }
            return null;
        }

        private static HostAlignmentResult ClassifyAgainstReference(
            XYZ facing, XYZ reference, bool referenceIsCorrect)
        {
            double dot = facing.X * reference.X + facing.Y * reference.Y;
            bool matchesReference = dot > 0.0;
            bool isCorrect = matchesReference == referenceIsCorrect;
            return isCorrect ? HostAlignmentResult.Aligned : HostAlignmentResult.Mismatched;
        }

        // ------------------------------------------------------------------ //
        //  GEOMETRY
        // ------------------------------------------------------------------ //
        /// <summary>
        /// Returns the clockwise angle in degrees between Project North (the
        /// internal Y axis) and True North, taken from the active project
        /// location's Position dialog value. This must be validated against
        /// a window or wall of known compass orientation before being
        /// trusted on a project where True North differs from Project
        /// North. If the reported cardinal value comes out rotated the
        /// wrong way after that test, flip InvertTrueNorthCorrectionSign.
        /// </summary>
        private static double GetTrueNorthCorrectionDegrees(Document doc)
        {
            try
            {
                var projectLocation = doc.ActiveProjectLocation;
                if (projectLocation == null) return 0.0;

                ProjectPosition position = projectLocation.GetProjectPosition(XYZ.Zero);
                double correctionDegrees = position.Angle * 180.0 / Math.PI;

                return InvertTrueNorthCorrectionSign ? -correctionDegrees : correctionDegrees;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Cmd_WindowOrientation.GetTrueNorthCorrectionDegrees", ex);
                return 0.0;
            }
        }

        private static double VectorToDegreesFromNorth(XYZ facing, double trueNorthCorrectionDegrees)
        {
            double angleRad = Math.Atan2(facing.X, facing.Y);
            double angleDeg = angleRad * 180.0 / Math.PI;
            if (angleDeg < 0) angleDeg += 360.0;

            double corrected = angleDeg + trueNorthCorrectionDegrees;
            corrected %= 360.0;
            if (corrected < 0) corrected += 360.0;
            return corrected;
        }

        // ------------------------------------------------------------------ //
        //  PARAMETER WRITE
        // ------------------------------------------------------------------ //
        private static bool TryWriteOrientationValue(Parameter orientationParam, double degrees)
        {
            try
            {
                if (orientationParam.StorageType == StorageType.String)
                {
                    string cardinal = DegreesToCardinal(degrees);
                    orientationParam.Set($"{degrees:F1} deg ({cardinal})");
                    return true;
                }

                if (orientationParam.StorageType == StorageType.Double)
                {
                    var dataType = orientationParam.Definition.GetDataType();
                    double valueToSet = dataType == SpecTypeId.Angle
                        ? UnitUtils.ConvertToInternalUnits(degrees, UnitTypeId.Degrees)
                        : degrees;
                    orientationParam.Set(valueToSet);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Cmd_WindowOrientation.TryWriteOrientationValue", ex);
                return false;
            }
        }

        private static string DegreesToCardinal(double degrees)
        {
            string[] directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW", "N" };
            int index = (int)Math.Round(degrees / 45.0);
            return directions[index];
        }

        private static string DescribeElement(Element element)
            => $"{element.Category?.Name ?? "Unknown"} (Id {element.Id.Value})";
    }
}
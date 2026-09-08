using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using BA.BAApplication;
using BA.BIM.Core.Dimensioning.Models;

namespace BA.BIM.Core.Dimensioning.Services
{
    public static class BA_DimensionPlacementService
    {
        private const string RoughHeightSharedParamName = "BA_Height_Rough";
        private const string InternalViewName = "BA_AutoDimension_Internal3D";
        private const double TerminalRayInsetFeet = 0.5;
        private const double TerminalRayMaxDistanceFeet = 2.0;

        public static (List<BA_DimensionPlacementOutcome> Outcomes, List<BA_DimensionSkip> RuntimeSkips) Execute(
            Document doc, IList<BA_DimensionCandidate> candidates)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));

            var outcomes = new List<BA_DimensionPlacementOutcome>();
            var runtimeSkips = new List<BA_DimensionSkip>();

            View3D internalView3D;
            using (var setupTx = new Transaction(doc, "BA Auto-Dimension - Internal 3D View Setup"))
            {
                setupTx.Start();
                internalView3D = GetOrCreateInternalView3D(doc);
                setupTx.Commit();
            }

            if (internalView3D == null)
            {
                foreach (var c in candidates)
                    outcomes.Add(Fail(null, c, "Could not create or find the internal 3D view required to resolve terminal wall references."));
                return (outcomes, runtimeSkips);
            }

            foreach (var viewGroup in candidates.GroupBy(c => c.ViewId))
            {
                var view = doc.GetElement(viewGroup.Key) as View;
                if (view == null)
                {
                    foreach (var c in viewGroup)
                        outcomes.Add(Fail(null, c, "View no longer exists."));
                    continue;
                }

                using (var tx = new Transaction(doc, $"BA Auto-Dimension - {view.Name}"))
                {
                    tx.Start();
                    var viewOutcomes = new List<BA_DimensionPlacementOutcome>();

                    foreach (var candidate in viewGroup)
                    {
                        try { viewOutcomes.Add(PlaceOne(doc, view, candidate, internalView3D)); }
                        catch (Exception ex)
                        {
                            viewOutcomes.Add(Fail(view, candidate, ex.Message));
                        }
                    }

                    try
                    {
                        tx.Commit();
                        outcomes.AddRange(viewOutcomes);
                    }
                    catch (Exception ex)
                    {
                        if (tx.GetStatus() != TransactionStatus.RolledBack) tx.RollBack();

                        foreach (var c in viewGroup)
                            runtimeSkips.Add(new BA_DimensionSkip
                            {
                                ViewId = view.Id,
                                ViewName = view.Name,
                                WallId = c.WallId,
                                WallName = c.WallName,
                                Reason = BA_DimensionSkipReason.Unknown,
                                Detail = $"View-level Transaction commit failed: {ex.Message}"
                            });
                    }
                }
            }

            return (outcomes, runtimeSkips);
        }

        private static BA_DimensionPlacementOutcome PlaceOne(Document doc, View view, BA_DimensionCandidate candidate, View3D internalView3D)
        {
            if (candidate.WallIds == null || candidate.WallIds.Count == 0)
                return Fail(view, candidate, "Candidate has no associated walls.");

            var runWalls = new List<Wall>();
            foreach (var wallId in candidate.WallIds)
            {
                var w = doc.GetElement(wallId) as Wall;
                if (w == null) return Fail(view, candidate, $"Wall {wallId.Value} no longer exists (deleted since scan).");
                runWalls.Add(w);
            }

            var wallLines = new Dictionary<ElementId, Line>();
            foreach (var w in runWalls)
            {
                if (!(w.Location is LocationCurve lc) || !(lc.Curve is Line line))
                    return Fail(view, candidate, $"Wall {w.Id.Value} is no longer straight (geometry changed since scan).");
                wallLines[w.Id] = line;
            }

            var (runStart, runEnd, _) = BA_DimensionRunGeometry.ComputeRunExtents(runWalls, wallLines);
            XYZ runDirection = (runEnd - runStart).Normalize();

            double sampleZStart = GetWallMidHeightZ(runWalls[0]);
            double sampleZEnd = GetWallMidHeightZ(runWalls[runWalls.Count - 1]);

            Reference startTerminalRef = ResolveTerminalReference(doc, internalView3D, runStart, runDirection.Negate(), sampleZStart, out string startError);
            if (startTerminalRef == null)
                return Fail(view, candidate, $"Could not resolve a terminal wall reference at the start of the run: {startError}");

            Reference endTerminalRef = ResolveTerminalReference(doc, internalView3D, runEnd, runDirection, sampleZEnd, out string endError);
            if (endTerminalRef == null)
                return Fail(view, candidate, $"Could not resolve a terminal wall reference at the end of the run: {endError}");

            var refArray = new ReferenceArray();
            refArray.Append(startTerminalRef);

            var openingWidthSegmentIndex = new int?[candidate.OrderedOpenings.Count];
            int cursor = 1;

            for (int idx = 0; idx < candidate.OrderedOpenings.Count; idx++)
            {
                var openingRef = candidate.OrderedOpenings[idx];
                var fi = doc.GetElement(openingRef.OpeningId) as FamilyInstance;
                if (fi == null)
                    return Fail(view, candidate, $"Opening {openingRef.OpeningId.Value} no longer exists.");

                if (openingRef.DimensionStyle == BA_DimensionStyle.Edges)
                {
                    string jambError = AppendConcreteJambReferences(fi, refArray);
                    if (jambError != null)
                        return Fail(view, candidate, jambError);

                    openingWidthSegmentIndex[idx] = cursor;
                    cursor += 2;
                }
                else
                {
                    IList<Reference> refs;
                    try { refs = fi.GetReferences(FamilyInstanceReferenceType.CenterLeftRight); }
                    catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                    {
                        return Fail(view, candidate, $"Opening {openingRef.OpeningId.Value} no longer exposes a CenterLeftRight reference: {ex.Message}");
                    }

                    if (refs == null || refs.Count == 0)
                        return Fail(view, candidate, $"Opening {openingRef.OpeningId.Value} has no CenterLeftRight reference at commit time.");

                    refArray.Append(refs[0]);
                    cursor += 1;
                }
            }

            refArray.Append(endTerminalRef);

            Wall firstWall = runWalls[0];
            XYZ baseOrientation = firstWall.Orientation;
            XYZ orientation = candidate.IsFlipped ? baseOrientation.Negate() : baseOrientation;
            XYZ offset = orientation.Multiply(candidate.OffsetFeet);

            XYZ p0 = runStart + offset - runDirection.Multiply(1.0);
            XYZ p1 = runEnd + offset + runDirection.Multiply(1.0);

            if (p0.DistanceTo(p1) < doc.Application.ShortCurveTolerance)
                return Fail(view, candidate, "Run is shorter than ShortCurveTolerance.");

            Line dimLine = Line.CreateBound(p0, p1);

            Dimension dim;
            try { dim = doc.Create.NewDimension(view, dimLine, refArray); }
            catch (Exception ex) { return Fail(view, candidate, $"NewDimension failed: {ex.Message}"); }

            ApplyRoughHeightAnnotations(doc, dim, candidate, openingWidthSegmentIndex);

            return new BA_DimensionPlacementOutcome
            {
                ViewId = view.Id,
                ViewName = view.Name,
                WallId = candidate.WallId,
                Success = true,
                CreatedDimensionId = dim?.Id,
                SourceCandidate = candidate
            };
        }

        /// <summary>
        /// Appends Left then Right jamb references, in that fixed order, per confirmed project
        /// convention that Left is always Left and Right is always Right for these families.
        /// </summary>
        private static string AppendConcreteJambReferences(FamilyInstance fi, ReferenceArray refArray)
        {
            IList<Reference> leftRefs;
            IList<Reference> rightRefs;
            try
            {
                leftRefs = fi.GetReferences(FamilyInstanceReferenceType.Left);
                rightRefs = fi.GetReferences(FamilyInstanceReferenceType.Right);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                return $"Opening {fi.Id.Value} does not expose Left/Right jamb references: {ex.Message}";
            }

            if (leftRefs == null || leftRefs.Count == 0 || rightRefs == null || rightRefs.Count == 0)
                return $"Opening {fi.Id.Value} is missing a Left or Right jamb reference at commit time.";

            refArray.Append(leftRefs[0]);
            refArray.Append(rightRefs[0]);

            return null;
        }

        /// <summary>
        /// Casts a ray from just inside the run toward and past the run's true end point,
        /// returning the first face hit. This is meant to resolve either the run's own wall
        /// end cap, or the face of a perpendicular wall joining at that corner. This is the
        /// least tested part of the whole feature; verify against a real model before trusting
        /// it on a live drawing set.
        /// </summary>
        private static Reference ResolveTerminalReference(Document doc, View3D view3D, XYZ endPoint, XYZ outwardDirection, double sampleZ, out string error)
        {
            error = null;

            XYZ rayOrigin = new XYZ(
                endPoint.X - outwardDirection.X * TerminalRayInsetFeet,
                endPoint.Y - outwardDirection.Y * TerminalRayInsetFeet,
                sampleZ);

            var intersector = new ReferenceIntersector(new ElementClassFilter(typeof(Wall)), FindReferenceTarget.Face, view3D)
            {
                FindReferencesInRevitLinks = false
            };

            IList<ReferenceWithContext> hits;
            try { hits = intersector.Find(rayOrigin, outwardDirection); }
            catch (Exception ex) { error = $"ReferenceIntersector.Find failed: {ex.Message}"; return null; }

            if (hits == null || hits.Count == 0)
            {
                error = "No face found along the terminal ray.";
                return null;
            }

            var best = hits
                .Where(h => h.Proximity > 0.001 && h.Proximity <= TerminalRayInsetFeet + TerminalRayMaxDistanceFeet)
                .OrderBy(h => h.Proximity)
                .FirstOrDefault();

            if (best == null)
            {
                error = "No face found within the expected terminal distance.";
                return null;
            }

            return best.GetReference();
        }

        private static double GetWallMidHeightZ(Wall wall)
        {
            BoundingBoxXYZ bb = wall.get_BoundingBox(null);
            if (bb != null) return (bb.Min.Z + bb.Max.Z) / 2.0;

            if (wall.Location is LocationCurve lc)
                return lc.Curve.GetEndPoint(0).Z + 3.0;

            return 3.0;
        }

        private static View3D GetOrCreateInternalView3D(Document doc)
        {
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate && v.Name == InternalViewName);

            if (existing != null) return existing;

            var vft = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == ViewFamily.ThreeDimensional);

            if (vft == null) return null;

            View3D view3D = View3D.CreateIsometric(doc, vft.Id);
            view3D.Name = InternalViewName;

            try
            {
                Parameter p = view3D.get_Parameter(BuiltInParameter.VIEW_DESCRIPTION);
                if (p != null && !p.IsReadOnly) p.Set("BA Tools internal use, required by Auto-Dimension. Do not delete.");
            }
            catch { }

            return view3D;
        }

        /// <summary>
        /// Sets DimensionSegment.Below on each Edges-style opening's width segment, when that
        /// opening is on a Concrete wall and is a Door, not a Window. Windows and Axis-style
        /// openings are left untouched.
        /// </summary>
        private static void ApplyRoughHeightAnnotations(Document doc, Dimension dim, BA_DimensionCandidate candidate, int?[] openingWidthSegmentIndex)
        {
            if (dim == null) return;

            DimensionSegmentArray segments = dim.Segments;
            if (segments == null || segments.Size == 0)
            {
                AppLogger.LogInfo($"BA Auto-Dimension: expected a segmented Dimension for wall run starting at {candidate.WallId.Value}, but Dimension.Segments was null/empty.");
                return;
            }

            for (int idx = 0; idx < candidate.OrderedOpenings.Count; idx++)
            {
                int? segIdx = openingWidthSegmentIndex[idx];
                if (!segIdx.HasValue) continue;

                var openingRef = candidate.OrderedOpenings[idx];
                if (openingRef.DimensionStyle != BA_DimensionStyle.Edges || openingRef.WallConstructionType != BA_WallConstructionType.Concrete)
                    continue;

                var fi = doc.GetElement(openingRef.OpeningId) as FamilyInstance;
                if (fi == null) continue;

                bool isDoor = fi.Category != null && fi.Category.Id == new ElementId(BuiltInCategory.OST_Doors);
                if (!isDoor) continue;

                string roughHeightText = GetRoughHeightText(fi);
                if (string.IsNullOrEmpty(roughHeightText))
                {
                    AppLogger.LogInfo($"BA Auto-Dimension: no {RoughHeightSharedParamName} or built-in Rough Height value on opening {fi.Id.Value}; skipping Below annotation.");
                    continue;
                }

                if (segIdx.Value >= segments.Size) continue;

                DimensionSegment seg = segments.get_Item(segIdx.Value);
                if (seg != null) seg.Below = roughHeightText;
            }
        }

        private static string GetRoughHeightText(FamilyInstance fi)
        {
            double? heightFeet = TryGetDoubleParam(fi.LookupParameter(RoughHeightSharedParamName));

            if (!heightFeet.HasValue)
            {
                Parameter builtIn = null;
                try { builtIn = fi.get_Parameter(BuiltInParameter.FAMILY_ROUGH_HEIGHT_PARAM); }
                catch { }

                heightFeet = TryGetDoubleParam(builtIn);
            }

            if (!heightFeet.HasValue) return null;

            double mm = UnitUtils.ConvertFromInternalUnits(heightFeet.Value, UnitTypeId.Millimeters);
            return Math.Round(mm, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
        }

        private static double? TryGetDoubleParam(Parameter p)
        {
            if (p == null || !p.HasValue || p.StorageType != StorageType.Double) return null;
            return p.AsDouble();
        }

        /// <summary>
        /// Deletes a placed dimension, its own short transaction. Called through the ExternalEvent
        /// bridge, never directly from WPF.
        /// </summary>
        public static bool DeleteDimension(Document doc, ElementId dimensionId)
        {
            if (doc == null || dimensionId == null || dimensionId == ElementId.InvalidElementId) return false;

            var element = doc.GetElement(dimensionId);
            if (element == null) return false;

            using (var tx = new Transaction(doc, "BA Auto-Dimension - Delete"))
            {
                tx.Start();
                try
                {
                    doc.Delete(dimensionId);
                    tx.Commit();
                    return true;
                }
                catch
                {
                    if (tx.GetStatus() != TransactionStatus.RolledBack) tx.RollBack();
                    return false;
                }
            }
        }

        private static BA_DimensionPlacementOutcome Fail(View view, BA_DimensionCandidate candidate, string message) =>
            new BA_DimensionPlacementOutcome
            {
                ViewId = view?.Id ?? candidate.ViewId,
                ViewName = view?.Name ?? candidate.ViewName,
                WallId = candidate.WallId,
                Success = false,
                FailureMessage = message,
                SourceCandidate = candidate
            };
    }
}
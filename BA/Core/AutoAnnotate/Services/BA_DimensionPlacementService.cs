using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BA.BIM.Core.Dimensioning.Models;

namespace BA.BIM.Core.Dimensioning.Services
{
    public static class BA_DimensionPlacementService
    {
        private const double OffsetFeet = BA_DimensionCandidateService.OffsetFeet;

        public static (List<BA_DimensionPlacementOutcome> Outcomes, List<BA_DimensionSkip> RuntimeSkips) Execute(
            Document doc, IList<BA_DimensionCandidate> candidates)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));

            var outcomes = new List<BA_DimensionPlacementOutcome>();
            var runtimeSkips = new List<BA_DimensionSkip>();

            foreach (var viewGroup in candidates.GroupBy(c => c.ViewId))
            {
                var view = doc.GetElement(viewGroup.Key) as View;
                if (view == null)
                {
                    foreach (var c in viewGroup)
                        outcomes.Add(new BA_DimensionPlacementOutcome
                        {
                            ViewId = viewGroup.Key,
                            ViewName = "(deleted view)",
                            WallId = c.WallId,
                            Success = false,
                            FailureMessage = "View no longer exists."
                        });
                    continue;
                }

                using (var tx = new Transaction(doc, $"BA Auto-Dimension - {view.Name}"))
                {
                    tx.Start();
                    var viewOutcomes = new List<BA_DimensionPlacementOutcome>();

                    foreach (var candidate in viewGroup)
                    {
                        try { viewOutcomes.Add(PlaceOne(doc, view, candidate)); }
                        catch (Exception ex)
                        {
                            viewOutcomes.Add(new BA_DimensionPlacementOutcome
                            {
                                ViewId = view.Id,
                                ViewName = view.Name,
                                WallId = candidate.WallId,
                                Success = false,
                                FailureMessage = ex.Message
                            });
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

        private static BA_DimensionPlacementOutcome PlaceOne(Document doc, View view, BA_DimensionCandidate candidate)
        {
            var wall = doc.GetElement(candidate.WallId) as Wall;
            if (wall == null)
                return Fail(view, candidate.WallId, "Wall no longer exists (deleted since scan).");

            var refArray = new ReferenceArray();
            foreach (var openingId in candidate.OrderedOpeningIds)
            {
                var fi = doc.GetElement(openingId) as FamilyInstance;
                if (fi == null)
                    return Fail(view, candidate.WallId, $"Opening {openingId.Value} no longer exists.");

                IList<Reference> refs;
                try { refs = fi.GetReferences(FamilyInstanceReferenceType.CenterLeftRight); }
                catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                {
                    return Fail(view, candidate.WallId, $"Opening {openingId.Value} no longer exposes a CenterLeftRight reference: {ex.Message}");
                }

                if (refs == null || refs.Count == 0)
                    return Fail(view, candidate.WallId, $"Opening {openingId.Value} has no CenterLeftRight reference at commit time.");

                refArray.Append(refs[0]);
            }

            if (!(wall.Location is LocationCurve lc) || !(lc.Curve is Line wallLine))
                return Fail(view, candidate.WallId, "Wall is no longer straight (geometry changed since scan).");

            XYZ orientation = wall.Orientation;
            XYZ offset = orientation.Multiply(OffsetFeet);

            XYZ p0 = wallLine.GetEndPoint(0) + offset;
            XYZ p1 = wallLine.GetEndPoint(1) + offset;

            if (p0.DistanceTo(p1) < doc.Application.ShortCurveTolerance)
                return Fail(view, candidate.WallId, "Wall is shorter than ShortCurveTolerance.");

            Line dimLine = Line.CreateBound(p0, p1);

            Dimension dim;
            try { dim = doc.Create.NewDimension(view, dimLine, refArray); }
            catch (Exception ex) { return Fail(view, candidate.WallId, $"NewDimension failed: {ex.Message}"); }

            return new BA_DimensionPlacementOutcome
            {
                ViewId = view.Id,
                ViewName = view.Name,
                WallId = candidate.WallId,
                Success = true,
                CreatedDimensionId = dim?.Id
            };
        }

        private static BA_DimensionPlacementOutcome Fail(View view, ElementId wallId, string message) =>
            new BA_DimensionPlacementOutcome
            {
                ViewId = view.Id,
                ViewName = view.Name,
                WallId = wallId,
                Success = false,
                FailureMessage = message
            };
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace BA.Core.Dimensioning
{
    /// <summary>
    /// Builds a single multi-segment linear Dimension from a set of picked elements plus one
    /// anchor reference, auto-detecting whether to dimension along X (Center Left/Right) or
    /// Y (Center Front/Back) based on the overall spread of anchor + element positions.
    ///
    /// KNOWN LIMITATIONS -- flagged explicitly, not hidden:
    /// - Only works on FamilyInstance elements. Walls, floors, rooms, etc. don't expose
    ///   Center (Left/Right)/(Front/Back) references and are skipped with a reported reason.
    /// - Direction detection ignores each family instance's own rotation -- it compares raw
    ///   X/Y spread of anchor + element center points. A rotated element whose local left/right
    ///   axis doesn't align with the project's X axis may resolve to the wrong reference type.
    /// - Direction is decided ONCE for the whole picked set (by total spread), not per element --
    ///   a single Dimension can only run in one direction, so per-element direction detection
    ///   isn't geometrically meaningful here.
    /// - If a FamilyInstance exposes multiple references of the chosen type (mirrored/repeated
    ///   internal geometry), only the first one returned is used.
    /// - Assumes a fully axis-aligned (orthogonal) layout, consistent with the rest of this
    ///   codebase's room/axis placement logic, which makes the same assumption.
    /// </summary>
    public static class DimensionToReferenceService
    {
        public sealed class SkippedElement
        {
            public ElementId ElementId = ElementId.InvalidElementId;
            public string Reason = string.Empty;
        }

        public sealed class Result
        {
            public Autodesk.Revit.DB.Dimension? Dimension;
            public int SegmentCount;
            public List<SkippedElement> Skipped = new();
        }

        public static Result CreateDimensionToAnchor(
            Document doc,
            Autodesk.Revit.DB.View view,
            Reference anchorReference,
            XYZ anchorPoint,
            IReadOnlyList<Reference> elementPicks)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (anchorReference == null) throw new ArgumentNullException(nameof(anchorReference));
            if (anchorPoint == null) throw new ArgumentNullException(nameof(anchorPoint));
            if (elementPicks == null) throw new ArgumentNullException(nameof(elementPicks));

            var result = new Result();

            // ---- Gather center points for direction detection ----
            var centers = new Dictionary<ElementId, XYZ>();
            foreach (var pick in elementPicks)
            {
                var el = doc.GetElement(pick);
                if (el == null) continue;
                centers[el.Id] = GetApproxCenter(el);
            }

            if (centers.Count == 0)
            {
                result.Skipped.Add(new SkippedElement { Reason = "No valid elements were picked." });
                return result;
            }

            var allPoints = new List<XYZ> { anchorPoint };
            allPoints.AddRange(centers.Values);

            double spreadX = allPoints.Max(p => p.X) - allPoints.Min(p => p.X);
            double spreadY = allPoints.Max(p => p.Y) - allPoints.Min(p => p.Y);
            bool horizontal = spreadX >= spreadY;

            var refType = horizontal
                ? FamilyInstanceReferenceType.CenterLeftRight
                : FamilyInstanceReferenceType.CenterFrontBack;

            // ---- Resolve each element's reference ----
            var resolved = new List<(Reference reference, XYZ point)> { (anchorReference, anchorPoint) };

            foreach (var pick in elementPicks)
            {
                var el = doc.GetElement(pick);
                if (el == null) continue;

                if (el is not FamilyInstance fi)
                {
                    result.Skipped.Add(new SkippedElement
                    {
                        ElementId = el.Id,
                        Reason = $"'{el.Name}' is not a family instance -- Center (Left/Right)/(Front/Back) " +
                                 "references only exist on family instances."
                    });
                    continue;
                }

                IList<Reference>? refs;
                try
                {
                    refs = fi.GetReferences(refType);
                }
                catch (Exception ex)
                {
                    result.Skipped.Add(new SkippedElement
                    {
                        ElementId = el.Id,
                        Reason = $"GetReferences({refType}) threw: {ex.Message}"
                    });
                    continue;
                }

                if (refs == null || refs.Count == 0)
                {
                    result.Skipped.Add(new SkippedElement
                    {
                        ElementId = el.Id,
                        Reason = $"No Center ({(horizontal ? "Left/Right" : "Front/Back")}) reference exists on this family instance."
                    });
                    continue;
                }

                resolved.Add((refs[0], centers[el.Id]));
            }

            if (resolved.Count < 2)
            {
                result.Skipped.Add(new SkippedElement { Reason = "Fewer than 2 usable references after resolution -- nothing to dimension." });
                return result;
            }

            // ---- Order along the dimension axis, build the line, create the dimension ----
            var ordered = horizontal
                ? resolved.OrderBy(x => x.point.X).ToList()
                : resolved.OrderBy(x => x.point.Y).ToList();

            var refArray = new ReferenceArray();
            foreach (var item in ordered)
                refArray.Append(item.reference);

            var first = ordered.First().point;
            var last = ordered.Last().point;
            var z = anchorPoint.Z;

            var line = horizontal
                ? Line.CreateBound(new XYZ(first.X, anchorPoint.Y, z), new XYZ(last.X, anchorPoint.Y, z))
                : Line.CreateBound(new XYZ(anchorPoint.X, first.Y, z), new XYZ(anchorPoint.X, last.Y, z));

            result.Dimension = doc.Create.NewDimension(view, line, refArray);
            result.SegmentCount = ordered.Count - 1;

            return result;
        }

        private static XYZ GetApproxCenter(Element el)
        {
            if (el.Location is LocationPoint lp)
                return lp.Point;

            var bb = el.get_BoundingBox(null);
            if (bb != null)
                return (bb.Min + bb.Max) * 0.5;

            return XYZ.Zero;
        }
    }
}

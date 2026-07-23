// File: BA/Core/CurveToElement/Models/CurveChain.cs
// Action: CREATE NEW

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace BA.Core.CurveToElement.Models
{
    /// <summary>
    /// An ordered, continuity-checked sequence of curves produced by CurveChainBuilder.
    /// Each curve's start point connects to the previous curve's end point within the
    /// builder's matching tolerance.
    /// </summary>
    public class CurveChain
    {
        public List<Curve> OrderedSegments { get; }
        public bool IsClosed { get; }

        public CurveChain(List<Curve> orderedSegments, bool isClosed)
        {
            OrderedSegments = orderedSegments ?? throw new ArgumentNullException(nameof(orderedSegments));
            if (orderedSegments.Count == 0)
                throw new ArgumentException("A curve chain must contain at least one segment.", nameof(orderedSegments));
            IsClosed = isClosed;
        }

        /// <summary>
        /// Computes the loop normal via Newell's method. Only valid for closed chains.
        /// Used instead of a flat XY shoelace sum because detail lines can live on any
        /// view plane (elevation/section views are not parallel to world XY). The caller
        /// must compare this against the host view's Z axis to interpret winding as
        /// "interior vs exterior" for that specific view.
        /// </summary>
        public XYZ ComputeNewellNormal()
        {
            if (!IsClosed)
                throw new InvalidOperationException("Newell normal is only defined for closed curve chains.");

            List<XYZ> points = CollectLoopPoints();
            if (points.Count < 3)
                return XYZ.Zero;

            double nx = 0.0, ny = 0.0, nz = 0.0;
            int count = points.Count;
            for (int i = 0; i < count; i++)
            {
                XYZ current = points[i];
                XYZ next = points[(i + 1) % count];
                nx += (current.Y - next.Y) * (current.Z + next.Z);
                ny += (current.Z - next.Z) * (current.X + next.X);
                nz += (current.X - next.X) * (current.Y + next.Y);
            }

            XYZ normal = new XYZ(nx, ny, nz);
            return normal.GetLength() > 1e-9 ? normal.Normalize() : XYZ.Zero;
        }

        private List<XYZ> CollectLoopPoints()
        {
            const double weldTolerance = 1.0 / 12.0 / 16.0; // 1/16 inch in feet

            var points = new List<XYZ>();
            foreach (Curve segment in OrderedSegments)
            {
                IList<XYZ> tessellated = segment.Tessellate();
                int startIndex = points.Count == 0 ? 0 : 1;
                for (int i = startIndex; i < tessellated.Count; i++)
                    points.Add(tessellated[i]);
            }

            if (points.Count > 1 && points[0].IsAlmostEqualTo(points[points.Count - 1], weldTolerance))
                points.RemoveAt(points.Count - 1);

            return points;
        }
    }
}
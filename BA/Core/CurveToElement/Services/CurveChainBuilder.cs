// File: BA/Core/CurveToElement/Services/CurveChainBuilder.cs
// Action: CREATE NEW

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BA.BAApplication;
using BA.Core.CurveToElement.Models;

namespace BA.Core.CurveToElement.Services
{
    /// <summary>
    /// Chains a group's curves end-to-end into ordered CurveChain objects, matching endpoints
    /// within a configurable tolerance and detecting closed loops. Assumes a simple planar
    /// network (each point touched by at most 2 curves); branch points (3+ curves meeting)
    /// are resolved with a tangent-alignment heuristic and logged - that is not a solved
    /// T-junction/graph-splitting case and should be reviewed manually before generation.
    /// </summary>
    public class CurveChainBuilder
    {
        private readonly double _pointTolerance;

        public CurveChainBuilder(double pointTolerance = 1.0 / 12.0 / 16.0) // default 1/16 inch in feet
        {
            if (pointTolerance <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(pointTolerance), "Point matching tolerance must be positive.");
            _pointTolerance = pointTolerance;
        }

        public List<CurveChain> BuildChains(IList<ClassifiableCurve> curves)
        {
            if (curves == null) throw new ArgumentNullException(nameof(curves));

            var pool = curves.Select(c => c.Curve).ToList();
            var used = new bool[pool.Count];
            var chains = new List<CurveChain>();

            for (int seedIndex = 0; seedIndex < pool.Count; seedIndex++)
            {
                if (used[seedIndex]) continue;

                used[seedIndex] = true;
                var chainSegments = new List<Curve> { pool[seedIndex] };

                ExtendForward(pool, used, chainSegments);
                ExtendBackward(pool, used, chainSegments);

                XYZ chainStart = chainSegments[0].GetEndPoint(0);
                XYZ chainEnd = chainSegments[chainSegments.Count - 1].GetEndPoint(1);
                bool isClosed = chainSegments.Count > 1 && chainStart.IsAlmostEqualTo(chainEnd, _pointTolerance);

                chains.Add(new CurveChain(chainSegments, isClosed));
            }

            return chains;
        }

        private void ExtendForward(List<Curve> pool, bool[] used, List<Curve> chainSegments)
        {
            while (true)
            {
                XYZ tail = chainSegments[chainSegments.Count - 1].GetEndPoint(1);
                int matchIndex = FindBestMatch(pool, used, tail, chainSegments[chainSegments.Count - 1], out bool matchIsStart);
                if (matchIndex < 0) return;

                Curve candidate = pool[matchIndex];
                Curve oriented = matchIsStart ? candidate : candidate.CreateReversed();
                used[matchIndex] = true;
                chainSegments.Add(oriented);

                XYZ loopStart = chainSegments[0].GetEndPoint(0);
                if (oriented.GetEndPoint(1).IsAlmostEqualTo(loopStart, _pointTolerance))
                    return;
            }
        }

        private void ExtendBackward(List<Curve> pool, bool[] used, List<Curve> chainSegments)
        {
            while (true)
            {
                XYZ head = chainSegments[0].GetEndPoint(0);
                int matchIndex = FindBestMatch(pool, used, head, chainSegments[0], out bool matchIsStart);
                if (matchIndex < 0) return;

                Curve candidate = pool[matchIndex];
                Curve oriented = matchIsStart ? candidate.CreateReversed() : candidate;
                used[matchIndex] = true;
                chainSegments.Insert(0, oriented);

                XYZ loopEnd = chainSegments[chainSegments.Count - 1].GetEndPoint(1);
                if (oriented.GetEndPoint(0).IsAlmostEqualTo(loopEnd, _pointTolerance))
                    return;
            }
        }

        /// <summary>
        /// Finds the unused curve with an endpoint nearest targetPoint (within tolerance).
        /// matchIsStart indicates whether the MATCHING endpoint is the candidate's own start
        /// point (true) or end point (false) - the caller uses this to decide whether the
        /// candidate needs CreateReversed() before it is appended or prepended.
        /// </summary>
        private int FindBestMatch(List<Curve> pool, bool[] used, XYZ targetPoint, Curve referenceCurve, out bool matchIsStart)
        {
            int bestIndex = -1;
            bool bestIsStart = true;
            double bestAlignment = double.NegativeInfinity;

            XYZ referenceDirection = GetApproximateDirection(referenceCurve);

            for (int i = 0; i < pool.Count; i++)
            {
                if (used[i]) continue;

                Curve candidate = pool[i];
                XYZ start = candidate.GetEndPoint(0);
                XYZ end = candidate.GetEndPoint(1);

                bool startMatches = start.IsAlmostEqualTo(targetPoint, _pointTolerance);
                bool endMatches = end.IsAlmostEqualTo(targetPoint, _pointTolerance);
                if (!startMatches && !endMatches) continue;

                XYZ candidateDirection = GetApproximateDirection(candidate);
                if (endMatches && !startMatches)
                    candidateDirection = -candidateDirection;

                double alignment = referenceDirection.DotProduct(candidateDirection);
                if (alignment > bestAlignment)
                {
                    bestAlignment = alignment;
                    bestIndex = i;
                    bestIsStart = startMatches;
                }
            }

            if (bestIndex >= 0 && CountMatchesAtPoint(pool, used, targetPoint) > 1)
            {
                AppLogger.LogInfo(
                    $"[CurveToElement] Branch point near ({targetPoint.X:F3}, {targetPoint.Y:F3}, {targetPoint.Z:F3}) - " +
                    "multiple curves meet here. Selected the best tangent-aligned match; verify chain topology manually.");
            }

            matchIsStart = bestIsStart;
            return bestIndex;
        }

        private int CountMatchesAtPoint(List<Curve> pool, bool[] used, XYZ point)
        {
            int count = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                if (used[i]) continue;
                if (pool[i].GetEndPoint(0).IsAlmostEqualTo(point, _pointTolerance) ||
                    pool[i].GetEndPoint(1).IsAlmostEqualTo(point, _pointTolerance))
                {
                    count++;
                }
            }
            return count;
        }

        private XYZ GetApproximateDirection(Curve curve)
        {
            XYZ chord = curve.GetEndPoint(1) - curve.GetEndPoint(0);
            double length = chord.GetLength();
            return length > 1e-9 ? chord.Normalize() : XYZ.BasisX;
        }
    }
}
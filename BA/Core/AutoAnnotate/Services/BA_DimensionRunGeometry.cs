using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace BA.BIM.Core.Dimensioning.Services
{
    /// <summary>
    /// A "run" is an ordered list of collinear, end joined Basic walls. This resolves the two
    /// true outer endpoints of that run, used by both candidate discovery (scan time) and
    /// placement (commit time, walls re fetched fresh) so both sides agree on the same math.
    /// </summary>
    internal static class BA_DimensionRunGeometry
    {
        internal const double RunJoinToleranceFeet = 0.03; // about 9mm

        internal static (XYZ RunStart, XYZ RunEnd, Line CombinedLine) ComputeRunExtents(
            IList<Wall> run, IDictionary<ElementId, Line> wallLines)
        {
            if (run.Count == 1)
            {
                Line l = wallLines[run[0].Id];
                return (l.GetEndPoint(0), l.GetEndPoint(1), l);
            }

            Line firstLine = wallLines[run[0].Id];
            Line secondLine = wallLines[run[1].Id];
            XYZ p0 = firstLine.GetEndPoint(0);
            XYZ p1 = firstLine.GetEndPoint(1);
            bool p0IsShared = p0.DistanceTo(secondLine.GetEndPoint(0)) <= RunJoinToleranceFeet
                               || p0.DistanceTo(secondLine.GetEndPoint(1)) <= RunJoinToleranceFeet;
            XYZ runStart = p0IsShared ? p1 : p0;

            Line lastLine = wallLines[run[run.Count - 1].Id];
            Line secondLastLine = wallLines[run[run.Count - 2].Id];
            XYZ q0 = lastLine.GetEndPoint(0);
            XYZ q1 = lastLine.GetEndPoint(1);
            bool q0IsShared = q0.DistanceTo(secondLastLine.GetEndPoint(0)) <= RunJoinToleranceFeet
                               || q0.DistanceTo(secondLastLine.GetEndPoint(1)) <= RunJoinToleranceFeet;
            XYZ runEnd = q0IsShared ? q1 : q0;

            Line combined = Line.CreateUnbound(runStart, (runEnd - runStart).Normalize());
            return (runStart, runEnd, combined);
        }
    }
}
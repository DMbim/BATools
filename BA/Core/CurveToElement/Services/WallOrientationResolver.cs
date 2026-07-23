// File: BA/Core/CurveToElement/Services/WallOrientationResolver.cs
// Action: CREATE NEW

using System;
using Autodesk.Revit.DB;

namespace BA.Core.CurveToElement.Services
{
    /// <summary>
    /// Determines whether a just-created wall needs Wall.Flip() to face the intended
    /// direction. Works by measuring the wall's actual current Wall.Orientation and
    /// comparing it against a computed desired direction, rather than assuming Revit's
    /// internal curve-direction-to-flip convention - this makes the result correct
    /// regardless of what that internal convention actually is.
    /// </summary>
    public class WallOrientationResolver
    {
        /// <summary>
        /// For a wall that is one segment of a closed CurveChain. desiredOutward is derived
        /// from the segment's direction crossed with the chain's Newell loop normal, which
        /// (by a standard property of Newell's method) points away from the polygon's
        /// enclosed area. Returns false (no flip) if the cross product degenerates - e.g. a
        /// segment whose direction is parallel to the loop normal, which should not occur for
        /// a genuinely planar loop but is guarded against defensively.
        /// </summary>
        public bool ShouldFlipForClosedLoop(Wall wall, Curve segmentCurve, XYZ loopNormal)
        {
            if (wall == null) throw new ArgumentNullException(nameof(wall));
            if (segmentCurve == null) throw new ArgumentNullException(nameof(segmentCurve));
            if (loopNormal == null || loopNormal.IsZeroLength())
                return false;

            XYZ chord = segmentCurve.GetEndPoint(1) - segmentCurve.GetEndPoint(0);
            if (chord.IsZeroLength())
                return false;

            XYZ direction = chord.Normalize();
            XYZ desiredOutward = direction.CrossProduct(loopNormal);
            if (desiredOutward.IsZeroLength())
                return false;

            desiredOutward = desiredOutward.Normalize();
            XYZ actualOrientation = wall.Orientation;

            return desiredOutward.DotProduct(actualOrientation) < 0.0;
        }
    }
}
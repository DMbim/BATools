using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.UI.KeyplanGrid
{
    /// <summary>
    /// Pure geometry service.  No Revit document dependency.
    /// Given a list of generated records, a direction vector, anchor keys, and a label style,
    /// produces an ordered list of KeyplanZoneAssignment ready to be written by KeyplanZoneParameterWriter.
    /// </summary>
    public static class KeyplanZoneLabelService
    {
        public const int MaxZoneCount = 12;
        private const double DirectionMinLength = 1e-9;

        // -------------------------------------------------------------------------
        // Public API
        // -------------------------------------------------------------------------

        /// <summary>
        /// Builds zone assignments for all FilledRegion records that fall between
        /// firstRegionStableKey and lastRegionStableKey along the given direction vector.
        /// </summary>
        /// <param name="records">All GeneratedElementRecords from the last generation result.
        ///     Only records with Role == "FilledRegion" and a non-null Centroid are considered.</param>
        /// <param name="directionVector">
        ///     Normalised first→second vector.  Must be computed by the caller from the
        ///     centroids of the first and second picked regions.</param>
        /// <param name="firstRegionStableKey">StableKey of the first picked region.</param>
        /// <param name="lastRegionStableKey">StableKey of the last picked region.</param>
        /// <param name="labelStyle">Numeric, AlphaUpper, or AlphaLower.</param>
        /// <param name="warningMessage">Non-empty if any capping or anomaly occurred.</param>
        /// <returns>Ordered list of assignments, index 0 = label "1" / "A" / "a".</returns>
        public static List<KeyplanZoneAssignment> BuildAssignments(
            IReadOnlyList<GeneratedElementRecord> records,
            XYZ directionVector,
            string firstRegionStableKey,
            string lastRegionStableKey,
            KeyplanZoneLabelStyle labelStyle,
            out string warningMessage)
        {
            warningMessage = string.Empty;

            if (records == null || records.Count == 0)
            {
                warningMessage = "No generated records supplied.";
                return new List<KeyplanZoneAssignment>();
            }

            if (directionVector == null || directionVector.GetLength() < DirectionMinLength)
            {
                warningMessage = "Direction vector is too short to define a traversal direction.";
                return new List<KeyplanZoneAssignment>();
            }

            // Normalise direction to unit vector.
            XYZ dir = NormaliseXY(directionVector);

            // Collect eligible records (FilledRegion + has centroid).
            List<GeneratedElementRecord> eligible = records
                .Where(r => r != null &&
                            string.Equals(r.Role, "FilledRegion", StringComparison.Ordinal) &&
                            r.Centroid != null)
                .ToList();

            if (eligible.Count == 0)
            {
                warningMessage = "No FilledRegion records with centroids found.";
                return new List<KeyplanZoneAssignment>();
            }

            // Locate anchors.
            GeneratedElementRecord firstRecord = eligible.FirstOrDefault(r =>
                string.Equals(r.StableKey, firstRegionStableKey, StringComparison.Ordinal));

            GeneratedElementRecord lastRecord = eligible.FirstOrDefault(r =>
                string.Equals(r.StableKey, lastRegionStableKey, StringComparison.Ordinal));

            if (firstRecord == null)
            {
                warningMessage = $"First region key '{firstRegionStableKey}' not found in records.";
                return new List<KeyplanZoneAssignment>();
            }

            if (lastRecord == null)
            {
                warningMessage = $"Last region key '{lastRegionStableKey}' not found in records.";
                return new List<KeyplanZoneAssignment>();
            }

            double firstProj = ProjectOntoDirection(firstRecord.Centroid, dir);
            double lastProj = ProjectOntoDirection(lastRecord.Centroid, dir);

            double minProj = Math.Min(firstProj, lastProj);
            double maxProj = Math.Max(firstProj, lastProj);

            // Small epsilon to include anchor regions that are right on the boundary.
            const double projEpsilon = 1e-6;

            // Project all eligible records and filter to range.
            List<(GeneratedElementRecord Record, double Proj)> projected = eligible
                .Select(r => (Record: r, Proj: ProjectOntoDirection(r.Centroid, dir)))
                .Where(x => x.Proj >= minProj - projEpsilon && x.Proj <= maxProj + projEpsilon)
                .OrderBy(x => x.Proj)
                .ToList();

            if (projected.Count == 0)
            {
                warningMessage = "No regions found within the range defined by first and last picks.";
                return new List<KeyplanZoneAssignment>();
            }

            // Cap to MaxZoneCount.
            bool capped = false;
            if (projected.Count > MaxZoneCount)
            {
                projected = projected.Take(MaxZoneCount).ToList();
                capped = true;
                warningMessage = $"Range contains more than {MaxZoneCount} regions. " +
                                 $"Only the first {MaxZoneCount} (by projection) were labelled.";
            }

            // Build assignments.
            List<KeyplanZoneAssignment> assignments = new List<KeyplanZoneAssignment>(projected.Count);

            for (int i = 0; i < projected.Count; i++)
            {
                GeneratedElementRecord rec = projected[i].Record;
                string label = GenerateLabel(i, labelStyle);
                string parameterName = $"BA.Tls_Zn_{i + 1}";

                assignments.Add(new KeyplanZoneAssignment
                {
                    RegionUniqueId = rec.UniqueId,
                    StableKey = rec.StableKey,
                    ParameterName = parameterName,
                    Label = label,
                    Centroid = rec.Centroid,
                    ProjectionValue = projected[i].Proj,
                    SequenceIndex = i
                });
            }

            return assignments;
        }

        /// <summary>
        /// Computes the normalised direction vector from the centroids of two records.
        /// Returns null if the two centroids are coincident (caller must check).
        /// </summary>
        public static XYZ ComputeDirectionVector(
            IReadOnlyList<GeneratedElementRecord> records,
            string firstStableKey,
            string secondStableKey,
            out string error)
        {
            error = string.Empty;

            GeneratedElementRecord first = records?.FirstOrDefault(r =>
                string.Equals(r.StableKey, firstStableKey, StringComparison.Ordinal));

            GeneratedElementRecord second = records?.FirstOrDefault(r =>
                string.Equals(r.StableKey, secondStableKey, StringComparison.Ordinal));

            if (first == null)
            {
                error = $"First region '{firstStableKey}' not found.";
                return null;
            }

            if (second == null)
            {
                error = $"Second region '{secondStableKey}' not found.";
                return null;
            }

            if (first.Centroid == null || second.Centroid == null)
            {
                error = "One or both anchor regions have no centroid.";
                return null;
            }

            XYZ delta = new XYZ(
                second.Centroid.X - first.Centroid.X,
                second.Centroid.Y - first.Centroid.Y,
                0.0);

            if (delta.GetLength() < DirectionMinLength)
            {
                error = "First and second regions are too close together to define a direction. " +
                        "Pick two distinct regions.";
                return null;
            }

            return NormaliseXY(delta);
        }

        // -------------------------------------------------------------------------
        // Label generation
        // -------------------------------------------------------------------------

        public static string GenerateLabel(int zeroBasedIndex, KeyplanZoneLabelStyle style)
        {
            switch (style)
            {
                case KeyplanZoneLabelStyle.AlphaUpper:
                    return zeroBasedIndex < 26
                        ? ((char)('A' + zeroBasedIndex)).ToString()
                        : "Z" + (zeroBasedIndex - 25).ToString();

                case KeyplanZoneLabelStyle.AlphaLower:
                    return zeroBasedIndex < 26
                        ? ((char)('a' + zeroBasedIndex)).ToString()
                        : "z" + (zeroBasedIndex - 25).ToString();

                case KeyplanZoneLabelStyle.Numeric:
                default:
                    return (zeroBasedIndex + 1).ToString();
            }
        }

        // -------------------------------------------------------------------------
        // Private helpers
        // -------------------------------------------------------------------------

        private static double ProjectOntoDirection(XYZ point, XYZ dir)
        {
            // Dot product with direction vector gives scalar projection.
            return point.X * dir.X + point.Y * dir.Y;
        }

        private static XYZ NormaliseXY(XYZ v)
        {
            double len = Math.Sqrt(v.X * v.X + v.Y * v.Y);
            if (len < DirectionMinLength)
                return new XYZ(1.0, 0.0, 0.0);

            return new XYZ(v.X / len, v.Y / len, 0.0);
        }
    }
}

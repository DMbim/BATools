using System.Collections.Generic;

namespace BA.UI.KeyplanGrid
{
    /// <summary>
    /// Pure label-generation helper. No Revit document dependency.
    /// </summary>
    public static class KeyplanZoneLabelService
    {
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

        /// <summary>
        /// Builds the final write-assignments from a committed pick session.
        /// Every assignment targets the same parameter, BA.Tls_Zone.
        /// </summary>
        public static List<KeyplanZoneAssignment> BuildAssignments(
            IReadOnlyList<GeneratedElementRecord> records,
            IReadOnlyList<string> pickedKeysInOrder,
            KeyplanZoneLabelStyle labelStyle)
        {
            List<KeyplanZoneAssignment> assignments = new List<KeyplanZoneAssignment>(pickedKeysInOrder.Count);

            for (int i = 0; i < pickedKeysInOrder.Count; i++)
            {
                string key = pickedKeysInOrder[i];

                GeneratedElementRecord rec = null;
                foreach (GeneratedElementRecord r in records)
                {
                    if (r != null && r.StableKey == key)
                    {
                        rec = r;
                        break;
                    }
                }

                if (rec == null)
                    continue;

                assignments.Add(new KeyplanZoneAssignment
                {
                    RegionUniqueId = rec.UniqueId,
                    StableKey = rec.StableKey,
                    ParameterName = KeyplanZoneParameterWriter.ZoneParameterName,
                    Label = GenerateLabel(i, labelStyle),
                    SequenceIndex = i
                });
            }

            return assignments;
        }
    }
}
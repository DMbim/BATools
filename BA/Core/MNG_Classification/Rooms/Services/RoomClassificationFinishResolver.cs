using System;
using System.Collections.Generic;
using BA.RoomClassification.Models;

namespace BA.RoomClassification.Services
{
    // This is the actual finish resolution logic: row-level override first, FinishPresets lookup
    // second. Pure C#, no Document dependency, so it is unit testable on its own.
    internal static class RoomClassificationFinishResolver
    {
        private const string DefaultFinishTier = "Standard";

        public static RoomClassificationFinishResolutionResult Resolve(
            IReadOnlyList<RoomClassificationRecord> matrixRecords,
            IReadOnlyList<FinishPresetRecord> finishPresets)
        {
            Dictionary<string, FinishPresetRecord> presetsByKey =
                new Dictionary<string, FinishPresetRecord>(StringComparer.OrdinalIgnoreCase);

            foreach (FinishPresetRecord preset in finishPresets)
            {
                // RoomClassificationValidator already checks this and blocks the import before we
                // get here, but this stays as a defense-in-depth guard in case Resolve is ever
                // called directly (e.g. from a unit test) without going through validation first.
                if (presetsByKey.ContainsKey(preset.FinishKey))
                    throw new InvalidOperationException(
                        $"Duplicate active BA.Tls_FinishKey '{preset.FinishKey}' in FinishPresets. " +
                        "Each active FinishKey must be unique.");
                presetsByKey.Add(preset.FinishKey, preset);
            }

            List<RoomClassificationRecord> resolved = new List<RoomClassificationRecord>(matrixRecords.Count);
            List<string> unresolved = new List<string>();

            foreach (RoomClassificationRecord record in matrixRecords)
            {
                string tier = string.IsNullOrWhiteSpace(record.FinishTier)
                    ? DefaultFinishTier
                    : record.FinishTier.Trim();
                string finishKey = $"{record.Department.Trim()}-{tier}";

                if (presetsByKey.TryGetValue(finishKey, out FinishPresetRecord preset))
                {
                    // Per-field override: a row that already has a value in one finish column keeps
                    // it even when the rest of the row falls back to the preset.
                    if (string.IsNullOrWhiteSpace(record.FloorFinish)) record.FloorFinish = preset.FloorFinish;
                    if (string.IsNullOrWhiteSpace(record.WallFinish)) record.WallFinish = preset.WallFinish;
                    if (string.IsNullOrWhiteSpace(record.CeilingFinish)) record.CeilingFinish = preset.CeilingFinish;
                    if (string.IsNullOrWhiteSpace(record.BaseFinish)) record.BaseFinish = preset.BaseFinish;
                }

                bool stillIncomplete =
                    string.IsNullOrWhiteSpace(record.FloorFinish) ||
                    string.IsNullOrWhiteSpace(record.WallFinish) ||
                    string.IsNullOrWhiteSpace(record.CeilingFinish) ||
                    string.IsNullOrWhiteSpace(record.BaseFinish);

                if (stillIncomplete)
                    unresolved.Add(record.RoomCode);

                resolved.Add(record);
            }

            return new RoomClassificationFinishResolutionResult(resolved, unresolved);
        }
    }
}

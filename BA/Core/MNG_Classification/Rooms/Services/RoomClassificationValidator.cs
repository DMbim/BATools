using System;
using System.Collections.Generic;
using System.Linq;
using BA.RoomClassification.Models;
using BA.RoomClassification.Configuration;

namespace BA.RoomClassification.Services
{
    internal static class RoomClassificationValidator
    {
        private static readonly string[] AllowedFinishTiers = { "Economy", "Standard", "Premium" };

        public static RoomClassificationValidationResult Validate(
            IReadOnlyList<RoomClassificationRecord> records,
            IReadOnlyList<FinishPresetRecord> finishPresets)
        {
            RoomClassificationValidationResult result = new RoomClassificationValidationResult();

            if (records == null || records.Count == 0)
            {
                result.Errors.Add("No active data rows were found in the Matrix sheet.");
                return result;
            }

            foreach (RoomClassificationRecord r in records)
            {
                Require(r.SourceRowNumber, RoomClassificationMatrixHeaders.RoomKey, r.RoomKey, result);
                Require(r.SourceRowNumber, RoomClassificationMatrixHeaders.ProgramType, r.ProgramType, result);
                Require(r.SourceRowNumber, RoomClassificationMatrixHeaders.Department, r.Department, result);
                Require(r.SourceRowNumber, RoomClassificationMatrixHeaders.RoomFunction, r.RoomFunction, result);
                Require(r.SourceRowNumber, RoomClassificationMatrixHeaders.RoomCode, r.RoomCode, result);
                Require(r.SourceRowNumber, RoomClassificationMatrixHeaders.RoomGroup, r.RoomGroup, result);
                // Floor/Wall/Ceiling/Base Finish deliberately not required here - resolved later.

                if (!string.IsNullOrWhiteSpace(r.FinishTier) &&
                    !AllowedFinishTiers.Any(t => string.Equals(t, r.FinishTier.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    result.Errors.Add(
                        $"Row {r.SourceRowNumber}: BA.Tls_Finish Tier value '{r.FinishTier}' is not one of the " +
                        $"allowed values ({string.Join(", ", AllowedFinishTiers)}).");
                }
            }

            foreach (IGrouping<string, RoomClassificationRecord> grp in
                records.GroupBy(x => x.RoomCode, StringComparer.OrdinalIgnoreCase))
            {
                if (grp.Count() > 1)
                    result.Errors.Add(
                        $"Duplicate BA.Tls_RoomCode '{grp.Key}' in rows " +
                        string.Join(", ", grp.Select(x => x.SourceRowNumber)) + ".");
            }

            foreach (IGrouping<string, RoomClassificationRecord> grp in
                records.GroupBy(x => x.RoomKey, StringComparer.OrdinalIgnoreCase))
            {
                if (grp.Count() > 1)
                    result.Errors.Add(
                        $"Duplicate BA.Tls_RoomKey '{grp.Key}' in rows " +
                        string.Join(", ", grp.Select(x => x.SourceRowNumber)) + ".");
            }

            if (finishPresets != null)
            {
                foreach (FinishPresetRecord p in finishPresets)
                {
                    if (string.IsNullOrWhiteSpace(p.FinishKey))
                        result.Errors.Add($"FinishPresets row {p.SourceRowNumber}: BA.Tls_FinishKey is empty.");
                }

                foreach (IGrouping<string, FinishPresetRecord> grp in
                    finishPresets.GroupBy(x => x.FinishKey, StringComparer.OrdinalIgnoreCase))
                {
                    if (grp.Count() > 1)
                        result.Errors.Add(
                            $"Duplicate active BA.Tls_FinishKey '{grp.Key}' in FinishPresets rows " +
                            string.Join(", ", grp.Select(x => x.SourceRowNumber)) +
                            ". Each active FinishKey must be unique, otherwise lookup is ambiguous.");
                }
            }

            return result;
        }

        private static void Require(int sourceRowNumber, string field, string value, RoomClassificationValidationResult result)
        {
            if (string.IsNullOrWhiteSpace(value))
                result.Errors.Add($"Row {sourceRowNumber}: field '{field}' is empty.");
        }
    }
}

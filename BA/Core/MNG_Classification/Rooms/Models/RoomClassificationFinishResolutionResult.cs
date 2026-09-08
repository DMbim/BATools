using System.Collections.Generic;

namespace BA.RoomClassification.Models
{
    // Result of resolving Matrix rows' finish fields against FinishPresets.
    internal sealed class RoomClassificationFinishResolutionResult
    {
        public RoomClassificationFinishResolutionResult(
            IReadOnlyList<RoomClassificationRecord> resolvedRecords,
            IReadOnlyList<string> unresolvedRoomCodes)
        {
            ResolvedRecords = resolvedRecords;
            UnresolvedRoomCodes = unresolvedRoomCodes;
        }

        public IReadOnlyList<RoomClassificationRecord> ResolvedRecords { get; }

        // BA.Tls_RoomCode values that still have at least one blank finish field after resolution,
        // either because no active FinishPresets row matched "{Department}-{FinishTier}", or
        // because the matched preset itself has that field blank (e.g. a TODO skeleton row).
        public IReadOnlyList<string> UnresolvedRoomCodes { get; }
    }
}

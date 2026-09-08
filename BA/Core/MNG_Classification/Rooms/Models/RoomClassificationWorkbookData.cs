using System.Collections.Generic;

namespace BA.RoomClassification.Models
{
    // Holds both sheets read from one workbook open.
    internal sealed class RoomClassificationWorkbookData
    {
        public RoomClassificationWorkbookData(
            IReadOnlyList<RoomClassificationRecord> matrixRecords,
            IReadOnlyList<FinishPresetRecord> finishPresetRecords)
        {
            MatrixRecords = matrixRecords;
            FinishPresetRecords = finishPresetRecords;
        }

        public IReadOnlyList<RoomClassificationRecord> MatrixRecords { get; }
        public IReadOnlyList<FinishPresetRecord> FinishPresetRecords { get; }
    }
}

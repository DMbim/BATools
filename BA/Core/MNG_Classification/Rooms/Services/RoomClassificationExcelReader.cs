using ClosedXML.Excel;
using System.Collections.Generic;
using BA.RoomClassification.Models;
using BA.RoomClassification.Configuration;

namespace BA.RoomClassification.Services
{
    internal static class RoomClassificationExcelReader
    {
        private const string MatrixSheetName = "Matrix";
        private const string FinishPresetSheetName = "FinishPresets";

        public static RoomClassificationWorkbookData Read(string filePath)
        {
            using XLWorkbook workbook = new XLWorkbook(filePath);

            IXLWorksheet matrixWs = workbook.Worksheet(MatrixSheetName);
            if (matrixWs == null)
                throw new System.InvalidOperationException($"Worksheet '{MatrixSheetName}' was not found.");

            IReadOnlyList<RoomClassificationRecord> matrixRecords = ReadMatrixSheet(matrixWs);

            // FinishPresets is treated as optional for backward compatibility with older workbooks
            // that predate the finish preset feature. If the sheet is missing, every row simply has
            // no preset to fall back on - row-level overrides still work, everything else is reported
            // as unresolved by RoomClassificationFinishResolver rather than the whole import failing.
            IReadOnlyList<FinishPresetRecord> finishPresetRecords;
            if (workbook.Worksheets.TryGetWorksheet(FinishPresetSheetName, out IXLWorksheet finishWs))
                finishPresetRecords = ReadFinishPresetSheet(finishWs);
            else
                finishPresetRecords = new List<FinishPresetRecord>();

            return new RoomClassificationWorkbookData(matrixRecords, finishPresetRecords);
        }

        private static IReadOnlyList<RoomClassificationRecord> ReadMatrixSheet(IXLWorksheet ws)
        {
            Dictionary<string, int> headerMap = ExcelReaderUtil.BuildHeaderMap(ws);

            // FinishTier / Floor Finish / Wall Finish / Ceiling Finish / Base Finish are intentionally
            // NOT in this list. They are optional columns - missing column or blank cell both resolve
            // to string.Empty, and no validation error is raised for either at the read stage. Whether
            // that blank is actually acceptable is decided later, by RoomClassificationFinishResolver.
            string[] required =
            {
                RoomClassificationMatrixHeaders.RoomKey, RoomClassificationMatrixHeaders.ProgramType,
                RoomClassificationMatrixHeaders.Department, RoomClassificationMatrixHeaders.RoomFunction,
                RoomClassificationMatrixHeaders.RoomCode, RoomClassificationMatrixHeaders.RoomGroup,
                RoomClassificationMatrixHeaders.IsActive, RoomClassificationMatrixHeaders.Notes
            };
            ExcelReaderUtil.EnsureRequiredHeaders(headerMap, required, MatrixSheetName);

            List<RoomClassificationRecord> result = new List<RoomClassificationRecord>();
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            for (int row = 2; row <= lastRow; row++)
            {
                if (ExcelReaderUtil.IsEntireRowEmpty(ws, row, headerMap))
                    continue;

                RoomClassificationRecord record = new RoomClassificationRecord
                {
                    RoomKey = ExcelReaderUtil.GetCellString(ws, row, headerMap, RoomClassificationMatrixHeaders.RoomKey),
                    ProgramType = ExcelReaderUtil.GetCellString(ws, row, headerMap, RoomClassificationMatrixHeaders.ProgramType),
                    Department = ExcelReaderUtil.GetCellString(ws, row, headerMap, RoomClassificationMatrixHeaders.Department),
                    RoomFunction = ExcelReaderUtil.GetCellString(ws, row, headerMap, RoomClassificationMatrixHeaders.RoomFunction),
                    RoomCode = ExcelReaderUtil.GetCellString(ws, row, headerMap, RoomClassificationMatrixHeaders.RoomCode),
                    RoomGroup = ExcelReaderUtil.GetCellString(ws, row, headerMap, RoomClassificationMatrixHeaders.RoomGroup),
                    FinishTier = ExcelReaderUtil.GetCellString(ws, row, headerMap, RoomClassificationMatrixHeaders.FinishTier),
                    FloorFinish = ExcelReaderUtil.GetCellString(ws, row, headerMap, RoomClassificationMatrixHeaders.FloorFinish),
                    WallFinish = ExcelReaderUtil.GetCellString(ws, row, headerMap, RoomClassificationMatrixHeaders.WallFinish),
                    CeilingFinish = ExcelReaderUtil.GetCellString(ws, row, headerMap, RoomClassificationMatrixHeaders.CeilingFinish),
                    BaseFinish = ExcelReaderUtil.GetCellString(ws, row, headerMap, RoomClassificationMatrixHeaders.BaseFinish),
                    IsActive = ExcelReaderUtil.ParseBooleanLike(ExcelReaderUtil.GetCellString(ws, row, headerMap, RoomClassificationMatrixHeaders.IsActive), true),
                    Notes = ExcelReaderUtil.GetCellString(ws, row, headerMap, RoomClassificationMatrixHeaders.Notes),
                    SourceRowNumber = row
                };

                if (record.IsActive)
                    result.Add(record);
            }
            return result;
        }

        private static IReadOnlyList<FinishPresetRecord> ReadFinishPresetSheet(IXLWorksheet ws)
        {
            Dictionary<string, int> headerMap = ExcelReaderUtil.BuildHeaderMap(ws);

            string[] required =
            {
                RoomClassificationFinishPresetHeaders.FinishKey, RoomClassificationFinishPresetHeaders.FloorFinish,
                RoomClassificationFinishPresetHeaders.WallFinish, RoomClassificationFinishPresetHeaders.CeilingFinish,
                RoomClassificationFinishPresetHeaders.BaseFinish, RoomClassificationFinishPresetHeaders.IsActive,
                RoomClassificationFinishPresetHeaders.Notes
            };
            ExcelReaderUtil.EnsureRequiredHeaders(headerMap, required, FinishPresetSheetName);

            List<FinishPresetRecord> result = new List<FinishPresetRecord>();
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            for (int row = 2; row <= lastRow; row++)
            {
                if (ExcelReaderUtil.IsEntireRowEmpty(ws, row, headerMap))
                    continue;

                FinishPresetRecord record = new FinishPresetRecord
                {
                    FinishKey = ExcelReaderUtil.GetCellString(ws, row, headerMap, RoomClassificationFinishPresetHeaders.FinishKey),
                    FloorFinish = ExcelReaderUtil.GetCellString(ws, row, headerMap, RoomClassificationFinishPresetHeaders.FloorFinish),
                    WallFinish = ExcelReaderUtil.GetCellString(ws, row, headerMap, RoomClassificationFinishPresetHeaders.WallFinish),
                    CeilingFinish = ExcelReaderUtil.GetCellString(ws, row, headerMap, RoomClassificationFinishPresetHeaders.CeilingFinish),
                    BaseFinish = ExcelReaderUtil.GetCellString(ws, row, headerMap, RoomClassificationFinishPresetHeaders.BaseFinish),
                    IsActive = ExcelReaderUtil.ParseBooleanLike(ExcelReaderUtil.GetCellString(ws, row, headerMap, RoomClassificationFinishPresetHeaders.IsActive), true),
                    Notes = ExcelReaderUtil.GetCellString(ws, row, headerMap, RoomClassificationFinishPresetHeaders.Notes),
                    SourceRowNumber = row
                };

                if (record.IsActive)
                    result.Add(record);
            }
            return result;
        }
    }
}

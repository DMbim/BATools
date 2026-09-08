using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using BA.RoomClassification.Models;
using BA.RoomClassification.Services;
using BA.RoomClassification.Configuration;

namespace BA.RoomClassification.Commands
{
    [Transaction(TransactionMode.Manual)]
    public sealed class RoomClassificationImportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return Run(commandData.Application, ref message);
        }

        public static Result Run(UIApplication uiApp, ref string message)
        {
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc?.Document;
            if (doc == null)
            {
                message = "No active document.";
                return Result.Failed;
            }

            if (doc.IsFamilyDocument)
            {
                message = "This command must run in a project document, not in the family editor.";
                return Result.Failed;
            }

            string filePath = PickExcelFile();
            if (string.IsNullOrWhiteSpace(filePath))
                return Result.Cancelled;

            RoomClassificationWorkbookData workbookData;
            try
            {
                workbookData = RoomClassificationExcelReader.Read(filePath);
            }
            catch (Exception ex)
            {
                message = "Failed to read Excel file.\n\n" + ex.Message;
                return Result.Failed;
            }

            RoomClassificationValidationResult validation =
                RoomClassificationValidator.Validate(workbookData.MatrixRecords, workbookData.FinishPresetRecords);
            if (!validation.IsValid)
            {
                message = validation.BuildMessage();
                return Result.Failed;
            }

            RoomClassificationFinishResolutionResult finishResolution;
            try
            {
                finishResolution = RoomClassificationFinishResolver.Resolve(
                    workbookData.MatrixRecords, workbookData.FinishPresetRecords);
            }
            catch (Exception ex)
            {
                message = "Failed to resolve finish presets.\n\n" + ex.Message;
                return Result.Failed;
            }

            using (TransactionGroup tg = new TransactionGroup(doc, "Import Room Classification"))
            {
                tg.Start();

                IList<RoomClassificationParameterDefinition> parameterDefinitions =
                    RoomClassificationParameterCatalog.BuildDefault();

                using (Transaction t1 = new Transaction(doc, "Ensure room shared parameters"))
                {
                    t1.Start();
                    RoomClassificationSharedParameterService.EnsureRoomParameters(
                        uiApp.Application, doc, parameterDefinitions);
                    t1.Commit();
                }

                ViewSchedule keySchedule;
                using (Transaction t2 = new Transaction(doc, "Ensure room key schedule"))
                {
                    t2.Start();
                    keySchedule = RoomClassificationScheduleService.EnsureRoomKeySchedule(
                        doc, parameterDefinitions);
                    t2.Commit();
                }

                RoomClassificationSyncResult syncResult;
                using (Transaction t3 = new Transaction(doc, "Sync room classification data"))
                {
                    t3.Start();
                    syncResult = RoomClassificationSyncService.UpsertRoomClassificationKeys(
                        doc, keySchedule, finishResolution.ResolvedRecords);
                    t3.Commit();
                }

                syncResult.UnresolvedFinishRoomCodes.AddRange(finishResolution.UnresolvedRoomCodes);

                tg.Assimilate();

                TaskDialog.Show("Room Classification Import", syncResult.BuildMessage());
            }

            return Result.Succeeded;
        }

        private static string PickExcelFile()
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Select Room Classification Excel File",
                Filter = "Excel Workbook (*.xlsx)|*.xlsx|Excel Macro Workbook (*.xlsm)|*.xlsm",
                Multiselect = false,
                CheckFileExists = true
            };

            // Default to the network Rooms folder when it is reachable at the moment the dialog
            // opens. Guarded with Directory.Exists rather than set unconditionally: if the share
            // is temporarily unmapped or the user is off VPN, InitialDirectory is simply left
            // unset and OpenFileDialog falls back to its normal OS-defined default, instead of
            // producing undefined or version-dependent dialog behavior against a path that does
            // not currently resolve.
            if (Directory.Exists(RoomClassificationDefaultPathsConfig.DefaultExcelFolder))
                dialog.InitialDirectory = RoomClassificationDefaultPathsConfig.DefaultExcelFolder;

            return dialog.ShowDialog() == true ? dialog.FileName : string.Empty;
        }
    }
}
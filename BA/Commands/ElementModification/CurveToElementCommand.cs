// File: BA/Commands/CurveToElement/CurveToElementCommand.cs
// Action: CREATE NEW

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using BA.BAApplication;
using BA.Core.CurveToElement.Infrastructure;
using BA.Core.CurveToElement.Models;
using BA.Core.CurveToElement.Services;
using BA.ViewModels.CurveToElement;
using System.Windows.Interop;

namespace BA.Commands.CurveToElement
{
    /// <summary>
    /// Entry point for the Curve-to-Element (detail line -> wall) tool. Prompts the user to
    /// select detail lines, classifies them by line style, and opens the settings window.
    /// The actual Wall.Create transaction happens later, asynchronously, via
    /// WallGenerationRequestHandler when the user clicks Generate in the window - this command's
    /// Execute() only needs Revit API access for the initial selection and read-only lookups
    /// (wall types, levels, units), all of which are safe to do directly here since Execute()
    /// already runs on Revit's main thread with a valid API context.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CurveToElementCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return Run(commandData.Application, ref message);
        }

        public static Result Run(UIApplication uiApp, ref string message)
        {
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            if (uiDoc == null)
            {
                message = "No active document.";
                return Result.Failed;
            }

            Document doc = uiDoc.Document;

            IList<Reference> pickedReferences;
            try
            {
                pickedReferences = uiDoc.Selection.PickObjects(
                    ObjectType.Element,
                    new DetailLineSelectionFilter(),
                    "Select detail lines to convert to walls, then click Finish.");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                AppLogger.LogInfo("CurveToElementCommand.Run: selection cancelled by user.");
                return Result.Cancelled;
            }

            if (pickedReferences == null || pickedReferences.Count == 0)
            {
                AppLogger.LogInfo("CurveToElementCommand.Run: no detail lines selected.");
                return Result.Cancelled;
            }

            List<ElementId> detailCurveIds = pickedReferences.Select(r => r.ElementId).ToList();

            var classificationService = new DetailLineClassificationService();
            List<CurveTypeGroup> classifiedGroups = classificationService.ClassifyByLineStyle(doc, detailCurveIds);

            if (classifiedGroups.Count == 0)
            {
                TaskDialog.Show("Curve to Element", "No valid detail curves found in the selection.");
                return Result.Cancelled;
            }

            ObservableCollection<WallTypeOption> availableWallTypes = CollectWallTypes(doc);
            ObservableCollection<LevelOption> availableLevels = CollectLevels(doc);

            if (availableWallTypes.Count == 0)
            {
                TaskDialog.Show("Curve to Element", "No wall types found in this document.");
                return Result.Cancelled;
            }

            if (availableLevels.Count == 0)
            {
                TaskDialog.Show("Curve to Element", "No levels found in this document.");
                return Result.Cancelled;
            }

            var previewHandler = new WallFaceOffsetPreviewHandler();
            var generationHandler = new WallGenerationRequestHandler();

            var windowViewModel = new CurveToElementWindowViewModel(
                classifiedGroups,
                availableWallTypes,
                availableLevels,
                doc.GetUnits(),
                previewHandler);

            windowViewModel.RequestGenerate = (requests, onComplete) =>
                generationHandler.RequestGeneration(requests, onComplete);

            // Window construction/ownership/Show() intentionally left to the code-behind layer,
            // consistent with LedgerSettingsWindow - see CurveToElementWindow, next piece.
            var window = new BA.UI.CurveToElement.CurveToElementWindow(windowViewModel);

            var windowInteropHelper = new System.Windows.Interop.WindowInteropHelper(window);
            windowInteropHelper.Owner = uiApp.MainWindowHandle; // <- NEW: without this, modeless window input capture is unreliable in Revit's host

            window.Show();

            return Result.Succeeded;
        }

        private static ObservableCollection<WallTypeOption> CollectWallTypes(Document doc)
        {
            var result = new ObservableCollection<WallTypeOption>();

            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .OrderBy(wt => wt.Name, StringComparer.OrdinalIgnoreCase);

            foreach (WallType wallType in collector)
            {
                result.Add(new WallTypeOption(wallType.Id, wallType.Name, wallType.Kind));
            }

            return result;
        }

        private static ObservableCollection<LevelOption> CollectLevels(Document doc)
        {
            var result = new ObservableCollection<LevelOption>();

            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation);

            foreach (Level level in collector)
            {
                result.Add(new LevelOption(level.Id, level.Name, level.Elevation));
            }

            return result;
        }
    }
}
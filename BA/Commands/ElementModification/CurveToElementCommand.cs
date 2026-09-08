// File: BA/Commands/CurveToElement/CurveToElementCommand.cs
// Action: REPLACE (full file)

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
    /// Entry point for the Curve-to-Element (detail line/model line -> wall) tool. Prompts the
    /// user to select detail lines and/or model lines, classifies them by line style, resolves a
    /// default Base Level from the active view when possible, and opens the settings window. The
    /// actual Wall.Create transaction happens later, asynchronously, via
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
                    "Select detail lines or model lines to convert to walls, then click Finish."); // <- CHANGED wording (was "detail lines" only)
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                AppLogger.LogInfo("CurveToElementCommand.Run: selection cancelled by user.");
                return Result.Cancelled;
            }

            if (pickedReferences == null || pickedReferences.Count == 0)
            {
                AppLogger.LogInfo("CurveToElementCommand.Run: no detail lines or model lines selected.");
                return Result.Cancelled;
            }

            List<ElementId> curveElementIds = pickedReferences.Select(r => r.ElementId).ToList(); // <- CHANGED (renamed from detailCurveIds)

            var classificationService = new DetailLineClassificationService();
            List<CurveTypeGroup> classifiedGroups = classificationService.ClassifyByLineStyle(doc, curveElementIds);

            if (classifiedGroups.Count == 0)
            {
                TaskDialog.Show("Curve to Element", "No valid detail curves or model curves found in the selection.");
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

            // <- NEW: preselect Base Level from the active view's level, when the active view is
            // a ViewPlan (floor plan, ceiling plan, area plan, structural plan, ...). Any other
            // active view type (section, elevation, drafting view, sheet, 3D view - the last of
            // which is the only place a model line can realistically be picked without a level
            // context) leaves this null and the Base Level combo starts unselected, same as
            // before this change.
            LevelOption defaultBaseLevel = ResolveActiveViewLevel(uiDoc.ActiveView, availableLevels);

            var previewHandler = new WallFaceOffsetPreviewHandler();
            var generationHandler = new WallGenerationRequestHandler();

            var windowViewModel = new CurveToElementWindowViewModel(
                classifiedGroups,
                availableWallTypes,
                availableLevels,
                doc.GetUnits(),
                previewHandler,
                defaultBaseLevel); // <- NEW argument

            windowViewModel.RequestGenerate = (requests, deleteSourceLines, onComplete) =>
                generationHandler.RequestGeneration(requests, deleteSourceLines, onComplete);

            // Window construction/ownership/Show() intentionally left to the code-behind layer,
            // consistent with LedgerSettingsWindow - see CurveToElementWindow.
            var window = new BA.UI.CurveToElement.CurveToElementWindow(windowViewModel);

            var windowInteropHelper = new System.Windows.Interop.WindowInteropHelper(window);
            windowInteropHelper.Owner = uiApp.MainWindowHandle; // without this, modeless window input capture is unreliable in Revit's host

            window.Show();

            return Result.Succeeded;
        }

        /// <summary>
        /// Resolves the Base Level to preselect from the view the curves were picked in.
        /// ViewPlan.GenLevel is the only reliable, unambiguous view-to-level mapping Revit
        /// exposes - ViewSection (which covers both sections and elevations), ViewDrafting, and
        /// sheets do not carry a GenLevel at all, and 3D views obviously don't either. For those
        /// cases this returns null and the window leaves Base Level unselected, matching the
        /// "Active view's level only" behavior decided on rather than trying to infer a level
        /// from curve geometry.
        /// </summary>
        private static LevelOption ResolveActiveViewLevel(View activeView, ObservableCollection<LevelOption> availableLevels) // <- NEW
        {
            if (!(activeView is ViewPlan viewPlan))
                return null;

            Level genLevel = viewPlan.GenLevel;
            if (genLevel == null)
                return null;

            return availableLevels.FirstOrDefault(l => l.Id == genLevel.Id);
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
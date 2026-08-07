// BA/Markup/Commands/PlaceMarkupCommand.cs
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using BA.Markup.Models;
using BA.Markup.Services;
using BA.Markup.Settings;
using BA.Markup.ViewModels;
using BA.Markup.Views;

namespace BA.Markup.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class PlaceMarkupCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            var uiApp = commandData.Application;
            var uiDoc = uiApp.ActiveUIDocument;
            var doc = uiDoc.Document;

            var activeView = doc.ActiveView;
            if (activeView == null)
            {
                message = "No active view.";
                return Result.Failed;
            }

            var settings = MarkupSettings.Load<MarkupSettings>();
            var currentUser = uiApp.Application.Username;
            var currentDate = DateTime.Now.ToString("yyyy-MM-dd");

            // All Revit API calls here are synchronous — we are inside Execute()
            // on the Revit API thread. No ExternalEvent needed for modal windows.

            IReadOnlyList<RevisionItem> LoadRevisions()
                => RevisionManagerHandler.ReadAllRevisions(doc);

            RevisionItem SaveRevision(RevisionEditModel model)
                => RevisionManagerHandler.SaveRevisionSync(doc, model);

            RevisionItem CreateRevision(RevisionEditModel model)
                => RevisionManagerHandler.CreateRevisionSync(doc, model);

            var initialRevisions = LoadRevisions();

            // <- NEW: active assignee suggestions for the Markup dialog's assignee picker.
            //    Sourced from MarkupUserRegistryService, independent of the Ledger. Never
            //    throws; GetActiveUsers already degrades to an empty list on any failure,
            //    so the dialog still opens with free-text-only entry if this can't resolve.
            IReadOnlyList<string> activeAssigneeUsers =
                MarkupUserRegistryService.GetActiveUsers(doc, settings.MarkupCleanupRetentionMonths);

            // ----------------------------------------------------------------
            // MarkupWindow
            // ----------------------------------------------------------------
            MarkupWindow? markupWindow = null;
            MarkupViewModel? markupViewModel = null;

            markupViewModel = new MarkupViewModel(
                currentUser,
                currentDate,
                initialRevisions,
                activeAssigneeUsers,
                refreshRevisionsCallback: () =>
                {
                    OpenRevisionManager(
                        uiApp.MainWindowHandle,   // <- CHANGED: pass handle, not WPF window
                        markupViewModel!,
                        LoadRevisions,
                        SaveRevision,
                        CreateRevision);
                });

            markupWindow = new MarkupWindow(markupViewModel);
            new WindowInteropHelper(markupWindow).Owner = uiApp.MainWindowHandle;
            markupWindow.ShowDialog();

            if (!markupViewModel.Confirmed)
                return Result.Cancelled;

            var input = markupViewModel.BuildModel();

            // ----------------------------------------------------------------
            // Pick area
            // ----------------------------------------------------------------
            BoundingBoxXYZ? boundingBox;
            try
            {
                boundingBox = PickBoundingBox(uiDoc, settings);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("BA Markup — Selection Error", ex.Message);
                return Result.Failed;
            }

            if (boundingBox == null)
            {
                TaskDialog.Show("BA Markup",
                    "Could not compute a bounding area from the selection.");
                return Result.Failed;
            }

            // ----------------------------------------------------------------
            // Place markup or revision cloud in a single transaction
            // ----------------------------------------------------------------
            var service = new MarkupService(uiDoc, settings);
            using var tx = new Transaction(doc, "BA — Place Markup");
            try
            {
                tx.Start();

                if (input.Mode == MarkupMode.InternalComment)
                    service.PlaceInternalMarkup(input, boundingBox, activeView);
                else
                    service.PlaceRevisionCloud(input, boundingBox, activeView);

                tx.Commit();
            }
            catch (Exception ex)
            {
                if (tx.GetStatus() == TransactionStatus.Started)
                    tx.RollBack();
                TaskDialog.Show("BA Markup — Placement Error", ex.Message);
                return Result.Failed;
            }

            return Result.Succeeded;
        }

        // ------------------------------------------------------------------ //
        //  REVISION MANAGER WINDOW
        // ------------------------------------------------------------------ //

        private static void OpenRevisionManager(
          IntPtr revitHandle,
          MarkupViewModel markupViewModel,
          Func<IReadOnlyList<RevisionItem>> loadRevisions,
          Func<RevisionEditModel, RevisionItem> saveRevision,
          Func<RevisionEditModel, RevisionItem> createRevision)
        {
            var managerViewModel = new RevisionManagerViewModel(
                loadRevisions(),
                loadRevisions,
                saveRevision,
                createRevision);

            var managerWindow = new RevisionManagerWindow(managerViewModel);
            new System.Windows.Interop.WindowInteropHelper(managerWindow).Owner = revitHandle;
            managerWindow.ShowDialog();

            if (managerViewModel.SelectedResult != null)
            {
                markupViewModel.UpdateRevisions(GetManagerRevisions(managerViewModel));
                markupViewModel.SelectedRevision = managerViewModel.SelectedResult;
            }
        }

        private static IReadOnlyList<RevisionItem> GetManagerRevisions(
            RevisionManagerViewModel vm)
        {
            // <- CHANGED: RevisionView (ICollectionView) removed from ViewModel.
            //    Use GetAllRevisions() which returns the master list directly.
            return vm.GetAllRevisions();
        }

        // ------------------------------------------------------------------ //
        //  PICK LOGIC
        // ------------------------------------------------------------------ //

        private static BoundingBoxXYZ? PickBoundingBox(
            UIDocument uiDoc,
            MarkupSettings settings)
        {
            var service = new MarkupService(uiDoc, settings);
            var pickedBox = uiDoc.Selection.PickBox(
                PickBoxStyle.Enclosing,
                "Draw a rectangle around the area to mark up");

            if (pickedBox == null) return null;

            return service.GetBoundingBoxFromPoints(pickedBox.Min, pickedBox.Max);
        }
    }
}
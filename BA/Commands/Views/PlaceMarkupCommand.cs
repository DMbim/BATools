// BA/Markup/Commands/PlaceMarkupCommand.cs
using System;
using System.Collections.Generic;
using System.Windows.Interop;
using System.Windows.Threading;
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
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
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
            var revisions = ReadRevisions(doc);

            var refreshHandler = new RefreshRevisionsHandler();
            var refreshEvent = ExternalEvent.Create(refreshHandler);

            MarkupViewModel? viewModel = null;
            viewModel = new MarkupViewModel(         // <- CHANGED: now calls the single correct
                currentUser,                         //    4-parameter constructor; second (dead)
                currentDate,                         //    constructor has been deleted from the VM.
                revisions,
                refreshRevisionsCallback: () =>
                {
                    refreshHandler.Prepare(viewModel!, Dispatcher.CurrentDispatcher);
                    refreshEvent.Raise();
                });

            var window = new MarkupWindow(viewModel);
            new WindowInteropHelper(window).Owner = uiApp.MainWindowHandle;
            window.ShowDialog();

            if (!viewModel.Confirmed)
                return Result.Cancelled;

            var input = viewModel.BuildModel();

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
                TaskDialog.Show("BA Markup", "Could not compute a bounding area from the selection.");
                return Result.Failed;
            }

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
        //  PICK LOGIC
        // ------------------------------------------------------------------ //
        private static BoundingBoxXYZ? PickBoundingBox(
            UIDocument uiDoc,
            MarkupSettings settings)                  // <- CHANGED: removed unused input/activeView
        {                                             //    parameters; PickBox needs neither.
            var service = new MarkupService(uiDoc, settings);

            var pickedBox = uiDoc.Selection.PickBox(
                PickBoxStyle.Enclosing,
                "Draw a rectangle around the area to mark up");

            if (pickedBox == null) return null;

            return service.GetBoundingBoxFromPoints(pickedBox.Min, pickedBox.Max);
        }

        // ------------------------------------------------------------------ //
        //  REVISION READER
        // ------------------------------------------------------------------ //
        private static List<RevisionItem> ReadRevisions(Document doc)
        {
            var result = new List<RevisionItem>();
            var ids = Revision.GetAllRevisionIds(doc);

            foreach (var id in ids)
            {
                if (doc.GetElement(id) is Revision rev)
                {
                    result.Add(new RevisionItem
                    {
                        ElementId = (int)id.Value,
                        DisplayName = $"{rev.SequenceNumber} — {rev.Description} ({rev.RevisionDate})"
                    });
                }
            }

            return result;
        }
    }
}
// FILE: BA_Tools/Warnings/ExternalEvents/HighlightWarningElementsHandler.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.UI.ExternalEvents;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace BA.Warnings.ExternalEvents
{
    // Still a singleton instance, not static, unlike the other five: this one
    // genuinely needs to remember which view and elements it last applied
    // overrides to, across separate button clicks, so Clear only ever touches
    // what this tool itself applied, never a blanket reset that could stomp
    // the user's own manual overrides elsewhere.
    public sealed class HighlightWarningElementsHandler
    {
        public static HighlightWarningElementsHandler Instance { get; } = new HighlightWarningElementsHandler();

        private ElementId _appliedViewId;
        private List<ElementId> _appliedElementIds = new List<ElementId>();

        private HighlightWarningElementsHandler() { }

        public void RequestHighlight(ICollection<ElementId> elementIds, Action<bool> onCompleted)
        {
            if (elementIds == null || elementIds.Count == 0)
            {
                onCompleted?.Invoke(false);
                return;
            }

            AppExternalInvoker.Instance.Run(
                app => ExecuteHighlight(app, elementIds),
                onCompleted,
                ex =>
                {
                    AppLogger.LogError("HighlightWarningElementsHandler.RequestHighlight", ex);
                    onCompleted?.Invoke(false);
                });
        }

        public void RequestClear(Action<bool> onCompleted)
        {
            AppExternalInvoker.Instance.Run(
                app => ExecuteClear(app),
                onCompleted,
                ex =>
                {
                    AppLogger.LogError("HighlightWarningElementsHandler.RequestClear", ex);
                    onCompleted?.Invoke(false);
                });
        }

        private bool ExecuteHighlight(UIApplication app, ICollection<ElementId> requestedIds)
        {
            UIDocument uiDoc = app.ActiveUIDocument;
            if (uiDoc == null) return false;

            Document doc = uiDoc.Document;
            View view = doc.ActiveView;

            List<ElementId> liveIds = requestedIds.Where(id => doc.GetElement(id) != null).ToList();
            if (liveIds.Count == 0)
            {
                TaskDialog.Show("Highlight Elements", "None of this warning's elements still exist in the model. Refresh the dashboard.");
                return false;
            }

            using (var t = new Transaction(doc, "BA Highlight Warning Elements"))
            {
                t.Start();

                ClearPreviousHighlightInTransaction(doc);

                ElementId solidFillId = GetSolidFillPatternId(doc);
                var ogs = new OverrideGraphicSettings();
                Color highlightColor = new Color(255, 0, 255);

                ogs.SetProjectionLineColor(highlightColor);
                ogs.SetProjectionLineWeight(6);
                if (solidFillId != null && solidFillId != ElementId.InvalidElementId)
                {
                    ogs.SetSurfaceForegroundPatternColor(highlightColor);
                    ogs.SetSurfaceForegroundPatternId(solidFillId);
                }

                foreach (ElementId id in liveIds)
                {
                    view.SetElementOverrides(id, ogs);
                }

                t.Commit();
            }

            _appliedViewId = view.Id;
            _appliedElementIds = liveIds;
            return true;
        }

        private bool ExecuteClear(UIApplication app)
        {
            UIDocument uiDoc = app.ActiveUIDocument;
            if (uiDoc == null) return true;

            Document doc = uiDoc.Document;

            using (var t = new Transaction(doc, "BA Clear Warning Highlights"))
            {
                t.Start();
                ClearPreviousHighlightInTransaction(doc);
                t.Commit();
            }

            return true;
        }

        private void ClearPreviousHighlightInTransaction(Document doc)
        {
            if (_appliedViewId == null || _appliedElementIds.Count == 0) return;

            if (doc.GetElement(_appliedViewId) is View previousView)
            {
                var defaultOgs = new OverrideGraphicSettings();
                foreach (ElementId id in _appliedElementIds)
                {
                    if (doc.GetElement(id) == null) continue;
                    try { previousView.SetElementOverrides(id, defaultOgs); }
                    catch { /* element may no longer be valid in that view, ignore */ }
                }
            }

            _appliedViewId = null;
            _appliedElementIds = new List<ElementId>();
        }

        private static ElementId GetSolidFillPatternId(Document doc)
        {
            FillPatternElement solid = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(f => f.GetFillPattern().IsSolidFill);

            return solid?.Id;
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BATools.SelectionManager.ExternalEvents
{
    public enum ViewOperationType
    {
        IsolateTemporary,
        HideElements,
        ResetTemporaryHideIsolate,
        OverrideColor,
        ResetOverrides
    }

    public class ViewOperationHandler : IExternalEventHandler
    {
        public ViewOperationType Operation { get; set; }
        /// <summary>
        /// Optional explicit element list. When empty, the handler reads
        /// the current Revit selection at execution time.
        /// </summary>
        public List<ElementId> ElementIds { get; set; } = new();
        public int ColorArgb { get; set; } = unchecked((int)0xFFFF0000);

        public void Execute(UIApplication uiApp)
        {
            var uidoc = uiApp.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null || uidoc == null) return;

            View? activeView = uidoc.ActiveView;
            if (activeView == null) return;

            // Read current Revit selection when caller passed no explicit IDs  // <- KEY FIX
            List<ElementId> ids = ElementIds.Count > 0
                ? ElementIds
                : uidoc.Selection.GetElementIds().ToList();

            // Operations that don't need elements can proceed with empty list
            bool needsElements = Operation != ViewOperationType.ResetTemporaryHideIsolate;
            if (needsElements && ids.Count == 0) return;

            using var tx = new Transaction(doc, $"BATools: {Operation}");
            try
            {
                var failOpts = tx.GetFailureHandlingOptions();
                failOpts.SetFailuresPreprocessor(new SilentFailurePreprocessor());
                tx.SetFailureHandlingOptions(failOpts);
                tx.Start();

                switch (Operation)
                {
                    case ViewOperationType.IsolateTemporary:
                        activeView.IsolateElementsTemporary(ids);
                        break;

                    case ViewOperationType.HideElements:
                        activeView.HideElements(ids);
                        break;

                    case ViewOperationType.ResetTemporaryHideIsolate:
                        activeView.DisableTemporaryViewMode(
                            TemporaryViewMode.TemporaryHideIsolate);
                        break;

                    case ViewOperationType.OverrideColor:
                        ApplyColorOverride(doc, activeView, ids);
                        break;

                    case ViewOperationType.ResetOverrides:
                        ResetOverrides(activeView, ids);
                        break;
                }

                tx.Commit();
            }
            catch (Exception ex)
            {
                if (tx.HasStarted()) tx.RollBack();
                System.Diagnostics.Debug.WriteLine(
                    $"[ViewOperationHandler] {ex.Message}");
            }
        }

        private void ApplyColorOverride(Document doc, View view, List<ElementId> ids)
        {
            var color = new Color(
                (byte)((ColorArgb >> 16) & 0xFF),
                (byte)((ColorArgb >> 8) & 0xFF),
                (byte)(ColorArgb & 0xFF));

            var ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(color);
            ogs.SetSurfaceForegroundPatternColor(color);
            ogs.SetSurfaceForegroundPatternVisible(true);

            var solidPattern = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);

            if (solidPattern != null)
                ogs.SetSurfaceForegroundPatternId(solidPattern.Id);

            foreach (var id in ids)
            {
                try { view.SetElementOverrides(id, ogs); }
                catch { /* skip invalid elements */ }
            }
        }

        private void ResetOverrides(View view, List<ElementId> ids)
        {
            var empty = new OverrideGraphicSettings();
            foreach (var id in ids)
            {
                try { view.SetElementOverrides(id, empty); }
                catch { }
            }
        }

        public string GetName() => "ViewOperation";
    }

    internal class SilentFailurePreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
        {
            foreach (var f in a.GetFailureMessages())
                if (f.GetSeverity() == FailureSeverity.Warning)
                    a.DeleteWarning(f);
            return FailureProcessingResult.Continue;
        }
    }
}
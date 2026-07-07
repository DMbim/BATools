using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;
using BATools.SelectionManager.Models;
using BATools.SelectionManager.Services;

namespace BATools.SelectionManager.ExternalEvents
{
    public class RecallSetHandler : IExternalEventHandler
    {
        public Guid SetId { get; set; }

        public void Execute(UIApplication uiApp)
        {
            var doc = uiApp.ActiveUIDocument?.Document;
            var uidoc = uiApp.ActiveUIDocument;
            if (doc == null || uidoc == null) return;

            SelectionSet? set = SetRepository.Instance.GetById(SetId);
            if (set == null) return;
            if (set.UniqueIds.Count == 0) return;

            List<Autodesk.Revit.DB.ElementId> ids =
                ElementIdResolver.Instance.Resolve(set.UniqueIds, doc);

            if (ids.Count == 0)
            {
                SetRepository.Instance.MarkHealth(set.Id, SetHealthStatus.FullyStale, set.UniqueIds.Count);
                return;
            }

            try
            {
                uidoc.Selection.SetElementIds(ids);
                uidoc.ShowElements(ids);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RecallSetHandler] {ex.Message}");
            }
        }

        public string GetName() => "RecallSelectionSet";
    }
}
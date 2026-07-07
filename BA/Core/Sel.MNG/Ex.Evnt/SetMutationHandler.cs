using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BATools.SelectionManager.Models;
using BATools.SelectionManager.Services;

namespace BATools.SelectionManager.ExternalEvents
{
    public enum SetMutationType { AddCurrentSelection, RemoveCurrentSelection, GetCurrentSelection }

    public class SetMutationHandler : IExternalEventHandler
    {
        public SetMutationType Operation { get; set; }
        public Guid TargetSetId { get; set; }
        public Action<List<string>>? OnSelectionRead { get; set; }

        public void Execute(UIApplication uiApp)
        {
            var doc = uiApp.ActiveUIDocument?.Document;
            var uidoc = uiApp.ActiveUIDocument;
            if (doc == null || uidoc == null) return;

            ICollection<ElementId> selectedIds;
            try { selectedIds = uidoc.Selection.GetElementIds(); }
            catch { return; }

            if (Operation == SetMutationType.GetCurrentSelection)
            {
                var uids = selectedIds
                    .Select(id => doc.GetElement(id)?.UniqueId)
                    .Where(u => u != null).Cast<string>().ToList();
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                    new Action(() => OnSelectionRead?.Invoke(uids)));
                return;
            }

            SelectionSet? set = SetRepository.Instance.GetById(TargetSetId);
            if (set == null) return;

            var selectedUids = selectedIds
                .Select(id => doc.GetElement(id)?.UniqueId)
                .Where(u => u != null).Cast<string>().ToHashSet();

            if (Operation == SetMutationType.AddCurrentSelection)
            {
                foreach (var uid in selectedUids)
                    if (!set.UniqueIds.Contains(uid))
                        set.UniqueIds.Add(uid);
            }
            else if (Operation == SetMutationType.RemoveCurrentSelection)
            {
                set.UniqueIds.RemoveAll(uid => selectedUids.Contains(uid));
            }

            if (set.UniqueIds.Count == 0)
                set.HealthStatus = SetHealthStatus.Empty;
            else
                set.HealthStatus = SetHealthStatus.Healthy;

            SetRepository.Instance.Update(set);
        }

        public string GetName() => "SetMutation";
    }
}
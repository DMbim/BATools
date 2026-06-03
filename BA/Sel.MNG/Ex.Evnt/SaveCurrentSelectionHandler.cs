using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BATools.SelectionManager.Models;
using BATools.SelectionManager.Services;

namespace BATools.SelectionManager.ExternalEvents
{
    public class SaveCurrentSelectionHandler : IExternalEventHandler
    {
        public string SetName { get; set; } = string.Empty;
        public Action<SelectionSet>? OnComplete { get; set; }

        public void Execute(UIApplication uiApp)
        {
            var doc = uiApp.ActiveUIDocument?.Document;
            var uidoc = uiApp.ActiveUIDocument;
            if (doc == null || uidoc == null) return;

            ICollection<ElementId> selectedIds;
            try
            {
                selectedIds = uidoc.Selection.GetElementIds();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SaveCurrentSelectionHandler] {ex.Message}");
                return;
            }

            if (selectedIds.Count == 0) return;

            // Convert ElementIds → UniqueIds for persistent storage
            var uniqueIds = selectedIds
                .Select(id => doc.GetElement(id)?.UniqueId)
                .Where(uid => uid != null)
                .Cast<string>()
                .ToList();

            // Pre-populate resolver cache
            foreach (var id in selectedIds)
            {
                var element = doc.GetElement(id);
                if (element != null)
                    ElementIdResolver.Instance.Resolve(new[] { element.UniqueId }, doc);
            }

            string fingerprint = DocumentFingerprint.Compute(doc.PathName, doc.Title);

            var newSet = new SelectionSet
            {
                Name = SetName,
                UniqueIds = uniqueIds,
                DocumentFingerprint = fingerprint,
                HealthStatus = SetHealthStatus.Healthy
            };

            SetRepository.Instance.Add(newSet);

            // Marshal result back to WPF thread
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                new Action(() => OnComplete?.Invoke(newSet)));
        }

        public string GetName() => "SaveCurrentSelection";
    }
}
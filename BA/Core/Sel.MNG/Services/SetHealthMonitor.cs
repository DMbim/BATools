using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using BATools.SelectionManager.Models;

namespace BATools.SelectionManager.Services
{
    /// <summary>
    /// Subscribes to DocumentChanged and updates set health status
    /// when elements belonging to saved sets are deleted.
    /// </summary>
    public class SetHealthMonitor
    {
        private static readonly SetHealthMonitor _instance = new();
        public static SetHealthMonitor Instance => _instance;

        public event Action<Guid, SetHealthStatus, int>? HealthChanged;

        private SetHealthMonitor() { }

        public void OnDocumentChanged(object? sender, DocumentChangedEventArgs args)
        {
            ICollection<ElementId> deleted = args.GetDeletedElementIds();
            if (deleted.Count == 0) return;

            List<string> invalidatedUids =
                ElementIdResolver.Instance.FindInvalidatedUniqueIds(deleted);

            if (invalidatedUids.Count == 0) return;

            var invalidSet = new HashSet<string>(invalidatedUids);
            var allSets = SetRepository.Instance.GetAll();

            foreach (var set in allSets)
            {
                if (set.UniqueIds.Count == 0)
                {
                    SetRepository.Instance.MarkHealth(set.Id, SetHealthStatus.Empty, 0);
                    HealthChanged?.Invoke(set.Id, SetHealthStatus.Empty, 0);
                    continue;
                }

                int staleCount = set.UniqueIds.Count(uid => invalidSet.Contains(uid));
                if (staleCount == 0) continue;

                SetHealthStatus status = staleCount >= set.UniqueIds.Count
                    ? SetHealthStatus.FullyStale
                    : SetHealthStatus.PartiallyStale;

                SetRepository.Instance.MarkHealth(set.Id, status, staleCount);
                HealthChanged?.Invoke(set.Id, status, staleCount);
            }
        }
    }
}
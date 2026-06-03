using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace BATools.SelectionManager.Services
{
    /// <summary>
    /// Resolves UniqueIds (persistent across sessions) to runtime ElementIds.
    /// Maintains a bidirectional cache to support health monitoring.
    /// Must only be called from within a valid Revit API context.
    /// </summary>
    public class ElementIdResolver
    {
        private static readonly ElementIdResolver _instance = new();
        public static ElementIdResolver Instance => _instance;

        // uniqueId → elementId (runtime, per-document)
        private readonly Dictionary<string, ElementId> _uidToId = new();
        // elementId.Value → uniqueId (for reverse lookup on DocumentChanged)
        private readonly Dictionary<long, string> _idToUid = new();

        private ElementIdResolver() { }

        public void InvalidateCache()
        {
            _uidToId.Clear();
            _idToUid.Clear();
        }

        public List<ElementId> Resolve(IEnumerable<string> uniqueIds, Document document)
        {
            var result = new List<ElementId>();
            var ids = uniqueIds.ToList();

            foreach (string uid in ids)
            {
                if (_uidToId.TryGetValue(uid, out var cachedId))
                {
                    // Validate cache entry is still alive
                    if (document.GetElement(cachedId) != null)
                    {
                        result.Add(cachedId);
                        continue;
                    }
                    // Cache entry stale — remove
                    _uidToId.Remove(uid);
                    _idToUid.Remove(cachedId.Value);
                }

                // Resolve fresh
                Element? element = document.GetElement(uid);
                if (element == null) continue;

                _uidToId[uid] = element.Id;
                _idToUid[element.Id.Value] = uid;
                result.Add(element.Id);
            }

            return result;
        }

        /// <summary>
        /// Checks which UniqueIds are now invalid given a set of deleted ElementIds.
        /// Called from DocumentChanged handler.
        /// </summary>
        public List<string> FindInvalidatedUniqueIds(ICollection<ElementId> deletedIds)
        {
            var invalidated = new List<string>();
            foreach (var id in deletedIds)
            {
                if (_idToUid.TryGetValue(id.Value, out string? uid))
                {
                    invalidated.Add(uid);
                    _idToUid.Remove(id.Value);
                    _uidToId.Remove(uid);
                }
            }
            return invalidated;
        }
    }
}
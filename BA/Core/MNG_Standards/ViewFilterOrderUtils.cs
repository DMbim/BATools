// File: BA.Core/Standards/ViewFilterOrderUtils.cs
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.Core.Standards
{
    public static class ViewFilterOrderUtils
    {
        private sealed class FilterState
        {
            public ElementId FilterId { get; }
            public bool Visible { get; }
            public bool? Enabled { get; }
            public OverrideGraphicSettings Overrides { get; }

            public FilterState(ElementId id, bool visible, bool? enabled, OverrideGraphicSettings ogs)
            {
                FilterId = id;
                Visible = visible;
                Enabled = enabled;
                Overrides = ogs;
            }
        }

        /// <summary>
        /// Reorders filters in the view by removing all and adding back in desired order.
        /// Preserves visibility, enabled state (if supported), and overrides for each filter.
        /// Must be executed inside an active Transaction.
        /// </summary>
        public static void ReorderFiltersPreserveStates(View view, IList<ElementId> desiredOrder)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (desiredOrder == null) throw new ArgumentNullException(nameof(desiredOrder));

            var current = view.GetFilters().ToList();
            if (current.Count == 0) return;

            // Desired order must be a permutation of current filters, but we tolerate:
            // - missing ids (ignored)
            // - extra ids (ignored)
            var desired = desiredOrder.Where(id => id != null && current.Contains(id)).Distinct().ToList();
            if (desired.Count == 0) return;

            // Append any current filters not in desired (keeps them, places at end)
            foreach (var id in current)
                if (!desired.Contains(id))
                    desired.Add(id);

            // If already identical, do nothing
            if (SequenceEqual(current, desired))
                return;

            // Snapshot states
            var states = new Dictionary<ElementId, FilterState>();
            foreach (var fid in current)
            {
                bool vis = true;
                OverrideGraphicSettings ogs = new OverrideGraphicSettings();
                bool? enabled = null;

                try { vis = view.GetFilterVisibility(fid); } catch { /* ignore */ }
                try { ogs = view.GetFilterOverrides(fid) ?? new OverrideGraphicSettings(); } catch { /* ignore */ }

                // Some view types / templates support enabled state
                try { enabled = view.GetIsFilterEnabled(fid); } catch { enabled = null; }

                states[fid] = new FilterState(fid, vis, enabled, ogs);
            }

            // Remove all filters
            // (reverse to reduce any internal ordering weirdness)
            for (int i = current.Count - 1; i >= 0; i--)
            {
                var fid = current[i];
                try { view.RemoveFilter(fid); } catch { /* ignore */ }
            }

            // Add back in desired order and restore states
            foreach (var fid in desired)
            {
                try { view.AddFilter(fid); } catch { continue; }

                if (!states.TryGetValue(fid, out var st)) continue;

                try { view.SetFilterVisibility(fid, st.Visible); } catch { /* ignore */ }

                if (st.Enabled.HasValue)
                {
                    try { view.SetIsFilterEnabled(fid, st.Enabled.Value); } catch { /* ignore */ }
                }

                try { view.SetFilterOverrides(fid, st.Overrides); } catch { /* ignore */ }
            }
        }

        private static bool SequenceEqual(IList<ElementId> a, IList<ElementId> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] == null || b[i] == null) return false;
                if (a[i].Value != b[i].Value) return false;
            }
            return true;
        }
    }
}

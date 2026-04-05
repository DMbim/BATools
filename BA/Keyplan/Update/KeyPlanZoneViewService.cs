using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.Keyplan
{
    public static class KeyplanZoneViewService
    {
        public static ViewDrafting CreateOrUpdateSheetSpecificKeyplanView(
            Document doc,
            ViewDrafting baseView,
            string targetViewName,
            string zoneCode,
            ElementId activeFilledRegionTypeId,
            ElementId inactiveFilledRegionTypeId,
            bool reuseExisting,
            out bool createdNew)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (baseView == null) throw new ArgumentNullException(nameof(baseView));
            if (string.IsNullOrWhiteSpace(targetViewName)) throw new ArgumentException("Target view name is required.", nameof(targetViewName));
            if (string.IsNullOrWhiteSpace(zoneCode)) throw new ArgumentException("Zone code is required.", nameof(zoneCode));

            createdNew = false;

            ViewDrafting targetView = null;

            if (reuseExisting)
            {
                targetView = KeyplanViewUtils.FindViewByName(doc, targetViewName) as ViewDrafting;
            }

            if (targetView == null)
            {
                ElementId duplicatedId = baseView.Duplicate(ViewDuplicateOption.WithDetailing);
                targetView = doc.GetElement(duplicatedId) as ViewDrafting;
                if (targetView == null)
                    throw new InvalidOperationException("Failed to duplicate the base drafting view.");

                targetView.Name = targetViewName;
                createdNew = true;
            }

            ApplyZoneHighlighting(doc, targetView, zoneCode, activeFilledRegionTypeId, inactiveFilledRegionTypeId);

            return targetView;
        }

        private static void ApplyZoneHighlighting(
            Document doc,
            ViewDrafting view,
            string activeZoneCode,
            ElementId activeFilledRegionTypeId,
            ElementId inactiveFilledRegionTypeId)
        {
            IList<FilledRegion> regions = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(FilledRegion))
                .Cast<FilledRegion>()
                .Where(x => x != null)
                .ToList();

            foreach (FilledRegion fr in regions)
            {
                string zoneCode = KeyplanFilledRegionUtils.GetZoneCodeFromFilledRegion(fr);

                if (string.IsNullOrWhiteSpace(zoneCode))
                {
                    // leave non-zoned regions as they are
                    continue;
                }

                if (string.Equals(zoneCode, activeZoneCode, StringComparison.OrdinalIgnoreCase))
                {
                    if (fr.GetTypeId() != activeFilledRegionTypeId)
                        fr.ChangeTypeId(activeFilledRegionTypeId);
                }
                else
                {
                    if (fr.GetTypeId() != inactiveFilledRegionTypeId)
                        fr.ChangeTypeId(inactiveFilledRegionTypeId);
                }
            }
        }
    }
}
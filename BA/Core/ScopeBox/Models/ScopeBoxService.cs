using Autodesk.Revit.DB;
using BA.Core.Views.ScopeBoxes;
using System;
using System.Collections.Generic;
using System.Linq;
using View = Autodesk.Revit.DB.View;

namespace BA.Core.Views.ScopeBoxes
{
    public static class ScopeBoxService
    {
        public static List<ScopeBoxInfo> GetAllScopeBoxes(Document doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            var result = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                .WhereElementIsNotElementType()
                .Select(e => new ScopeBoxInfo(
                    e.Id,
                    GetScopeBoxName(e)))
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return result;
        }

        public static List<ViewScopeRow> GetEligibleViews(Document doc, ICollection<ElementId> restrictToViewIds = null)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            IEnumerable<View> views = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate)
                .Where(v => !v.Name.StartsWith("<"))
                .Where(v => v.ViewType != ViewType.Internal);

            if (restrictToViewIds != null && restrictToViewIds.Count > 0)
            {
                HashSet<ElementId> set = new HashSet<ElementId>(restrictToViewIds);
                views = views.Where(v => set.Contains(v.Id));
            }

            var rows = new List<ViewScopeRow>();

            foreach (View view in views.OrderBy(v => v.ViewType.ToString()).ThenBy(v => v.Name))
            {
                Parameter scopeParam = GetViewScopeBoxParameter(view);
                if (scopeParam == null)
                    continue;

                string currentScopeName = GetCurrentScopeBoxName(doc, scopeParam);
                bool isLocked = scopeParam.IsReadOnly;
                string status = isLocked ? "Locked by template or view state" : "Ready";

                Element typeElem = doc.GetElement(view.GetTypeId());
                string viewTypeName = typeElem?.Name ?? string.Empty;
                string familyName = GetViewFamilyTypeFamilyName(typeElem);

                rows.Add(new ViewScopeRow(
                    view.Id,
                    view.Name,
                    viewTypeName,
                    familyName,
                    currentScopeName,
                    isLocked,
                    status));
            }

            return rows;
        }

        public static int ApplyScopeBox(Document doc, IEnumerable<ViewScopeRow> rows, ElementId scopeBoxId)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (scopeBoxId == null || scopeBoxId == ElementId.InvalidElementId)
                throw new ArgumentException("A valid scope box id is required.", nameof(scopeBoxId));

            int changed = 0;

            using (Transaction tx = new Transaction(doc, "Apply Scope Box"))
            {
                tx.Start();

                foreach (ViewScopeRow row in rows.Where(r => r.IsChecked))
                {
                    View view = doc.GetElement(row.ViewId) as View;
                    if (view == null)
                    {
                        row.Status = "View not found";
                        continue;
                    }

                    Parameter p = GetViewScopeBoxParameter(view);
                    if (p == null)
                    {
                        row.Status = "No scope box parameter";
                        continue;
                    }

                    if (p.IsReadOnly)
                    {
                        row.Status = "Locked by template or view state";
                        row.IsLocked = true;
                        continue;
                    }

                    if (p.StorageType != StorageType.ElementId)
                    {
                        row.Status = "Unexpected parameter storage";
                        continue;
                    }

                    if (p.AsElementId() == scopeBoxId)
                    {
                        row.Status = "Already assigned";
                        row.CurrentScopeBoxName = GetScopeBoxName(doc.GetElement(scopeBoxId));
                        continue;
                    }

                    bool ok = p.Set(scopeBoxId);
                    if (ok)
                    {
                        changed++;
                        row.CurrentScopeBoxName = GetScopeBoxName(doc.GetElement(scopeBoxId));
                        row.Status = "Assigned";
                        row.IsLocked = p.IsReadOnly;
                    }
                    else
                    {
                        row.Status = "Assignment failed";
                    }
                }

                tx.Commit();
            }

            return changed;
        }

        public static int ClearScopeBox(Document doc, IEnumerable<ViewScopeRow> rows)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (rows == null) throw new ArgumentNullException(nameof(rows));

            int changed = 0;

            using (Transaction tx = new Transaction(doc, "Clear Scope Box"))
            {
                tx.Start();

                foreach (ViewScopeRow row in rows.Where(r => r.IsChecked))
                {
                    View view = doc.GetElement(row.ViewId) as View;
                    if (view == null)
                    {
                        row.Status = "View not found";
                        continue;
                    }

                    Parameter p = GetViewScopeBoxParameter(view);
                    if (p == null)
                    {
                        row.Status = "No scope box parameter";
                        continue;
                    }

                    if (p.IsReadOnly)
                    {
                        row.Status = "Locked by template or view state";
                        row.IsLocked = true;
                        continue;
                    }

                    if (p.StorageType != StorageType.ElementId)
                    {
                        row.Status = "Unexpected parameter storage";
                        continue;
                    }

                    if (p.AsElementId() == ElementId.InvalidElementId)
                    {
                        row.Status = "Already empty";
                        row.CurrentScopeBoxName = string.Empty;
                        continue;
                    }

                    bool ok = p.Set(ElementId.InvalidElementId);
                    if (ok)
                    {
                        changed++;
                        row.CurrentScopeBoxName = string.Empty;
                        row.Status = "Cleared";
                        row.IsLocked = p.IsReadOnly;
                    }
                    else
                    {
                        row.Status = "Clear failed";
                    }
                }

                tx.Commit();
            }

            return changed;
        }

        public static void RefreshRowState(Document doc, ViewScopeRow row)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (row == null) throw new ArgumentNullException(nameof(row));

            View view = doc.GetElement(row.ViewId) as View;
            if (view == null)
            {
                row.Status = "View not found";
                return;
            }

            Parameter p = GetViewScopeBoxParameter(view);
            if (p == null)
            {
                row.Status = "No scope box parameter";
                row.CurrentScopeBoxName = string.Empty;
                row.IsLocked = false;
                return;
            }

            row.CurrentScopeBoxName = GetCurrentScopeBoxName(doc, p);
            row.IsLocked = p.IsReadOnly;
            row.Status = row.IsLocked ? "Locked by template or view state" : "Ready";
        }

        public static Parameter GetViewScopeBoxParameter(View view)
        {
            if (view == null) return null;
            return view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
        }

        public static string GetScopeBoxName(Element scopeBox)
        {
            if (scopeBox == null) return string.Empty;

            Parameter p = scopeBox.get_Parameter(BuiltInParameter.VOLUME_OF_INTEREST_NAME);
            if (p != null && p.HasValue)
            {
                string name = p.AsString();
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }

            return scopeBox.Name ?? string.Empty;
        }

        private static string GetCurrentScopeBoxName(Document doc, Parameter scopeParam)
        {
            if (doc == null || scopeParam == null) return string.Empty;

            if (scopeParam.StorageType != StorageType.ElementId)
                return string.Empty;

            ElementId id = scopeParam.AsElementId();
            if (id == null || id == ElementId.InvalidElementId)
                return string.Empty;

            Element scopeBox = doc.GetElement(id);
            return GetScopeBoxName(scopeBox);
        }

        private static string GetViewFamilyTypeFamilyName(Element typeElem)
        {
            if (typeElem == null) return string.Empty;

            Parameter familyParam = typeElem.get_Parameter(BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM);
            if (familyParam != null && familyParam.HasValue)
            {
                string value = familyParam.AsString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return string.Empty;
        }
    }
}
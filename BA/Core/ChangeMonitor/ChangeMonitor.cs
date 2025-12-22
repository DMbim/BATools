using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Color = Autodesk.Revit.DB.Color;
using View = Autodesk.Revit.DB.View;

namespace BA.Core
{
    // Helper for curve endpoints
    internal struct CurveEnds
    {
        public XYZ Start;
        public XYZ End;
        public CurveEnds(XYZ s, XYZ e) { Start = s; End = e; }
    }

    #region Public DTOs

    public enum ChangeKind
    {
        Added,
        Deleted,
        Modified,
        Moved
    }

    public class ParamChange
    {
        public string ParamName { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
    }

    public class ChangeRecord
    {
        public DateTime When { get; set; }
        public ChangeKind ChangeType { get; set; }
        public ElementId ElementId { get; set; }
        public string Category { get; set; }
        public ElementId ViewId { get; set; } // may be null/invalid
        public string ViewName { get; set; }
        public string Username { get; set; }
        public string TransactionNames { get; set; }

        public List<ParamChange> ParameterChanges { get; } = new List<ParamChange>();
    }

    public class ChangeReport
    {
        public Document Document { get; }
        public IReadOnlyList<ChangeRecord> Records { get; }

        internal ChangeReport(Document doc, List<ChangeRecord> recs)
        {
            Document = doc;
            Records = recs.AsReadOnly();
        }

        public string GetSummaryText()
        {
            int adds = Records.Count(r => r.ChangeType == ChangeKind.Added);
            int dels = Records.Count(r => r.ChangeType == ChangeKind.Deleted);
            int moved = Records.Count(r => r.ChangeType == ChangeKind.Moved);
            int edited = Records.Count(r => r.ChangeType == ChangeKind.Modified);

            return
                $"Added: {adds}\n" +
                $"Deleted: {dels}\n" +
                $"Moved: {moved}\n" +
                $"Modified (parameters): {edited}\n" +
                $"Total: {Records.Count}";
        }
    }

    #endregion

    #region The service

    public static class ChangeMonitorService
    {
        private static UIApplication _uiApp;
        private static Document _doc;

        private static readonly Dictionary<ElementId, ElementSnapshot> _snapshots =
            new Dictionary<ElementId, ElementSnapshot>();

        private static readonly List<ChangeRecord> _records =
            new List<ChangeRecord>();

        private static readonly object _lock = new object();

        private static ElementId _currentViewId;
        private static string _currentViewName;

        public static bool IsRunning { get; private set; }

        /// <summary>
        /// Raised when new records are appended (for live window).
        /// </summary>
        public static event Action<IReadOnlyList<ChangeRecord>> RecordsAppended;

        public static void Start(UIApplication uiApp)
        {
            if (IsRunning) return;

            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _doc = _uiApp.ActiveUIDocument?.Document ?? throw new InvalidOperationException("No active document.");

            lock (_lock)
            {
                _records.Clear();
                _snapshots.Clear();
            }

            SeedInitialSnapshots(_doc);

            var av = _uiApp.ActiveUIDocument?.ActiveView;
            _currentViewId = av?.Id;
            _currentViewName = av?.Name;

            _uiApp.ViewActivated += UiApp_ViewActivated;
            _doc.Application.DocumentChanged += Application_DocumentChanged;

            IsRunning = true;
        }

        public static ChangeReport Stop()
        {
            if (!IsRunning) return null;

            if (_uiApp != null)
                _uiApp.ViewActivated -= UiApp_ViewActivated;

            if (_doc?.Application != null)
                _doc.Application.DocumentChanged -= Application_DocumentChanged;

            IsRunning = false;

            lock (_lock)
            {
                return new ChangeReport(_doc, new List<ChangeRecord>(_records));
            }
        }

        public static IReadOnlyList<ChangeRecord> GetRecordsSnapshot()
        {
            lock (_lock)
            {
                return new List<ChangeRecord>(_records).AsReadOnly();
            }
        }

        private static void UiApp_ViewActivated(object sender, ViewActivatedEventArgs e)
        {
            try
            {
                _currentViewId = e.CurrentActiveView?.Id;
                _currentViewName = e.CurrentActiveView?.Name;
            }
            catch
            {
                _currentViewId = null;
                _currentViewName = null;
            }
        }

        private static void Application_DocumentChanged(object sender, DocumentChangedEventArgs e)
        {
            if (!IsRunning) return;

            var doc = e.GetDocument();
            var appended = new List<ChangeRecord>();

            // per-event context
            string username = SafeUsername();
            string txn = "";

            try
            {
                var names = e.GetTransactionNames();
                if (names != null && names.Count > 0)
                    txn = string.Join(" | ", names);
            }
            catch { /* ignore */ }

            // Adds
            foreach (var id in e.GetAddedElementIds())
            {
                var rec = TryRecordAdd(doc, id, username, txn);
                if (rec != null) appended.Add(rec);
            }

            // Deletes
            foreach (var id in e.GetDeletedElementIds())
            {
                var rec = TryRecordDelete(id, username, txn);
                if (rec != null) appended.Add(rec);
                _snapshots.Remove(id);
            }

            // Modifies / moves
            foreach (var id in e.GetModifiedElementIds())
            {
                var recs = TryRecordModifyOrMove(doc, id, username, txn);
                if (recs != null && recs.Count > 0) appended.AddRange(recs);
            }

            if (appended.Count > 0)
            {
                lock (_lock)
                {
                    _records.AddRange(appended);
                }
                RecordsAppended?.Invoke(appended.AsReadOnly());
            }
        }

        private static string SafeUsername()
        {
            try
            {
                var u = _uiApp?.Application?.Username;
                if (!string.IsNullOrWhiteSpace(u)) return u;
            }
            catch { /* ignore */ }

            // Fallback to Windows username
            return Environment.UserName;
        }

        private static void SeedInitialSnapshots(Document doc)
        {
            var collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => !(e is View) && !(e is ElementType));

            foreach (var e in collector)
            {
                if (e.Category == null) continue;
                var snap = SafeSnapshot(doc, e);
                if (snap != null)
                    _snapshots[e.Id] = snap;
            }
        }

        private static ElementSnapshot SafeSnapshot(Document doc, Element e)
        {
            try { return new ElementSnapshot(doc, e); }
            catch { return null; }
        }

        private static ChangeRecord TryRecordAdd(Document doc, ElementId id, string user, string txn)
        {
            try
            {
                var el = doc.GetElement(id);
                if (el == null || el.Category == null) return null;

                var rec = new ChangeRecord
                {
                    When = DateTime.Now,
                    ChangeType = ChangeKind.Added,
                    ElementId = id,
                    Category = el.Category?.Name,
                    ViewId = _currentViewId,
                    ViewName = _currentViewName,
                    Username = user,
                    TransactionNames = txn
                };

                var snap = SafeSnapshot(doc, el);
                if (snap != null)
                    _snapshots[id] = snap;

                return rec;
            }
            catch
            {
                return null;
            }
        }

        private static ChangeRecord TryRecordDelete(ElementId id, string user, string txn)
        {
            try
            {
                return new ChangeRecord
                {
                    When = DateTime.Now,
                    ChangeType = ChangeKind.Deleted,
                    ElementId = id,
                    Category = "(deleted)",
                    ViewId = _currentViewId,
                    ViewName = _currentViewName,
                    Username = user,
                    TransactionNames = txn
                };
            }
            catch
            {
                return null;
            }
        }

        private static List<ChangeRecord> TryRecordModifyOrMove(
            Document doc,
            ElementId id,
            string user,
            string txn)
        {
            var outList = new List<ChangeRecord>();

            try
            {
                var el = doc.GetElement(id);
                if (el == null || el.Category == null) return outList;

                if (!_snapshots.TryGetValue(id, out var before))
                {
                    var seeded = SafeSnapshot(doc, el);
                    if (seeded != null) _snapshots[id] = seeded;

                    outList.Add(new ChangeRecord
                    {
                        When = DateTime.Now,
                        ChangeType = ChangeKind.Modified,
                        ElementId = id,
                        Category = el.Category?.Name,
                        ViewId = _currentViewId,
                        ViewName = _currentViewName,
                        Username = user,
                        TransactionNames = txn
                    });
                    return outList;
                }

                bool moved = !before.LocationEquals(el);
                var diffs = before.DiffParams(doc, el).ToList();

                if (moved)
                {
                    outList.Add(new ChangeRecord
                    {
                        When = DateTime.Now,
                        ChangeType = ChangeKind.Moved,
                        ElementId = id,
                        Category = el.Category?.Name,
                        ViewId = _currentViewId,
                        ViewName = _currentViewName,
                        Username = user,
                        TransactionNames = txn
                    });
                }

                if (diffs.Count > 0)
                {
                    var rec = new ChangeRecord
                    {
                        When = DateTime.Now,
                        ChangeType = ChangeKind.Modified,
                        ElementId = id,
                        Category = el.Category?.Name,
                        ViewId = _currentViewId,
                        ViewName = _currentViewName,
                        Username = user,
                        TransactionNames = txn
                    };
                    rec.ParameterChanges.AddRange(diffs);
                    outList.Add(rec);
                }

                var after = SafeSnapshot(doc, el);
                if (after != null)
                    _snapshots[id] = after;

                return outList;
            }
            catch
            {
                return outList;
            }
        }
    }

    #endregion

    #region Internal snapshot model

    internal class ElementSnapshot
    {
        public ElementId Id { get; }
        public string StableUniqueId { get; }
        public XYZ LocationPoint { get; }
        public CurveEnds? LocationCurve { get; }

        public Dictionary<string, string> ParamValues { get; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public ElementSnapshot(Document doc, Element e)
        {
            Id = e.Id;
            StableUniqueId = e.UniqueId;

            (LocationPoint, LocationCurve) = ReadLocation(e);
            CaptureParams(doc, e, ParamValues);
        }

        public static (XYZ, CurveEnds?) ReadLocation(Element e)
        {
            var loc = e?.Location;
            if (loc is LocationPoint lp)
                return (lp.Point, null);

            if (loc is LocationCurve lc)
            {
                var crv = lc.Curve;
                return (null, new CurveEnds(crv.GetEndPoint(0), crv.GetEndPoint(1)));
            }

            return (null, null);
        }

        private static void CaptureParams(Document doc, Element e, Dictionary<string, string> dict)
        {
            foreach (Parameter p in e.Parameters)
            {
                if (p == null || p.IsReadOnly || p.StorageType == StorageType.None)
                    continue;

                string name = p.Definition?.Name ?? p.Id.ToString();
                string val = GetParamString(doc, p);
                if (val == null) continue;
                if (val.Length > 2000) val = val.Substring(0, 2000);

                if (!dict.ContainsKey(name))
                    dict.Add(name, val);
            }
        }

        private static string GetParamString(Document doc, Parameter p)
        {
            try
            {
                switch (p.StorageType)
                {
                    case StorageType.String:
                        return p.AsString();
                    case StorageType.Double:
                        return p.AsValueString() ?? p.AsDouble().ToString("R");
                    case StorageType.Integer:
                        return p.AsInteger().ToString();
                    case StorageType.ElementId:
                        var id = p.AsElementId();
                        if (id == ElementId.InvalidElementId) return "-1";
                        var el = doc.GetElement(id);
                        return el != null ? $"{id} ({el.Name})" : id.ToString();
                    default:
                        return null;
                }
            }
            catch
            {
                return null;
            }
        }

        public bool LocationEquals(Element other)
        {
            var (op, oc) = ReadLocation(other);

            if (LocationPoint != null && op != null)
                return LocationPoint.IsAlmostEqualTo(op);

            if (LocationCurve.HasValue && oc.HasValue)
            {
                return LocationCurve.Value.Start.IsAlmostEqualTo(oc.Value.Start)
                    && LocationCurve.Value.End.IsAlmostEqualTo(oc.Value.End);
            }

            return LocationPoint == null &&
                   !LocationCurve.HasValue &&
                   op == null &&
                   !oc.HasValue;
        }

        public IEnumerable<ParamChange> DiffParams(Document doc, Element current)
        {
            var now = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            CaptureParams(doc, current, now);

            foreach (var kv in ParamValues)
            {
                if (!now.TryGetValue(kv.Key, out var newVal))
                    continue;

                if (!StringEqualsLoose(kv.Value, newVal))
                {
                    yield return new ParamChange
                    {
                        ParamName = kv.Key,
                        OldValue = kv.Value,
                        NewValue = newVal
                    };
                }
            }
        }

        private static bool StringEqualsLoose(string a, string b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            return a.Trim().Equals(b.Trim(), StringComparison.Ordinal);
        }
    }

    #endregion

    #region Highlighter + BA_Change

    public static class Highlighter
    {
        private static readonly HashSet<(ElementId viewId, ElementId elemId)> _touched =
            new HashSet<(ElementId, ElementId)>(new PairComparer());

        public static void ApplyPerViewOverrides(ChangeReport report)
        {
            var doc = report.Document;

            var byView = report.Records
                .Where(r => r.ChangeType != ChangeKind.Deleted)
                .Where(r => r.ViewId != null && r.ViewId != ElementId.InvalidElementId)
                .GroupBy(r => r.ViewId, new ElementIdEqualityComparer());

            var red = new Color(255, 0, 0);
            var ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(red);

            foreach (var g in byView)
            {
                var viewId = g.Key;
                var view = doc.GetElement(viewId) as View;
                if (view == null) continue;

                var ids = new HashSet<ElementId>(new ElementIdEqualityComparer());
                foreach (var r in g)
                    ids.Add(r.ElementId);

                foreach (var id in ids)
                {
                    try
                    {
                        var el = doc.GetElement(id);
                        if (el == null) continue;

                        view.SetElementOverrides(id, ogs);
                        _touched.Add((viewId, id));
                    }
                    catch { /* ignore */ }
                }
            }
        }

        public static void ClearAllOverrides(Document doc)
        {
            var map = new Dictionary<ElementId, List<ElementId>>(new ElementIdEqualityComparer());
            foreach (var pair in _touched)
            {
                if (!map.TryGetValue(pair.viewId, out var list))
                {
                    list = new List<ElementId>();
                    map[pair.viewId] = list;
                }
                list.Add(pair.elemId);
            }

            var empty = new OverrideGraphicSettings();
            foreach (var kv in map)
            {
                var view = doc.GetElement(kv.Key) as View;
                if (view == null) continue;

                foreach (var id in kv.Value.Distinct(new ElementIdEqualityComparer()))
                {
                    try { view.SetElementOverrides(id, empty); } catch { }
                }
            }

            _touched.Clear();
        }

        public static void ApplyBAChangeTags(ChangeReport report)
        {
            var doc = report.Document;

            using (var t = new Transaction(doc, "BA_Change Tagging"))
            {
                t.Start();

                var changed = report.Records
                    .Where(r => r.ChangeType != ChangeKind.Deleted)
                    .Select(r => r.ElementId)
                    .Distinct(new ElementIdEqualityComparer())
                    .ToList();

                if (changed.Count == 0)
                {
                    t.Commit();
                    return;
                }

                bool anyTagged = false;

                foreach (var id in changed)
                {
                    var el = doc.GetElement(id);
                    if (el == null) continue;

                    var p = el.LookupParameter("BA_Change");
                    if (p == null || p.IsReadOnly) continue;

                    try
                    {
                        // Yes/No in Revit is integer 0/1
                        p.Set(1);
                        anyTagged = true;
                    }
                    catch { /* ignore */ }
                }

                if (anyTagged)
                {
                    EnsureBAChangeViewFilters(doc, report);
                }

                t.Commit();
            }
        }

        private static void EnsureBAChangeViewFilters(Document doc, ChangeReport report)
        {
            try
            {
                var paramElem = new FilteredElementCollector(doc)
                    .OfClass(typeof(ParameterElement))
                    .Cast<ParameterElement>()
                    .FirstOrDefault(pe => pe.GetDefinition()?.Name == "BA_Change");

                if (paramElem == null) return;

                var prov = new ParameterValueProvider(paramElem.Id);
                var rule = new FilterIntegerRule(prov, new FilterNumericEquals(), 1);
                var elemFilter = new ElementParameterFilter(rule);

                const string filterName = "BA_Change == Yes";

                var existing = new FilteredElementCollector(doc)
                    .OfClass(typeof(ParameterFilterElement))
                    .Cast<ParameterFilterElement>()
                    .FirstOrDefault(f => f.Name == filterName);

                ParameterFilterElement pfe = existing;

                if (pfe == null)
                {
                    var cats = new HashSet<ElementId>();
                    foreach (BuiltInCategory bic in (BuiltInCategory[])Enum.GetValues(typeof(BuiltInCategory)))
                    {
                        try
                        {
                            var cat = Category.GetCategory(doc, bic);
                            if (cat != null && !cat.IsTagCategory && cat.AllowsBoundParameters)
                                cats.Add(cat.Id);
                        }
                        catch { }
                    }

                    if (cats.Count == 0) return;

                    pfe = ParameterFilterElement.Create(doc, filterName, cats.ToList(), elemFilter);
                }
                else
                {
                    pfe.SetElementFilter(elemFilter);
                }

                var red = new Color(255, 0, 0);
                var ogs = new OverrideGraphicSettings();
                ogs.SetProjectionLineColor(red);
                ogs.SetSurfaceForegroundPatternColor(red);
                ogs.SetSurfaceForegroundPatternId(new ElementId(1)); // Solid fill (assuming id 1 is valid)

                var viewIds = report.Records
                    .Where(r => r.ViewId != null && r.ViewId != ElementId.InvalidElementId)
                    .Select(r => r.ViewId)
                    .Distinct(new ElementIdEqualityComparer());

                foreach (var vid in viewIds)
                {
                    var v = doc.GetElement(vid) as View;
                    if (v == null || v.IsTemplate) continue;

                    var filters = v.GetFilters();
                    if (!filters.Contains(pfe.Id))
                        v.AddFilter(pfe.Id);

                    v.SetFilterOverrides(pfe.Id, ogs);
                }
            }
            catch
            {
                // soft-fail; parameter might not be bound everywhere
            }
        }

        private class PairComparer : IEqualityComparer<(ElementId viewId, ElementId elemId)>
        {
            private readonly ElementIdEqualityComparer _inner = new ElementIdEqualityComparer();

            public bool Equals((ElementId viewId, ElementId elemId) x, (ElementId viewId, ElementId elemId) y)
            {
                return _inner.Equals(x.viewId, y.viewId) && _inner.Equals(x.elemId, y.elemId);
            }

            public int GetHashCode((ElementId viewId, ElementId elemId) obj)
            {
                unchecked
                {
                    int h1 = _inner.GetHashCode(obj.viewId);
                    int h2 = _inner.GetHashCode(obj.elemId);
                    return (h1 * 397) ^ h2;
                }
            }
        }
    }

    internal class ElementIdEqualityComparer : IEqualityComparer<ElementId>
    {
        public bool Equals(ElementId x, ElementId y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            return x.Equals(y);
        }

        public int GetHashCode(ElementId obj) => obj?.GetHashCode() ?? 0;
    }

    #endregion
}

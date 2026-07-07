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
        Moved,
        Modified,
        Deleted
    }

    public class ParamChange
    {
        public string ParamName { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
    }

    public class ChangeRecord
    {
        public Guid ActionId { get; set; }
        public DateTime When { get; set; }

        // Ordered, priority sorted list of change kinds that applied to this element
        // within a single user action (Idling cycle).
        public List<ChangeKind> ChangeTypes { get; } = new List<ChangeKind>();

        public string ChangeTypeDisplay => string.Join(" + ", ChangeTypes);

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
            int adds = Records.Count(r => r.ChangeTypes.Contains(ChangeKind.Added));
            int dels = Records.Count(r => r.ChangeTypes.Contains(ChangeKind.Deleted));
            int moved = Records.Count(r => r.ChangeTypes.Contains(ChangeKind.Moved));
            int edited = Records.Count(r => r.ChangeTypes.Contains(ChangeKind.Modified));

            return
                $"Added: {adds}\n" +
                $"Deleted: {dels}\n" +
                $"Moved: {moved}\n" +
                $"Modified (parameters): {edited}\n" +
                $"Total actions logged: {Records.Count}";
        }
    }

    #endregion

    #region The service

    public static class ChangeMonitorService
    {
        private static UIApplication _uiApp;
        private static Document _doc;

        private static readonly Dictionary<ElementId, ElementSnapshot> _snapshots =
            new Dictionary<ElementId, ElementSnapshot>(new ElementIdEqualityComparer());

        private static readonly List<ChangeRecord> _records =
            new List<ChangeRecord>();

        private static readonly object _lock = new object();

        private static ElementId _currentViewId;
        private static string _currentViewName;

        // Category names that are internal side effects of sketch mode and are never
        // meaningful to a user reviewing a change log. Filtered at accumulation time.
        private static readonly HashSet<string> _sketchNoiseCategories =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "<Sketch>",
                "Automatic Sketch Dimensions",
                "Work Plane Grid"
            };

        // Pending, unconsolidated changes for the action currently in progress.
        // A "batch" spans from the first DocumentChanged after the previous flush
        // up to the next Idling event, which is the real boundary of a user action.
        private static readonly Dictionary<ElementId, PendingElementChange> _currentBatch =
            new Dictionary<ElementId, PendingElementChange>(new ElementIdEqualityComparer());

        private static Guid _batchActionId = Guid.Empty;
        private static DateTime _batchStarted;
        private static ElementId _batchViewId;
        private static string _batchViewName;
        private static string _batchUsername;
        private static readonly HashSet<string> _batchTransactionNames = new HashSet<string>();

        public static bool IsRunning { get; private set; }

        /// <summary>
        /// Raised once per user action (on Idling), after consolidation and filtering.
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
                _currentBatch.Clear();
                _batchActionId = Guid.Empty;
                _batchTransactionNames.Clear();
            }

            SeedInitialSnapshots(_doc);

            var av = _uiApp.ActiveUIDocument?.ActiveView;
            _currentViewId = av?.Id;
            _currentViewName = av?.Name;

            _uiApp.ViewActivated += UiApp_ViewActivated;
            _uiApp.Idling += UiApp_Idling;
            _doc.Application.DocumentChanged += Application_DocumentChanged;

            IsRunning = true;
        }

        public static ChangeReport Stop()
        {
            if (!IsRunning) return null;

            // Flush whatever action is still pending so it is not lost.
            FinalizeBatch();

            if (_uiApp != null)
            {
                _uiApp.ViewActivated -= UiApp_ViewActivated;
                _uiApp.Idling -= UiApp_Idling;
            }

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

        private static void UiApp_Idling(object sender, IdlingEventArgs e)
        {
            if (!IsRunning) return;
            FinalizeBatch();
        }

        private static void Application_DocumentChanged(object sender, DocumentChangedEventArgs e)
        {
            if (!IsRunning) return;

            var doc = e.GetDocument();

            lock (_lock)
            {
                if (_currentBatch.Count == 0 && _batchActionId == Guid.Empty)
                {
                    _batchActionId = Guid.NewGuid();
                    _batchStarted = DateTime.Now;
                    _batchViewId = _currentViewId;
                    _batchViewName = _currentViewName;
                    _batchUsername = SafeUsername();
                    _batchTransactionNames.Clear();
                }

                try
                {
                    var names = e.GetTransactionNames();
                    if (names != null)
                    {
                        foreach (var n in names)
                            if (!string.IsNullOrWhiteSpace(n))
                                _batchTransactionNames.Add(n);
                    }
                }
                catch { /* ignore */ }

                foreach (var id in e.GetAddedElementIds())
                    AccumulateAdd(doc, id);

                foreach (var id in e.GetDeletedElementIds())
                {
                    AccumulateDelete(id);
                    _snapshots.Remove(id);
                }

                foreach (var id in e.GetModifiedElementIds())
                    AccumulateModifyOrMove(doc, id);
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

        private static bool IsSketchNoise(string categoryName)
        {
            return categoryName != null && _sketchNoiseCategories.Contains(categoryName);
        }

        private static void AccumulateAdd(Document doc, ElementId id)
        {
            try
            {
                var el = doc.GetElement(id);
                if (el == null || el.Category == null) return;

                var categoryName = el.Category.Name;
                if (IsSketchNoise(categoryName)) return;

                if (!_currentBatch.TryGetValue(id, out var pending))
                {
                    pending = new PendingElementChange
                    {
                        ElementId = id,
                        Category = categoryName,
                        FirstSeen = DateTime.Now
                    };
                    _currentBatch[id] = pending;
                }

                pending.WasAdded = true;
                pending.Category = categoryName;

                var snap = SafeSnapshot(doc, el);
                if (snap != null)
                    _snapshots[id] = snap;
            }
            catch { /* ignore */ }
        }

        private static void AccumulateDelete(ElementId id)
        {
            try
            {
                if (_currentBatch.TryGetValue(id, out var pending))
                {
                    // Element was added earlier in this same action and is now gone.
                    // Net effect for the user is nothing, mark for drop at flush time.
                    pending.WasDeleted = true;
                    return;
                }

                _currentBatch[id] = new PendingElementChange
                {
                    ElementId = id,
                    Category = "(deleted)",
                    FirstSeen = DateTime.Now,
                    WasDeleted = true
                };
            }
            catch { /* ignore */ }
        }

        private static void AccumulateModifyOrMove(Document doc, ElementId id)
        {
            try
            {
                var el = doc.GetElement(id);
                if (el == null || el.Category == null) return;

                var categoryName = el.Category.Name;
                if (IsSketchNoise(categoryName)) return;

                if (!_snapshots.TryGetValue(id, out var before))
                {
                    var seeded = SafeSnapshot(doc, el);
                    if (seeded != null) _snapshots[id] = seeded;

                    if (!_currentBatch.TryGetValue(id, out var freshPending))
                    {
                        freshPending = new PendingElementChange
                        {
                            ElementId = id,
                            Category = categoryName,
                            FirstSeen = DateTime.Now
                        };
                        _currentBatch[id] = freshPending;
                    }

                    freshPending.WasModified = true;
                    freshPending.Category = categoryName;
                    return;
                }

                bool moved = !before.LocationEquals(el);
                var diffs = before.DiffParams(doc, el).ToList();

                if (moved || diffs.Count > 0)
                {
                    if (!_currentBatch.TryGetValue(id, out var pending))
                    {
                        pending = new PendingElementChange
                        {
                            ElementId = id,
                            Category = categoryName,
                            FirstSeen = DateTime.Now
                        };
                        _currentBatch[id] = pending;
                    }

                    pending.Category = categoryName;

                    if (moved)
                        pending.WasMoved = true;

                    if (diffs.Count > 0)
                    {
                        pending.WasModified = true;
                        foreach (var d in diffs)
                        {
                            if (pending.ParamMerges.TryGetValue(d.ParamName, out var existing))
                            {
                                // keep earliest OldValue, advance NewValue
                                existing.NewValue = d.NewValue;
                            }
                            else
                            {
                                pending.ParamMerges[d.ParamName] = new ParamChange
                                {
                                    ParamName = d.ParamName,
                                    OldValue = d.OldValue,
                                    NewValue = d.NewValue
                                };
                            }
                        }
                    }
                }

                var after = SafeSnapshot(doc, el);
                if (after != null)
                    _snapshots[id] = after;
            }
            catch { /* ignore */ }
        }

        private static void FinalizeBatch()
        {
            List<ChangeRecord> toAppend;

            lock (_lock)
            {
                if (_currentBatch.Count == 0)
                {
                    _batchActionId = Guid.Empty;
                    return;
                }

                toAppend = _currentBatch.Values
                    .OrderBy(p => p.FirstSeen)
                    .Select(BuildChangeRecord)
                    .Where(r => r != null)
                    .ToList();

                _currentBatch.Clear();
                _batchActionId = Guid.Empty;
                _batchTransactionNames.Clear();
            }

            if (toAppend.Count == 0) return;

            lock (_lock)
            {
                _records.AddRange(toAppend);
            }

            RecordsAppended?.Invoke(toAppend.AsReadOnly());
        }

        private static ChangeRecord BuildChangeRecord(PendingElementChange p)
        {
            // Added then deleted within the same action nets to zero, this is how
            // sketch mode temp curves and similar internal artifacts get dropped
            // without needing a category blacklist for them.
            if (p.WasAdded && p.WasDeleted)
                return null;

            // Drop parameter entries that round tripped back to their original value
            // within this action, they are not a real change from the user's view.
            var finalParamChanges = p.ParamMerges.Values
                .Where(pc => !StringEqualsLoose(pc.OldValue, pc.NewValue))
                .ToList();

            var kinds = new List<ChangeKind>();

            if (p.WasDeleted)
            {
                kinds.Add(ChangeKind.Deleted);
            }
            else
            {
                if (p.WasAdded) kinds.Add(ChangeKind.Added);
                if (p.WasMoved) kinds.Add(ChangeKind.Moved);
                if (p.WasModified && finalParamChanges.Count > 0) kinds.Add(ChangeKind.Modified);
            }

            if (kinds.Count == 0)
                return null;

            var rec = new ChangeRecord
            {
                ActionId = _batchActionId,
                When = _batchStarted,
                ElementId = p.ElementId,
                Category = p.Category,
                ViewId = _batchViewId,
                ViewName = _batchViewName,
                Username = _batchUsername,
                TransactionNames = _batchTransactionNames.Count > 0
                    ? string.Join(" | ", _batchTransactionNames)
                    : null
            };

            rec.ChangeTypes.AddRange(kinds);

            if (!p.WasDeleted)
                rec.ParameterChanges.AddRange(finalParamChanges);

            return rec;
        }

        private static bool StringEqualsLoose(string a, string b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            return a.Trim().Equals(b.Trim(), StringComparison.Ordinal);
        }

        private class PendingElementChange
        {
            public ElementId ElementId;
            public string Category;
            public bool WasAdded;
            public bool WasDeleted;
            public bool WasMoved;
            public bool WasModified;
            public DateTime FirstSeen;
            public readonly Dictionary<string, ParamChange> ParamMerges =
                new Dictionary<string, ParamChange>(StringComparer.OrdinalIgnoreCase);
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

                if (!StringEqualsLooseStatic(kv.Value, newVal))
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

        private static bool StringEqualsLooseStatic(string a, string b)
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
                .Where(r => !r.ChangeTypes.Contains(ChangeKind.Deleted))
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
                    .Where(r => !r.ChangeTypes.Contains(ChangeKind.Deleted))
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
                ogs.SetSurfaceForegroundPatternId(new ElementId(1)); // Solid fill

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
                // soft fail, parameter might not be bound everywhere
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
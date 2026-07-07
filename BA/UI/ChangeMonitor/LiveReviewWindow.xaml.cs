using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Core;

namespace BA.UI
{
    public partial class LiveReviewWindow : Window
    {
        private readonly UIApplication _uiApp;
        private readonly ExternalEvent _showEvent;
        private readonly ShowElementHandler _showHandler;

        private readonly ExternalEvent _cloudEvent;
        private readonly AddRevisionCloudHandler _cloudHandler;

        // Canonical source: all raw change records from the service
        private readonly List<ChangeRecord> _allRecords = new List<ChangeRecord>();

        public ObservableCollection<ChangeRecordRow> Items { get; } =
            new ObservableCollection<ChangeRecordRow>();

        public LiveReviewWindow(UIApplication uiApp, ChangeReport report = null)
        {
            InitializeComponent();
            _uiApp = uiApp;

            _showHandler = new ShowElementHandler();
            _showEvent = ExternalEvent.Create(_showHandler);

            _cloudHandler = new AddRevisionCloudHandler();
            _cloudEvent = ExternalEvent.Create(_cloudHandler);

            GridChanges.ItemsSource = Items;

            try
            {
                if (CmbType != null && CmbType.SelectedIndex < 0)
                    CmbType.SelectedIndex = 0;
                if (CmbGroupMode != null && CmbGroupMode.SelectedIndex < 0)
                    CmbGroupMode.SelectedIndex = 0;
            }
            catch { }

            LoadSnapshot(report);

            ChangeMonitorService.RecordsAppended += OnRecordsAppended;
            Closed += (s, e) => ChangeMonitorService.RecordsAppended -= OnRecordsAppended;
        }

        private void LoadSnapshot(ChangeReport report = null)
        {
            _allRecords.Clear();

            IEnumerable<ChangeRecord> source =
                report?.Records ?? ChangeMonitorService.GetRecordsSnapshot();

            _allRecords.AddRange(source);
            ApplyFilters();
        }

        private void OnRecordsAppended(IReadOnlyList<ChangeRecord> appended)
        {
            Dispatcher.Invoke(() =>
            {
                _allRecords.AddRange(appended);

                if (ChkFollow.IsChecked == true)
                {
                    ApplyFilters(scrollToEnd: true);
                }
            });
        }

        private IEnumerable<ChangeRecordRow> GetSelectedRows()
        {
            if (GridChanges.SelectedItems == null || GridChanges.SelectedItems.Count == 0)
                yield break;

            foreach (var item in GridChanges.SelectedItems)
            {
                if (item is ChangeRecordRow row)
                    yield return row;
            }
        }

        private void OnShowClick(object sender, RoutedEventArgs e)
        {
            var first = GetSelectedRows().FirstOrDefault();
            if (first == null) return;

            _showHandler.Request(_uiApp, first.ViewIdRaw, first.ElementIdRaw);
            _showEvent.Raise();
        }

        private void OnAddCloudsClick(object sender, RoutedEventArgs e)
        {
            var rows = GetSelectedRows().ToList();
            if (rows.Count == 0)
            {
                MessageBox.Show(this, "Select at least one change record first.", "Change Monitor",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _cloudHandler.Request(_uiApp, rows);
            _cloudEvent.Raise();
        }

        private void GridChanges_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            OnShowClick(sender, e);
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

        private void OnRefreshClick(object sender, RoutedEventArgs e) => LoadSnapshot();

        private void OnFilterChanged(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            ApplyFilters();
        }

        private void ApplyFilters(bool scrollToEnd = false)
        {
            string type = "(All)";
            try
            {
                if (CmbType != null)
                {
                    var si = CmbType.SelectedItem;
                    if (si is ComboBoxItem cbi && cbi.Content != null)
                        type = cbi.Content.ToString();
                    else if (si != null)
                        type = si.ToString();
                }
            }
            catch { }

            string cat = "";
            string ptxt = "";

            try { cat = (TxtCategory?.Text ?? "").Trim().ToLowerInvariant(); } catch { }
            try { ptxt = (TxtParam?.Text ?? "").Trim().ToLowerInvariant(); } catch { }

            string groupMode = "None";
            try
            {
                if (CmbGroupMode != null)
                {
                    var si = CmbGroupMode.SelectedItem;
                    if (si is ComboBoxItem cbi && cbi.Content != null)
                        groupMode = cbi.Content.ToString();
                    else if (si != null)
                        groupMode = si.ToString();
                }
            }
            catch { }

            Items.Clear();

            IEnumerable<ChangeRecord> filtered = _allRecords;

            // Type filter
            if (!string.Equals(type, "(All)", StringComparison.OrdinalIgnoreCase))
            {
                if (Enum.TryParse<ChangeKind>(type, out var ck))
                    filtered = filtered.Where(r => r.ChangeTypes != null && r.ChangeTypes.Contains(ck));
                else
                    filtered = Enumerable.Empty<ChangeRecord>();
            }

            // Category filter
            if (!string.IsNullOrEmpty(cat))
            {
                filtered = filtered.Where(r =>
                    !string.IsNullOrEmpty(r.Category) &&
                    r.Category.ToLowerInvariant().Contains(cat));
            }

            // Param filter
            if (!string.IsNullOrEmpty(ptxt))
            {
                filtered = filtered.Where(r =>
                    r.ParameterChanges != null &&
                    r.ParameterChanges.Any(p =>
                        (!string.IsNullOrEmpty(p.ParamName) &&
                         p.ParamName.ToLowerInvariant().Contains(ptxt)) ||
                        (!string.IsNullOrEmpty(p.OldValue) &&
                         p.OldValue.ToLowerInvariant().Contains(ptxt)) ||
                        (!string.IsNullOrEmpty(p.NewValue) &&
                         p.NewValue.ToLowerInvariant().Contains(ptxt))));
            }

            List<ChangeRecordRow> rows;

            if (groupMode.StartsWith("Transaction", StringComparison.OrdinalIgnoreCase))
            {
                rows = filtered
                    .GroupBy(r => new
                    {
                        ChangeType = r.ChangeTypes.FirstOrDefault(),
                        r.Category,
                        r.ViewId,
                        r.ViewName,
                        r.Username,
                        r.TransactionNames
                    })
                    .Select(ChangeRecordRow.FromGroup)
                    .Where(r => r != null)
                    .ToList();
            }
            else if (groupMode.StartsWith("Time", StringComparison.OrdinalIgnoreCase))
            {
                rows = filtered
                    .GroupBy(r => new
                    {
                        // group by minute bucket
                        Bucket = new DateTime(r.When.Year, r.When.Month, r.When.Day,
                                              r.When.Hour, r.When.Minute, 0),
                        ChangeType = r.ChangeTypes.FirstOrDefault(),
                        r.Category,
                        r.ViewId,
                        r.ViewName,
                        r.Username
                    })
                    .Select(ChangeRecordRow.FromGroup)
                    .Where(r => r != null)
                    .ToList();
            }
            else
            {
                rows = filtered.Select(ChangeRecordRow.From).ToList();
            }

            foreach (var row in rows)
                Items.Add(row);

            try
            {
                if (scrollToEnd && Items.Count > 0 && GridChanges != null)
                    GridChanges.ScrollIntoView(Items[Items.Count - 1]);
            }
            catch { }
        }
    }

    public class ChangeRecordRow
    {
        public bool IsDone { get; set; }
        public bool IsGroup { get; set; }

        public string When { get; set; }
        public string ChangeType { get; set; }
        public string ElementId { get; set; }
        public string Category { get; set; }
        public string ViewName { get; set; }
        public string ViewId { get; set; }
        public string Username { get; set; }
        public string Transactions { get; set; }
        public string ParamSummary { get; set; }

        public ElementId ElementIdRaw { get; set; }
        public ElementId ViewIdRaw { get; set; }

        public List<ChangeRecord> GroupRecords { get; set; }

        public static ChangeRecordRow From(ChangeRecord r)
        {
            var summary = (r.ParameterChanges == null || r.ParameterChanges.Count == 0)
                ? ""
                : string.Join("; ", r.ParameterChanges.Select(p =>
                    $"{p.ParamName}: \"{Trim(p.OldValue)}\" → \"{Trim(p.NewValue)}\""));

            return new ChangeRecordRow
            {
                IsGroup = false,
                When = r.When.ToString("yyyy-MM-dd HH:mm:ss"),
                ChangeType = r.ChangeTypeDisplay ?? r.ChangeTypes.FirstOrDefault().ToString(),
                ElementId = r.ElementId?.ToString(),
                Category = r.Category,
                ViewName = r.ViewName,
                ViewId = r.ViewId?.ToString(),
                Username = r.Username,
                Transactions = r.TransactionNames,
                ParamSummary = summary,
                ElementIdRaw = r.ElementId,
                ViewIdRaw = r.ViewId,
                GroupRecords = new List<ChangeRecord> { r }
            };
        }

        public static ChangeRecordRow FromGroup(IGrouping<object, ChangeRecord> g)
        {
            var list = g.ToList();
            if (list.Count == 0) return null;

            var first = list.First();
            var whenMin = list.Min(r => r.When);
            var whenMax = list.Max(r => r.When);

            string whenText = whenMin == whenMax
                ? whenMin.ToString("yyyy-MM-dd HH:mm:ss")
                : $"{whenMin:yyyy-MM-dd HH:mm:ss} – {whenMax:HH:mm:ss} ({list.Count} items)";

            string elementText =
                $"{list.Select(r => r.ElementId).Distinct().Count()} elements";

            var allParamChanges = list
                .Where(r => r.ParameterChanges != null)
                .SelectMany(r => r.ParameterChanges);

            string paramSummary = allParamChanges.Any()
                ? string.Join("; ", allParamChanges.Take(5).Select(p =>
                    $"{p.ParamName}: \"{Trim(p.OldValue)}\" → \"{Trim(p.NewValue)}\"")) +
                  (allParamChanges.Count() > 5 ? " …" : "")
                : "";

            return new ChangeRecordRow
            {
                IsGroup = true,
                When = whenText,
                ChangeType = first.ChangeTypeDisplay ?? first.ChangeTypes.FirstOrDefault().ToString(),
                ElementId = elementText,
                Category = first.Category,
                ViewName = first.ViewName,
                ViewId = first.ViewId?.ToString(),
                Username = first.Username,
                Transactions = first.TransactionNames,
                ParamSummary = paramSummary,
                ElementIdRaw = first.ElementId,
                ViewIdRaw = first.ViewId,
                GroupRecords = list
            };
        }

        private static string Trim(string s)
        {
            if (s == null) return "";
            s = s.Trim();
            if (s.Length > 80) s = s.Substring(0, 80) + "…";
            return s;
        }
    }
}

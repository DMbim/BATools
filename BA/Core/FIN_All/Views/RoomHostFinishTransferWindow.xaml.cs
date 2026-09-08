using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using BA.Core.Rooms;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace BA.Commands.Rooms
{
    public partial class RoomHostFinishTransferWindow : Window
    {
        private readonly UIApplication _uiapp;
        private readonly ExternalEvent _exEvent;
        private readonly RoomHostFinishTransferHandler _handler;

        public ObservableCollection<RoomHostParamMapping> Mappings { get; } = new();

        // Full unfiltered room list, loaded live from the model.
        private List<RoomPickRow> _allRoomRows = new();

        // Selection tracked independently of ListBox.SelectedItems so it survives
        // ItemsSource being reassigned when the filter text changes.
        private readonly HashSet<ElementId> _selectedRoomIds = new();

        // Available Source Parameter names keyed by Source Category ("Ceiling"/"Floor"),
        // scanned live from the model. Each list is the flat union of instance parameter
        // names and type parameter names for that category, deduped by name. Since
        // RoomHostFinishTransferRunner already resolves instance first and only falls back
        // to type when the instance parameter is null/empty (ParameterUtil.ReadAsString
        // with allowTypeFallback: true), a flat deduped list matches actual runtime
        // resolution order with no separate disambiguation needed.
        private readonly Dictionary<string, List<string>> _sourceParamsByCategory =
            new(StringComparer.OrdinalIgnoreCase);

        // Available Target Room Parameter names, scanned live from the model.
        // Feeds ColTargetParam.ItemsSource directly.
        private List<string> _availableRoomParameters = new();

        private static readonly Dictionary<string, BuiltInCategory> SourceCategoryMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Ceiling"] = BuiltInCategory.OST_Ceilings,
                ["Floor"] = BuiltInCategory.OST_Floors
            };

        public RoomHostFinishTransferWindow(UIApplication uiapp, ExternalEvent exEvent, RoomHostFinishTransferHandler handler)
        {
            InitializeComponent();

            _uiapp = uiapp ?? throw new ArgumentNullException(nameof(uiapp));
            _exEvent = exEvent ?? throw new ArgumentNullException(nameof(exEvent));
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));

            Owner = System.Windows.Interop.HwndSource.FromHwnd(_uiapp.MainWindowHandle)?.RootVisual as Window;

            GridMappings.ItemsSource = Mappings;

            // Apply the dark combo style to every DataGridComboBoxColumn (Source Category
            // AND Target Room Parameter), not just the first one found.
            foreach (var comboCol in GridMappings.Columns.OfType<DataGridComboBoxColumn>())
            {
                comboCol.EditingElementStyle = (Style)Resources["BaDarkComboBox"];
                comboCol.ElementStyle = (Style)Resources["BaDarkComboBox"];
            }

            // Load on open (best effort)
            TryLoad();
            LoadRoomsFromModel();
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            Mappings.Add(new RoomHostParamMapping
            {
                SourceCategory = "Ceiling",
                SourceParameterName = "BA_Class_Name_EN",
                TargetRoomParameterName = "Ceiling Finish",
                WriteOnlyIfEmpty = true
            });
        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            var selected = GridMappings.SelectedItems.Cast<object>()
                .OfType<RoomHostParamMapping>()
                .ToList();

            foreach (var m in selected)
                Mappings.Remove(m);
        }

        private void BtnLoad_Click(object sender, RoutedEventArgs e) => TryLoad();

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RoomHostFinishTransferSettingsStore.Save(new RoomHostFinishTransferSettings
                {
                    Mappings = Mappings.ToList()
                });
                TxtStatus.Text = "Saved.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "BA", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedRoomIds.Count == 0)
            {
                TxtStatus.Text = "Select at least one room in the list before running.";
                return;
            }

            var settings = new RoomHostFinishTransferSettings { Mappings = Mappings.ToList() };
            var roomIds = _selectedRoomIds.ToList();

            TxtStatus.Text = "Running...";

            _handler.Raise(app =>
            {
                var doc = app.ActiveUIDocument?.Document;

                if (doc == null)
                    throw new InvalidOperationException("No active document.");

                var runner = new RoomHostFinishTransferRunner();
                var result = runner.Run(doc, settings, roomIds);

                // Back to UI thread
                Dispatcher.Invoke(() =>
                {
                    TxtStatus.Text = $"Done. Rooms processed: {result.RoomsProcessed}, writes: {result.ValuesWritten}, skipped: {result.Skipped}.";
                });

            }, "Room Host Finish Transfer");

            _exEvent.Raise();
        }

        private void BtnLoadRooms_Click(object sender, RoutedEventArgs e) => LoadRoomsFromModel();

        private void BtnSelectAllRooms_Click(object sender, RoutedEventArgs e)
        {
            foreach (var row in _allRoomRows)
                _selectedRoomIds.Add(row.RoomId);

            ApplyRoomFilter();
        }

        private void BtnSelectNoneRooms_Click(object sender, RoutedEventArgs e)
        {
            _selectedRoomIds.Clear();
            ApplyRoomFilter();
        }

        private void TxtRoomFilter_TextChanged(object sender, TextChangedEventArgs e) => ApplyRoomFilter();

        private void ListRooms_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            foreach (var item in e.RemovedItems.OfType<RoomPickRow>())
                _selectedRoomIds.Remove(item.RoomId);

            foreach (var item in e.AddedItems.OfType<RoomPickRow>())
                _selectedRoomIds.Add(item.RoomId);

            UpdateRoomCountText();
        }

        /// <summary>
        /// When a row's Source Category combo commits a new value, the row's Source
        /// Parameter selection almost certainly no longer belongs to the newly chosen
        /// category. Clear it and force the grid to reevaluate all cell bindings so the
        /// per row parameter combo (bound through CategoryToParametersConverter) picks up
        /// the new category's list immediately, without requiring the mapping class to
        /// raise property change notifications.
        /// </summary>
        private void GridMappings_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit)
                return;

            if (!ReferenceEquals(e.Column, ColSourceCategory))
                return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (e.Row.Item is RoomHostParamMapping m)
                    m.SourceParameterName = null;

                GridMappings.Items.Refresh();
            }), DispatcherPriority.Background);
        }

        /// <summary>
        /// Raises the ExternalEvent to collect all placed Rooms (Area > 0) from the active
        /// document, along with the available string parameter names on Ceiling, Floor,
        /// and Room elements for the mapping dropdowns, then marshals the result back onto
        /// the UI thread. Resets the current room selection, since room picks are not
        /// persisted across loads or sessions.
        /// </summary>
        private void LoadRoomsFromModel()
        {
            TxtStatus.Text = "Loading rooms and parameters...";

            _handler.Raise(app =>
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                    throw new InvalidOperationException("No active document.");

                var rows = new List<RoomPickRow>();

                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .OfType<Room>()
                    .Where(r => r.Area > 0);

                foreach (var r in rooms)
                {
                    string levelName = "";
                    try
                    {
                        var lvl = doc.GetElement(r.LevelId) as Level;
                        levelName = lvl?.Name ?? "";
                    }
                    catch
                    {
                        // level lookup failure shouldn't block the row from appearing
                    }

                    rows.Add(new RoomPickRow(r.Id, r.Number ?? "", r.Name ?? "", levelName, r.Area));
                }

                // Target: Room writable string parameters. Instance only, Room type
                // parameters aren't in scope here since this wasn't requested.
                var roomParamNames = CollectStringParameterNames(
                    doc, BuiltInCategory.OST_Rooms, writableOnly: true, includeTypeParameters: false);

                // Source: Ceiling and Floor string parameters, instance + type, flat and
                // deduped by name (instance wins at runtime via ParameterUtil.ReadAsString's
                // allowTypeFallback). Read only allowed since these are only ever read from.
                var ceilingParamNames = CollectStringParameterNames(
                    doc, BuiltInCategory.OST_Ceilings, writableOnly: false, includeTypeParameters: true);

                var floorParamNames = CollectStringParameterNames(
                    doc, BuiltInCategory.OST_Floors, writableOnly: false, includeTypeParameters: true);

                Dispatcher.Invoke(() =>
                {
                    _allRoomRows = rows
                        .OrderBy(x => x.Number, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    _selectedRoomIds.Clear();
                    ApplyRoomFilter();

                    _availableRoomParameters = roomParamNames;
                    if (ColTargetParam != null)
                        ColTargetParam.ItemsSource = _availableRoomParameters;

                    _sourceParamsByCategory["Ceiling"] = ceilingParamNames;
                    _sourceParamsByCategory["Floor"] = floorParamNames;

                    if (Resources["CatToSourceParamsConverter"] is BA.UI.Converters.CategoryToParametersConverter conv)
                        conv.Map = _sourceParamsByCategory;

                    GridMappings.Items.Refresh();

                    TxtStatus.Text =
                        $"Loaded {_allRoomRows.Count} rooms. Params found: " +
                        $"{ceilingParamNames.Count} Ceiling, {floorParamNames.Count} Floor, " +
                        $"{roomParamNames.Count} Room.";
                });

            }, "Load Rooms And Parameters");

            _exEvent.Raise();
        }

        /// <summary>
        /// Scans every non type instance of the given category and collects the distinct
        /// names of its string storage type parameters. When includeTypeParameters is true,
        /// also collects string parameter names from each instance's ElementType, caching
        /// resolved type ids so a type shared by many instances is only scanned once. Scans
        /// every instance rather than sampling one, since a single instance can miss
        /// conditionally present parameters. Names are deduped case insensitively across
        /// instance and type; RoomHostFinishTransferRunner already resolves instance before
        /// falling back to type, so a flat list correctly represents what will actually
        /// resolve at runtime.
        /// </summary>
        private static List<string> CollectStringParameterNames(
            Document doc, BuiltInCategory category, bool writableOnly, bool includeTypeParameters)
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenTypeIds = new HashSet<ElementId>();

            var elements = new FilteredElementCollector(doc)
                .OfCategory(category)
                .WhereElementIsNotElementType();

            foreach (var el in elements)
            {
                AddStringParamNames(el, names, writableOnly);

                if (!includeTypeParameters)
                    continue;

                var typeId = el.GetTypeId();
                if (typeId == ElementId.InvalidElementId || !seenTypeIds.Add(typeId))
                    continue;

                if (doc.GetElement(typeId) is Element typeEl)
                    AddStringParamNames(typeEl, names, writableOnly);
            }

            return names.ToList();
        }

        private static void AddStringParamNames(Element el, SortedSet<string> names, bool writableOnly)
        {
            foreach (var p in el.GetOrderedParameters())
            {
                if (p.StorageType != StorageType.String)
                    continue;

                if (p.Definition == null)
                    continue;

                if (writableOnly && p.IsReadOnly)
                    continue;

                names.Add(p.Definition.Name);
            }
        }

        /// <summary>
        /// Rebuilds the visible ListBox contents from the current filter text, then re-applies
        /// selection state from _selectedRoomIds for whichever rows remain visible. Handler is
        /// unhooked during the rebuild so re-selecting existing picks doesn't churn the HashSet.
        /// </summary>
        private void ApplyRoomFilter()
        {
            string filter = TxtRoomFilter?.Text?.Trim() ?? "";

            IEnumerable<RoomPickRow> filtered = _allRoomRows;

            if (!string.IsNullOrEmpty(filter))
            {
                filtered = _allRoomRows.Where(r =>
                    r.Number.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    r.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            var visible = filtered
                .OrderBy(r => r.Number, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ListRooms.SelectionChanged -= ListRooms_SelectionChanged;

            ListRooms.ItemsSource = visible;

            foreach (var row in visible)
            {
                if (_selectedRoomIds.Contains(row.RoomId))
                    ListRooms.SelectedItems.Add(row);
            }

            ListRooms.SelectionChanged += ListRooms_SelectionChanged;

            UpdateRoomCountText();
        }

        private void UpdateRoomCountText()
        {
            int visibleCount = (ListRooms.ItemsSource as List<RoomPickRow>)?.Count ?? 0;
            TxtRoomCount.Text = $"{visibleCount} shown / {_allRoomRows.Count} total, {_selectedRoomIds.Count} selected";
        }

        private void TryLoad()
        {
            try
            {
                var s = RoomHostFinishTransferSettingsStore.Load();

                Mappings.Clear();
                foreach (var m in s.Mappings ?? Enumerable.Empty<RoomHostParamMapping>())
                    Mappings.Add(m);

                if (Mappings.Count == 0)
                {
                    // Your BA defaults
                    Mappings.Add(new RoomHostParamMapping
                    {
                        SourceCategory = "Ceiling",
                        SourceParameterName = "BA_Class_Name_EN",
                        TargetRoomParameterName = "Ceiling Finish",
                        WriteOnlyIfEmpty = true
                    });

                    Mappings.Add(new RoomHostParamMapping
                    {
                        SourceCategory = "Floor",
                        SourceParameterName = "BA_Class_Name_EN",
                        TargetRoomParameterName = "Floor Finish",
                        WriteOnlyIfEmpty = true
                    });
                }

                TxtStatus.Text = "Loaded.";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "Load failed (using defaults).";
                // optional: MessageBox.Show(ex.ToString(), "BA", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
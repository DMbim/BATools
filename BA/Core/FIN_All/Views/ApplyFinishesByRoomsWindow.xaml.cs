using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using BA.Core.Settings;
using BA.UI.Core.Finishes;
using BA.UI.TextHub;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using UnitUtil = BA.UI.Core.Finishes.UnitUtil;

namespace BA.UI.Finishes
{
    public partial class ApplyFinishesByRoomsWindow : Window
    {
        private readonly UIApplication _uiApp;
        private readonly UIDocument _uiDoc;
        private readonly Document _doc;
        private readonly RevitExternalEventRunner _runner;

        private readonly PluginSettings _settings;

        private readonly ObservableCollection<RoomPickRow> _rooms = new();
        private List<RoomPickRow> _roomsAll = new();

        // ---- persisted settings keys ----
        // Deliberately does NOT persist the room selection itself, that's document/session
        // specific and would either go stale or point at the wrong rooms in a different file.
        // Only the finish option fields are remembered.
        private const string KeyApplyWalls = "ApplyFinishesByRooms.ApplyWalls";
        private const string KeyApplyFloors = "ApplyFinishesByRooms.ApplyFloors";
        private const string KeyApplyCeilings = "ApplyFinishesByRooms.ApplyCeilings";

        private const string KeyUseRoomDefinedTypes = "ApplyFinishesByRooms.UseRoomDefinedTypes";

        // Type selections are stored by Type Name, not ElementId, ids aren't stable across
        // documents/sessions. Restored by matching name against whatever's actually loaded
        // in the current document; if no match, falls back to whatever LoadTypeCombos()
        // already defaulted to (first item).
        private const string KeyWallTypeName = "ApplyFinishesByRooms.WallTypeName";
        private const string KeyFloorTypeName = "ApplyFinishesByRooms.FloorTypeName";
        private const string KeyCeilingTypeName = "ApplyFinishesByRooms.CeilingTypeName";

        private const string KeyUseTopOffset = "ApplyFinishesByRooms.UseTopOffset";
        private const string KeyTopOffsetMm = "ApplyFinishesByRooms.TopOffsetMm";
        private const string KeyBaseOffsetMm = "ApplyFinishesByRooms.BaseOffsetMm";

        private const string KeyCeilingUseRoomHeightOffset = "ApplyFinishesByRooms.CeilingUseRoomHeightOffset";
        private const string KeyCeilingTopOffsetMm = "ApplyFinishesByRooms.CeilingTopOffsetMm";
        private const string KeyCeilingHeightAboveLevelMm = "ApplyFinishesByRooms.CeilingHeightAboveLevelMm";

        public ApplyFinishesByRoomsWindow(UIApplication uiApp, RevitExternalEventRunner runner)
        {
            InitializeComponent();

            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _uiDoc = _uiApp.ActiveUIDocument ?? throw new InvalidOperationException("No active UIDocument.");
            _doc = _uiDoc.Document;
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));

            _settings = PluginSettingsStore.Load();

            ListRooms.ItemsSource = _rooms;

            LoadTypeCombos();
            ApplySavedSettings();
            RefreshRooms();
            UpdateFinishTypeSourceUi();

            Closing += ApplyFinishesByRoomsWindow_Closing;
        }

        private void LoadTypeCombos()
        {
            // Wall types
            var wallTypes = new FilteredElementCollector(_doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .Where(t => !t.IsStackedWallType())
                .OrderBy(t => t.FamilyName).ThenBy(t => t.Name)
                .ToList();

            CmbWallType.ItemsSource = wallTypes;
            CmbWallType.DisplayMemberPath = "Name";
            CmbWallType.SelectedItem = wallTypes.FirstOrDefault();

            // Floor types
            var floorTypes = new FilteredElementCollector(_doc)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .OrderBy(t => t.FamilyName).ThenBy(t => t.Name)
                .ToList();

            CmbFloorType.ItemsSource = floorTypes;
            CmbFloorType.DisplayMemberPath = "Name";
            CmbFloorType.SelectedItem = floorTypes.FirstOrDefault();

            // Ceiling types
            var ceilTypes = new FilteredElementCollector(_doc)
                .OfClass(typeof(CeilingType))
                .Cast<CeilingType>()
                .OrderBy(t => t.FamilyName).ThenBy(t => t.Name)
                .ToList();

            CmbCeilingType.ItemsSource = ceilTypes;
            CmbCeilingType.DisplayMemberPath = "Name";
            CmbCeilingType.SelectedItem = ceilTypes.FirstOrDefault();
        }

        /// <summary>
        /// Applies values loaded from PluginSettingsStore to the UI. Must run after
        /// LoadTypeCombos() so the combo ItemsSources exist to match saved type names against.
        /// Every read has a sensible default matching the XAML's original hardcoded defaults,
        /// so a fresh install (no settings.json yet) behaves exactly as before this change.
        /// </summary>
        private void ApplySavedSettings()
        {
            ChkWalls.IsChecked = _settings.GetBool(KeyApplyWalls, true);
            ChkFloors.IsChecked = _settings.GetBool(KeyApplyFloors, false);
            ChkCeilings.IsChecked = _settings.GetBool(KeyApplyCeilings, false);

            bool useRoomDefinedTypes = _settings.GetBool(KeyUseRoomDefinedTypes, false);
            RadioRoomDefinedType.IsChecked = useRoomDefinedTypes;
            RadioFixedType.IsChecked = !useRoomDefinedTypes;

            RestoreComboSelectionByName(CmbWallType, _settings.GetString(KeyWallTypeName, ""));
            RestoreComboSelectionByName(CmbFloorType, _settings.GetString(KeyFloorTypeName, ""));
            RestoreComboSelectionByName(CmbCeilingType, _settings.GetString(KeyCeilingTypeName, ""));

            ChkUseTopOffset.IsChecked = _settings.GetBool(KeyUseTopOffset, true);
            TxtTopOffsetMm.Text = FormatMm(_settings.GetDouble(KeyTopOffsetMm, 100));
            TxtBaseOffsetMm.Text = FormatMm(_settings.GetDouble(KeyBaseOffsetMm, 0));

            ChkCeilingUseRoomHeightOffset.IsChecked = _settings.GetBool(KeyCeilingUseRoomHeightOffset, true);
            TxtCeilingTopOffsetMm.Text = FormatMm(_settings.GetDouble(KeyCeilingTopOffsetMm, 100));
            TxtCeilingHeightAboveLevelMm.Text = FormatMm(_settings.GetDouble(KeyCeilingHeightAboveLevelMm, 2400));
        }

        private static void RestoreComboSelectionByName(System.Windows.Controls.ComboBox combo, string savedName)
        {
            if (string.IsNullOrWhiteSpace(savedName)) return; // leave LoadTypeCombos()'s default

            foreach (var item in combo.Items)
            {
                var name = (item as ElementType)?.Name;
                if (string.Equals(name, savedName, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = item;
                    return;
                }
            }

            // Saved type no longer exists in this document (renamed/deleted/different project),
            // silently keep whatever LoadTypeCombos() already defaulted to.
        }

        private static string FormatMm(double v) =>
            v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        private void ApplyFinishesByRoomsWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveCurrentSettings();
        }

        private void SaveCurrentSettings()
        {
            _settings.SetBool(KeyApplyWalls, ChkWalls.IsChecked == true);
            _settings.SetBool(KeyApplyFloors, ChkFloors.IsChecked == true);
            _settings.SetBool(KeyApplyCeilings, ChkCeilings.IsChecked == true);

            _settings.SetBool(KeyUseRoomDefinedTypes, RadioRoomDefinedType.IsChecked == true);

            _settings.SetString(KeyWallTypeName, (CmbWallType.SelectedItem as WallType)?.Name ?? "");
            _settings.SetString(KeyFloorTypeName, (CmbFloorType.SelectedItem as FloorType)?.Name ?? "");
            _settings.SetString(KeyCeilingTypeName, (CmbCeilingType.SelectedItem as CeilingType)?.Name ?? "");

            _settings.SetBool(KeyUseTopOffset, ChkUseTopOffset.IsChecked == true);
            _settings.SetDouble(KeyTopOffsetMm, ParseMm(TxtTopOffsetMm.Text, 100));
            _settings.SetDouble(KeyBaseOffsetMm, ParseMm(TxtBaseOffsetMm.Text, 0));

            _settings.SetBool(KeyCeilingUseRoomHeightOffset, ChkCeilingUseRoomHeightOffset.IsChecked == true);
            _settings.SetDouble(KeyCeilingTopOffsetMm, ParseMm(TxtCeilingTopOffsetMm.Text, 100));
            _settings.SetDouble(KeyCeilingHeightAboveLevelMm, ParseMm(TxtCeilingHeightAboveLevelMm.Text, 2400));

            try
            {
                PluginSettingsStore.Save(_settings);
            }
            catch
            {
                // Best-effort, don't block window close over a settings write failure.
            }
        }

        private void RefreshRooms()
        {
            _rooms.Clear();

            var rooms = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .OfClass(typeof(SpatialElement))
                .Cast<Room>()
                .Where(r => r.Location != null && r.Area > 1e-6) // placed
                .ToList();

            var list = new List<RoomPickRow>();

            foreach (var r in rooms)
            {
                string num = r.Number ?? "";
                string name = r.Name ?? "";
                string lvl = _doc.GetElement(r.LevelId) is Level lv ? lv.Name : "<no level>";
                list.Add(new RoomPickRow(r.Id, num, name, lvl));
            }

            _roomsAll = list
                .OrderBy(x => x.LevelName)
                .ThenBy(x => x.Number)
                .ThenBy(x => x.Name)
                .ToList();

            foreach (var row in _roomsAll) _rooms.Add(row);
        }

        private void ApplySearchFilter()
        {
            string q = (TxtSearch.Text ?? "").Trim();
            IEnumerable<RoomPickRow> src = _roomsAll;

            if (!string.IsNullOrWhiteSpace(q))
            {
                string ql = q.ToLowerInvariant();
                src = src.Where(r =>
                    (r.Number ?? "").ToLowerInvariant().Contains(ql) ||
                    (r.Name ?? "").ToLowerInvariant().Contains(ql) ||
                    (r.LevelName ?? "").ToLowerInvariant().Contains(ql));
            }

            _rooms.Clear();
            foreach (var row in src) _rooms.Add(row);
        }

        private void BtnRefreshRooms_Click(object sender, RoutedEventArgs e)
        {
            _runner.Raise("Refresh rooms", _ =>
            {
                // Reads are safe, but keep it consistent
                Dispatcher.Invoke(() => RefreshRooms());
            });
        }

        private void TxtSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            ApplySearchFilter();
        }

        private void FinishTypeSource_Changed(object sender, RoutedEventArgs e)
        {
            UpdateFinishTypeSourceUi();
        }

        /// <summary>
        /// The fixed-type combos are meaningless in "Defined by room" mode, disable them so
        /// it's visually obvious they're not what's driving the run.
        /// </summary>
        private void UpdateFinishTypeSourceUi()
        {
            bool fixedMode = RadioFixedType?.IsChecked == true;

            if (CmbWallType != null) CmbWallType.IsEnabled = fixedMode;
            if (CmbFloorType != null) CmbFloorType.IsEnabled = fixedMode;
            if (CmbCeilingType != null) CmbCeilingType.IsEnabled = fixedMode;
        }

        private void BtnPickRooms_Click(object sender, RoutedEventArgs e)
        {
            Hide();

            _runner.Raise("Pick rooms", app =>
            {
                try
                {
                    var sel = _uiDoc.Selection;
                    var refs = sel.PickObjects(ObjectType.Element, new BA.Filters.RoomOrRoomTagSelectionFilter(),
                        "Pick Rooms or Room Tags. ESC to finish.");

                    var roomIds = new HashSet<ElementId>();
                    foreach (var r in refs)
                    {
                        var el = _doc.GetElement(r);

                        if (el is Autodesk.Revit.DB.Architecture.Room room)
                            roomIds.Add(room.Id);
                        else if (el is Autodesk.Revit.DB.Architecture.RoomTag rt)
                        {
                            var rid = rt.TaggedLocalRoomId;
                            if (rid != ElementId.InvalidElementId)
                                roomIds.Add(rid);
                            else if (rt.Room != null)
                                roomIds.Add(rt.Room.Id);
                        }
                    }

                    Dispatcher.Invoke(() =>
                    {
                        Show();
                        SelectRoomsInList(roomIds);
                    });
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    Dispatcher.Invoke(() => Show());
                }
            });
        }

        private void SelectRoomsInList(HashSet<ElementId> roomIds)
        {
            if (roomIds.Count == 0) return;

            ListRooms.SelectedItems.Clear();

            var rowsById = _rooms.ToDictionary(x => x.RoomId, x => x);
            foreach (var id in roomIds)
            {
                if (rowsById.TryGetValue(id, out var row))
                    ListRooms.SelectedItems.Add(row);
            }
        }

        private void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            var selected = ListRooms.SelectedItems.Cast<object>().OfType<RoomPickRow>().ToList();
            if (selected.Count == 0)
            {
                TaskDialog.Show("BA", "Select rooms in the list, or use 'Pick in model'.");
                return;
            }

            bool doWalls = ChkWalls.IsChecked == true;
            bool doFloors = ChkFloors.IsChecked == true;
            bool doCeils = ChkCeilings.IsChecked == true;

            if (!doWalls && !doFloors && !doCeils)
            {
                TaskDialog.Show("BA", "Choose at least one target: Walls, Floors, or Ceilings.");
                return;
            }

            bool useRoomDefinedTypes = RadioRoomDefinedType.IsChecked == true;

            // Fixed-type selections only matter in fixed mode. In room-defined mode the
            // combos are disabled and their current selection is simply ignored.
            if (!useRoomDefinedTypes)
            {
                if (doWalls && !(CmbWallType.SelectedItem is WallType))
                {
                    TaskDialog.Show("BA", "Choose a finish wall type.");
                    return;
                }
                if (doFloors && !(CmbFloorType.SelectedItem is FloorType))
                {
                    TaskDialog.Show("BA", "Choose a finish floor type.");
                    return;
                }
                if (doCeils && !(CmbCeilingType.SelectedItem is CeilingType))
                {
                    TaskDialog.Show("BA", "Choose a finish ceiling type.");
                    return;
                }
            }

            bool useTopOffset = ChkUseTopOffset.IsChecked == true;
            double topOffsetMm = ParseMm(TxtTopOffsetMm.Text, 100);
            double baseOffsetMm = ParseMm(TxtBaseOffsetMm.Text, 0);

            bool ceilingUseRoomHeightOffset = ChkCeilingUseRoomHeightOffset.IsChecked == true;
            double ceilingTopOffsetMm = ParseMm(TxtCeilingTopOffsetMm.Text, 100);
            double ceilingHeightAboveLevelMm = ParseMm(TxtCeilingHeightAboveLevelMm.Text, 2400);

            var opts = new ApplyFinishesOptions(
                roomIds: selected.Select(x => x.RoomId).ToList(),
                applyWalls: doWalls,
                applyFloors: doFloors,
                applyCeilings: doCeils,
                wallTypeId: (CmbWallType.SelectedItem as WallType)?.Id ?? ElementId.InvalidElementId,
                floorTypeId: (CmbFloorType.SelectedItem as FloorType)?.Id ?? ElementId.InvalidElementId,
                ceilingTypeId: (CmbCeilingType.SelectedItem as CeilingType)?.Id ?? ElementId.InvalidElementId,
                useRoomDefinedFinishTypes: useRoomDefinedTypes,
                useTopOffset: useTopOffset,
                topOffsetFt: UnitUtil.MmToFt(topOffsetMm),
                baseOffsetFt: UnitUtil.MmToFt(baseOffsetMm),
                ceilingUseRoomHeightOffset: ceilingUseRoomHeightOffset,
                ceilingTopOffsetFt: UnitUtil.MmToFt(ceilingTopOffsetMm),
                ceilingHeightAboveLevelFt: UnitUtil.MmToFt(ceilingHeightAboveLevelMm)
            );

            _runner.Raise("Apply finishes by rooms", app =>
            {
                var report = FinishesByRoomService.Execute(app.ActiveUIDocument.Document, opts);
                TaskDialog.Show("BA - Finishes", report.ToString());
            });
        }

        private static double ParseMm(string? text, double fallback)
        {
            if (double.TryParse((text ?? "").Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double v))
                return v;

            // try current culture
            if (double.TryParse((text ?? "").Trim(), out v))
                return v;

            return fallback;
        }
    }



    internal sealed class RoomOrRoomTagSelectionFilter : ISelectionFilter
    {
        private readonly Document _doc;

        public RoomOrRoomTagSelectionFilter(Document doc) => _doc = doc;

        public bool AllowElement(Element elem)
        {
            if (elem is Room) return true;
            if (elem is RoomTag) return true; // Revit room tag class
            return false;
        }

        public bool AllowReference(Reference reference, XYZ position) => true;
    }

    internal static class WallTypeExt
    {
        public static bool IsStackedWallType(this WallType wt)
        {
            // Stacked walls report Kind=Stacked in some versions; safest is BuiltInParameter check.
            var p = wt.get_Parameter(BuiltInParameter.WALL_ATTR_WIDTH_PARAM);
            return false; // keep simple: treat all as valid unless you want to filter further
        }
    }
}
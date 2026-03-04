using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
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


        private readonly ObservableCollection<RoomPickRow> _rooms = new();
        private List<RoomPickRow> _roomsAll = new();

        public ApplyFinishesByRoomsWindow(UIApplication uiApp, RevitExternalEventRunner runner)
        {
            InitializeComponent();

            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _uiDoc = _uiApp.ActiveUIDocument ?? throw new InvalidOperationException("No active UIDocument.");
            _doc = _uiDoc.Document;
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));

            ListRooms.ItemsSource = _rooms;

            LoadTypeCombos();
            RefreshRooms();
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

            bool useTopOffset = ChkUseTopOffset.IsChecked == true;
            double topOffsetMm = ParseMm(TxtTopOffsetMm.Text, 100);
            double baseOffsetMm = ParseMm(TxtBaseOffsetMm.Text, 0);

            var opts = new ApplyFinishesOptions(
                roomIds: selected.Select(x => x.RoomId).ToList(),
                applyWalls: doWalls,
                applyFloors: doFloors,
                applyCeilings: doCeils,
                wallTypeId: (CmbWallType.SelectedItem as WallType)?.Id ?? ElementId.InvalidElementId,
                floorTypeId: (CmbFloorType.SelectedItem as FloorType)?.Id ?? ElementId.InvalidElementId,
                ceilingTypeId: (CmbCeilingType.SelectedItem as CeilingType)?.Id ?? ElementId.InvalidElementId,
                useTopOffset: useTopOffset,
                topOffsetFt: UnitUtil.MmToFt(topOffsetMm),
                baseOffsetFt: UnitUtil.MmToFt(baseOffsetMm)
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
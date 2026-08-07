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

        public RoomHostFinishTransferWindow(UIApplication uiapp, ExternalEvent exEvent, RoomHostFinishTransferHandler handler)
        {
            InitializeComponent();

            _uiapp = uiapp ?? throw new ArgumentNullException(nameof(uiapp));
            _exEvent = exEvent ?? throw new ArgumentNullException(nameof(exEvent));
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));

            Owner = System.Windows.Interop.HwndSource.FromHwnd(_uiapp.MainWindowHandle)?.RootVisual as Window;

            GridMappings.ItemsSource = Mappings;
            var col = GridMappings.Columns
            .OfType<DataGridComboBoxColumn>()
            .FirstOrDefault();

            if (col != null)
            {
                col.EditingElementStyle = (Style)Resources["BaDarkComboBox"];
                col.ElementStyle = (Style)Resources["BaDarkComboBox"];
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
        /// Raises the ExternalEvent to collect all placed Rooms (Area > 0) from the active
        /// document, then marshals the result back onto the UI thread. Resets the current
        /// selection, since room picks are not persisted across loads or sessions.
        /// </summary>
        private void LoadRoomsFromModel()
        {
            TxtStatus.Text = "Loading rooms...";

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

                Dispatcher.Invoke(() =>
                {
                    _allRoomRows = rows
                        .OrderBy(x => x.Number, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    _selectedRoomIds.Clear();
                    ApplyRoomFilter();
                    TxtStatus.Text = $"Loaded {_allRoomRows.Count} rooms.";
                });

            }, "Load Rooms");

            _exEvent.Raise();
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
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Core.Rooms;
using System;
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
                col.EditingElementStyle = (Style)Resources["DarkComboBoxStyle"];
                col.ElementStyle = (Style)Resources["DarkComboBoxStyle"];
            }

            // Load on open (best effort)
            TryLoad();
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
            var settings = new RoomHostFinishTransferSettings { Mappings = Mappings.ToList() };
            bool selectedOnly = (ChkSelectedOnly.IsChecked == true);

            TxtStatus.Text = "Running...";

            _handler.Raise(app =>
            {
                var doc = app.ActiveUIDocument?.Document;
                var uidoc = app.ActiveUIDocument;

                if (doc == null || uidoc == null)
                    throw new InvalidOperationException("No active document.");

                var runner = new RoomHostFinishTransferRunner();
                var result = runner.Run(uidoc, settings, selectedOnly);

                // Back to UI thread
                Dispatcher.Invoke(() =>
                {
                    TxtStatus.Text = $"Done. Rooms processed: {result.RoomsProcessed}, writes: {result.ValuesWritten}, skipped: {result.Skipped}.";
                });

            }, "Room Host Finish Transfer");

            _exEvent.Raise();
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
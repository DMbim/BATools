using Autodesk.Revit.DB;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace BA.UI.KeyplanGrid
{
    public partial class KeyplanGridWindow : Window
    {
        private readonly Document _doc;
        private readonly KeyplanGridViewModel _vm;

        // -------------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------------

        public KeyplanGridWindow(Document doc, KeyplanGridViewModel vm)
        {
            InitializeComponent();

            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));

            DataContext = _vm;

            Loaded += KeyplanGridWindow_Loaded;
            SizeChanged += KeyplanGridWindow_SizeChanged;

            PreviewCanvas.AxisDragged += PreviewCanvas_AxisDragged;
            PreviewCanvas.AxisClicked += PreviewCanvas_AxisClicked;
            PreviewCanvas.CellPolygonClicked += PreviewCanvas_CellPolygonClicked;

            // Auto-render whenever the ViewModel rebuilds PreviewData.
            _vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(KeyplanGridViewModel.PreviewData))
                    RenderPreview();
            };
        }

        // -------------------------------------------------------------------------
        // Window lifecycle
        // -------------------------------------------------------------------------

        private void KeyplanGridWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshPreview();
        }

        private void KeyplanGridWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RefreshPreview();
        }

        // -------------------------------------------------------------------------
        // Canvas event handlers
        // -------------------------------------------------------------------------

        private void PreviewCanvas_AxisClicked(object sender, PreviewAxisClickEventArgs e)
        {
            bool additive = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            _vm.SelectSplit(e.SplitId, e.Orientation, additive);
            // RenderPreview() will be triggered automatically via PropertyChanged on PreviewData.
        }

        private void PreviewCanvas_AxisDragged(object sender, PreviewAxisEventArgs e)
        {
            _vm.MoveSplit(e.SplitId, e.Orientation, e.Normalized);
        }

        private void PreviewCanvas_CellPolygonClicked(object sender, PreviewCellClickEventArgs e)
        {
            // Route to zone pick session if one is active.
            if (_vm.IsZoneSessionActive)
            {
                _vm.HandleZoneRegionPick(e.CellKey);
                // RenderPreview triggered by PropertyChanged.
                return;
            }

            bool additive = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            _vm.ToggleCellSelection(e.CellKey, additive);
        }

        // -------------------------------------------------------------------------
        // Split interaction buttons
        // -------------------------------------------------------------------------

        private void BtnRefreshPreview_Click(object sender, RoutedEventArgs e)
        {
            RefreshPreview();
        }

        private void BtnAddVerticalSplit_Click(object sender, RoutedEventArgs e)
        {
            _vm.AddVerticalSplit();
        }

        private void BtnAddHorizontalSplit_Click(object sender, RoutedEventArgs e)
        {
            _vm.AddHorizontalSplit();
        }

        private void BtnRemoveVerticalSplit_Click(object sender, RoutedEventArgs e)
        {
            _vm.RemoveSelectedVerticalSplits();
        }

        private void BtnRemoveHorizontalSplit_Click(object sender, RoutedEventArgs e)
        {
            _vm.RemoveSelectedHorizontalSplits();
        }

        private void BtnDeleteSplit_Click(object sender, RoutedEventArgs e)
        {
            _vm.DeleteSelectedSplit();
        }

        private void BtnNudgeLeft_Click(object sender, RoutedEventArgs e)
        {
            _vm.NudgeSelectedSplit(-0.01);
        }

        private void BtnNudgeRight_Click(object sender, RoutedEventArgs e)
        {
            _vm.NudgeSelectedSplit(0.01);
        }

        // -------------------------------------------------------------------------
        // Scale preset buttons
        // -------------------------------------------------------------------------

        private void BtnPresetScale300_Click(object sender, RoutedEventArgs e)
        {
            _vm.GlobalScaleFactor = 1.0 / 300.0;
        }

        private void BtnPresetScale100_Click(object sender, RoutedEventArgs e)
        {
            _vm.GlobalScaleFactor = 1.0 / 100.0;
        }

        private void BtnPresetScale1_Click(object sender, RoutedEventArgs e)
        {
            _vm.GlobalScaleFactor = 1.0;
        }

        // -------------------------------------------------------------------------
        // Cell edit buttons
        // -------------------------------------------------------------------------

        private void BtnExcludeSelected_Click(object sender, RoutedEventArgs e)
        {
            _vm.ExcludeSelectedCells();
        }

        private void BtnIncludeSelected_Click(object sender, RoutedEventArgs e)
        {
            _vm.IncludeSelectedCells();
        }

        private void BtnIncludeAll_Click(object sender, RoutedEventArgs e)
        {
            _vm.IncludeAllCells();
        }

        private void BtnClearSelection_Click(object sender, RoutedEventArgs e)
        {
            _vm.ClearSelection();
        }

        // -------------------------------------------------------------------------
        // Zone label session buttons
        // -------------------------------------------------------------------------

        /// <summary>
        /// Begins a zone label pick session.
        /// Requires that at least one generation has been performed in this window session.
        /// </summary>
        private void BtnBeginZoneLabels_Click(object sender, RoutedEventArgs e)
        {
            if (!_vm.BeginZoneLabelSession(out string error))
            {
                MessageBox.Show(error, "Zone Labels", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            PreviewCanvas.ZoneLabelPickModeActive = true;
            UpdateZoneButtonVisibility();
            RenderPreview();
        }

        /// <summary>
        /// Commits all pending zone assignments to Revit elements.
        /// Only enabled when the session is in Ready state.
        /// </summary>
        private void BtnCommitZoneLabels_Click(object sender, RoutedEventArgs e)
        {
            if (!_vm.IsZoneSessionActive) return;

            try
            {
                ZoneWriteResult result = _vm.CommitZoneLabels(_doc);

                PreviewCanvas.ZoneLabelPickModeActive = false;
                UpdateZoneButtonVisibility();
                RenderPreview();

                string message = result.Summary;

                if (result.HasWarnings)
                {
                    int take = Math.Min(10, result.MissingParameters.Count +
                                            result.ReadOnlyParameters.Count +
                                            result.Errors.Count);

                    message += "\n\nWarnings / errors:";

                    foreach (string w in result.MissingParameters.Take(take))
                        message += "\n• " + w;

                    foreach (string w in result.ReadOnlyParameters.Take(take))
                        message += "\n• " + w;

                    foreach (string w in result.Errors.Take(take))
                        message += "\n• " + w;
                }

                MessageBox.Show(message, "Zone Labels",
                    MessageBoxButton.OK,
                    result.HasWarnings ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Zone Label Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Cancels the active zone label session without writing anything.
        /// </summary>
        private void BtnCancelZoneLabels_Click(object sender, RoutedEventArgs e)
        {
            _vm.CancelZoneLabelSession();
            PreviewCanvas.ZoneLabelPickModeActive = false;
            UpdateZoneButtonVisibility();
            RenderPreview();
        }

        // -------------------------------------------------------------------------
        // Generate
        // -------------------------------------------------------------------------

        private void BtnGenerate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CurveLoop outerLoop = _vm.GetSourceOuterLoop();
                if (outerLoop == null)
                {
                    MessageBox.Show("No source outer loop found.", "Keyplan Grid",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                KeyplanGridOptions options = _vm.BuildOptions();

                GenerationResult result = KeyplanRegionGenerationService.Generate(
                    _doc,
                    outerLoop,
                    options,
                    _vm.GetVerticalSplitSnapshot(),
                    _vm.GetHorizontalSplitSnapshot(),
                    _vm.GetCellEditsSnapshot());

                // Store result on the ViewModel so the zone label session can use it.
                _vm.SetLastGenerationResult(result);

                _vm.StatusText =
                    $"Generated. Scale: {options.GlobalScaleFactor:0.############}, " +
                    $"Polygons: {result.TotalPolygonsFromBuilder}, " +
                    $"Regions: {result.CreatedFilledRegions}, " +
                    $"Grid lines: {result.CreatedGridLines}, " +
                    $"Outline: {result.CreatedOutlineCurves}, " +
                    $"Skipped: {result.Skipped}.";

                string rejectPreview = string.Empty;
                if (result.RegionRejectReasons.Count > 0)
                {
                    int take = Math.Min(12, result.RegionRejectReasons.Count);
                    rejectPreview = "\n\nFirst reject reasons:\n• " +
                        string.Join("\n• ", result.RegionRejectReasons.Take(take));
                }

                MessageBox.Show(
                    $"Keyplan generated into view:\n{result.TargetViewName}\n\n" +
                    $"Scale factor:   {options.GlobalScaleFactor:0.############}\n" +
                    $"Builder polys:  {result.TotalPolygonsFromBuilder}\n" +
                    $"Filled regions: {result.CreatedFilledRegions}\n" +
                    $"Grid lines:     {result.CreatedGridLines}\n" +
                    $"Outline curves: {result.CreatedOutlineCurves}\n" +
                    $"Skipped:        {result.Skipped}" +
                    rejectPreview,
                    "Keyplan Grid",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // Update zone button availability now that a result exists.
                UpdateZoneButtonVisibility();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Generation Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // -------------------------------------------------------------------------
        // Preview helpers
        // -------------------------------------------------------------------------

        private void RefreshPreview()
        {
            double width = Math.Max(300.0, PreviewCanvas.ActualWidth);
            double height = Math.Max(300.0, PreviewCanvas.ActualHeight);

            _vm.RebuildPreview(width, height, 24.0);
            // RenderPreview() is called automatically via PropertyChanged.
        }

        private void RenderPreview()
        {
            PreviewCanvas.RenderPreview(_vm.PreviewData);
        }

        // -------------------------------------------------------------------------
        // Zone button visibility helper
        // Assumes the XAML contains:
        //   BtnBeginZoneLabels  — visible when session is NOT active
        //   BtnCommitZoneLabels — visible only when session is active AND Ready
        //   BtnCancelZoneLabels — visible when session is active
        // -------------------------------------------------------------------------

        private void UpdateZoneButtonVisibility()
        {
            bool active = _vm.IsZoneSessionActive;
            bool ready = active && _vm.ActiveZoneSession?.State == ZonePickState.Ready;
            bool hasResult = _vm.LastGenerationResult != null &&
                             _vm.LastGenerationResult.CreatedFilledRegions > 0;

            // These controls must exist in the XAML with these exact x:Name values.
            if (BtnBeginZoneLabels != null) BtnBeginZoneLabels.IsEnabled = !active && hasResult;
            if (BtnCommitZoneLabels != null) BtnCommitZoneLabels.IsEnabled = ready;
            if (BtnCancelZoneLabels != null) BtnCancelZoneLabels.IsEnabled = active;
        }
    }
}

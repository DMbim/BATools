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

        public KeyplanGridWindow(Document doc, KeyplanGridViewModel vm)
        {
            InitializeComponent();

            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));

            DataContext = _vm;

            Loaded += KeyplanGridWindow_Loaded;
            SizeChanged += KeyplanGridWindow_SizeChanged;
            PreviewCanvas.AxisDragged += PreviewCanvas_AxisDragged;

            // You need to add this event to the canvas control; see section 8 below.
            PreviewCanvas.CellPolygonClicked += PreviewCanvas_CellPolygonClicked;
        }

        private void KeyplanGridWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshPreview();
        }

        private void KeyplanGridWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RefreshPreview();
        }

        private void PreviewCanvas_AxisDragged(AxisOrientation orientation, int interiorIndex, double normalized)
        {
            _vm.MoveAxis(orientation, interiorIndex, normalized);
            RenderPreview();
        }

        private void PreviewCanvas_CellPolygonClicked(object sender, PreviewCellClickEventArgs e)
        {
            bool additive = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            _vm.ToggleCellSelection(e.CellKey, additive);
            RenderPreview();
        }

        private void BtnRefreshPreview_Click(object sender, RoutedEventArgs e)
        {
            RefreshPreview();
        }

        private void BtnGenerate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CurveLoop outerLoop = _vm.GetSourceOuterLoop();
                if (outerLoop == null)
                {
                    MessageBox.Show("No source outer loop found.", "Keyplan Grid", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                KeyplanGridOptions options = _vm.BuildOptions();
                double[] xBreaks = _vm.GetXNormalizedBreaks();
                double[] yBreaks = _vm.GetYNormalizedBreaks();

                GenerationResult result = KeyplanRegionGenerationService.Generate(
                    _doc,
                    outerLoop,
                    options,
                    xBreaks,
                    yBreaks,
                    _vm.GetCellEditsSnapshot());

                _vm.StatusText =
                    $"Generated. Fill mode: {options.FillMode}, " +
                    $"Cells: {result.TotalCellsFromBuilder}, Polygons: {result.TotalPolygonsFromBuilder}, " +
                    $"Regions: {result.CreatedFilledRegions}, Grid lines: {result.CreatedGridLines}, " +
                    $"Outline curves: {result.CreatedOutlineCurves}, Skipped: {result.Skipped}.";

                string rejectPreview = "";
                if (result.RegionRejectReasons.Count > 0)
                {
                    int take = Math.Min(12, result.RegionRejectReasons.Count);
                    rejectPreview = "\n\nFirst reject reasons:\n- " +
                                    string.Join("\n- ", result.RegionRejectReasons.Take(take));
                }

                MessageBox.Show(
                    $"Keyplan generated into view:\n{result.TargetViewName}\n\n" +
                    $"Builder cells: {result.TotalCellsFromBuilder}\n" +
                    $"Builder polygons: {result.TotalPolygonsFromBuilder}\n" +
                    $"Filled regions: {result.CreatedFilledRegions}\n" +
                    $"Grid lines: {result.CreatedGridLines}\n" +
                    $"Outline curves: {result.CreatedOutlineCurves}\n" +
                    $"Skipped: {result.Skipped}" +
                    rejectPreview,
                    "Keyplan Grid",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Generation Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void RefreshPreview()
        {
            double width = Math.Max(300.0, PreviewCanvas.ActualWidth);
            double height = Math.Max(300.0, PreviewCanvas.ActualHeight);

            _vm.RebuildPreview(width, height, 24.0);
            RenderPreview();
        }

        private void RenderPreview()
        {
            PreviewCanvas.RenderPreview(_vm.PreviewData);
        }

        private void BtnExcludeSelected_Click(object sender, RoutedEventArgs e)
        {
            _vm.ExcludeSelectedCells();
            RenderPreview();
        }

        private void BtnIncludeSelected_Click(object sender, RoutedEventArgs e)
        {
            _vm.IncludeSelectedCells();
            RenderPreview();
        }

        private void BtnIncludeAll_Click(object sender, RoutedEventArgs e)
        {
            _vm.IncludeAllCells();
            RenderPreview();
        }

        private void BtnClearSelection_Click(object sender, RoutedEventArgs e)
        {
            _vm.ClearSelection();
            RenderPreview();
        }
        private void BtnXDivisionPlus_Click(object sender, RoutedEventArgs e)
        {
            _vm.XDivisionCount += 1;
            RenderPreview();
        }

        private void BtnXDivisionMinus_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.XDivisionCount > 1)
                _vm.XDivisionCount -= 1;

            RenderPreview();
        }

        private void BtnYDivisionPlus_Click(object sender, RoutedEventArgs e)
        {
            _vm.YDivisionCount += 1;
            RenderPreview();
        }

        private void BtnYDivisionMinus_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.YDivisionCount > 1)
                _vm.YDivisionCount -= 1;

            RenderPreview();
        }
    }
}
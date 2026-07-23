using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using VIS = System.Windows.Visibility;

namespace BA.UI.KeyplanGrid
{
    public partial class KeyplanLevelPickerWindow : Window
    {
        public const string SchemeName = "KP_GrossArea(KeyPlan)";

        private readonly Document _doc;
        private readonly UIDocument _uiDoc;
        private List<KeyplanLevelOption> _options;

        /// <summary>
        /// Populated with the selected option after a successful OK.
        /// Null if the dialog was cancelled.
        /// </summary>
        public KeyplanLevelOption SelectedOption { get; private set; }

        public KeyplanLevelPickerWindow(UIDocument uiDoc)
        {
            InitializeComponent();

            _uiDoc = uiDoc ?? throw new ArgumentNullException(nameof(uiDoc));
            _doc = uiDoc.Document;

            _options = BuildLevelOptions(_doc);
            PopulateList(preselectActiveLevel: true);
        }

        // -------------------------------------------------------------------------
        // List population
        // -------------------------------------------------------------------------

        private void PopulateList(bool preselectActiveLevel)
        {
            ElementId previousId =
                (LevelListBox.SelectedItem as KeyplanLevelOption)?.Level?.Id
                ?? ElementId.InvalidElementId;

            LevelListBox.Items.Clear();

            foreach (KeyplanLevelOption option in _options)
                LevelListBox.Items.Add(option);

            KeyplanLevelOption toSelect = null;

            if (previousId != null && previousId != ElementId.InvalidElementId)
            {
                toSelect = _options.FirstOrDefault(o =>
                    o.Level != null && o.Level.Id == previousId);
            }

            if (toSelect == null && preselectActiveLevel)
            {
                Level activeLevel = _doc.ActiveView?.GenLevel;

                if (activeLevel != null)
                {
                    toSelect = _options.FirstOrDefault(o =>
                        o.Level != null && o.Level.Id == activeLevel.Id);
                }

                if (toSelect == null)
                    toSelect = _options.FirstOrDefault(o => o.IsReady);
            }

            if (toSelect != null)
                LevelListBox.SelectedItem = toSelect;
            else
                UpdateButtonsForSelection(null);
        }

        private void PopulateListSelectingLevel(ElementId levelId)
        {
            LevelListBox.Items.Clear();

            foreach (KeyplanLevelOption option in _options)
                LevelListBox.Items.Add(option);

            KeyplanLevelOption toSelect = null;

            if (levelId != null && levelId != ElementId.InvalidElementId)
            {
                toSelect = _options.FirstOrDefault(o =>
                    o.Level != null && o.Level.Id == levelId);
            }

            if (toSelect == null)
                toSelect = _options.FirstOrDefault(o => o.IsReady);

            if (toSelect != null)
                LevelListBox.SelectedItem = toSelect;
            else
                UpdateButtonsForSelection(null);
        }

        // -------------------------------------------------------------------------
        // Level resolution
        // -------------------------------------------------------------------------

        private static List<KeyplanLevelOption> BuildLevelOptions(Document doc)
        {
            List<Level> levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            List<ViewPlan> schemeViews = GetSchemeAreaPlanViews(doc);

            List<KeyplanLevelOption> result = new List<KeyplanLevelOption>();

            foreach (Level level in levels)
            {
                result.Add(EvaluateLevel(doc, level, schemeViews));
            }

            return result;
        }

        private static List<ViewPlan> GetSchemeAreaPlanViews(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(v => !v.IsTemplate
                    && v.AreaScheme != null
                    && string.Equals(v.AreaScheme.Name, SchemeName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private static KeyplanLevelOption EvaluateLevel(Document doc, Level level, List<ViewPlan> schemeViews)
        {
            KeyplanLevelOption option = new KeyplanLevelOption
            {
                Level = level,
                LevelName = level.Name ?? string.Empty,
                Elevation = level.Elevation
            };

            ViewPlan matchingView = schemeViews.FirstOrDefault(v =>
                v.GenLevel != null && v.GenLevel.Id == level.Id);

            if (matchingView == null)
            {
                option.IsReady = false;
                option.CanCreateView = true;
                option.NotReadyReason =
                    $"No '{SchemeName}' area plan view exists for level '{level.Name}'.\n\n" +
                    "If the area scheme already exists in this project, click 'Create Area Plan View' below.\n\n" +
                    "If this is the first time, set up the scheme manually first:\n" +
                    "1. Architecture tab > Area > Area and Volume Computations > Area Schemes tab.\n" +
                    $"2. Click New, name it '{SchemeName}', click OK.\n" +
                    "3. Then click 'Create Area Plan View' below.";

                return option;
            }

            CurveLoop outerLoop = KeyplanAreaSourceService.GetLargestOuterLoopFromView(doc, matchingView);

            if (outerLoop == null)
            {
                option.IsReady = false;
                option.CanCreateView = false;
                option.SourceView = matchingView;
                option.NotReadyReason =
                    $"The '{SchemeName}' area plan for level '{level.Name}' exists but has no valid Area boundary.\n\n" +
                    "Click 'Open Area Plan View' below, add Area Boundary lines, then use " +
                    "Place Area (or Place Areas Automatically) so at least one Area element exists.\n\n" +
                    "Then click Refresh here.";

                return option;
            }

            option.IsReady = true;
            option.CanCreateView = false;
            option.SourceView = matchingView;
            option.OuterLoop = outerLoop;
            return option;
        }

        // -------------------------------------------------------------------------
        // Create Area Plan View
        // -------------------------------------------------------------------------

        private void BtnCreateView_Click(object sender, RoutedEventArgs e)
        {
            KeyplanLevelOption selected = LevelListBox.SelectedItem as KeyplanLevelOption;

            if (selected == null || selected.Level == null || !selected.CanCreateView)
                return;

            ElementId targetLevelId = selected.Level.Id;
            string boundaryWarning = string.Empty;
            int manualBoundaryCount = 0;
            bool nativeBoundaryCreated = false;

            UIApplication uiApp = _uiDoc.Application;

            // Auto-answer Revit's "Automatically create area boundary lines..."
            // prompt with Yes so the native, wall-associated boundary feature runs
            // without user interaction.
            void OnDialogShowing(object s, Autodesk.Revit.UI.Events.DialogBoxShowingEventArgs args)
            {
                // 6 == IDYES. Applies to both message-box and TaskDialog variants.
                args.OverrideResult(6);
            }

            try
            {
                using (Transaction tx = new Transaction(_doc, $"Create {SchemeName} Area Plan"))
                {
                    tx.Start();

                    ViewFamilyType areaPlanVft = ResolveAreaSchemeViewFamilyType(_doc);

                    uiApp.DialogBoxShowing += OnDialogShowing;

                    ViewPlan newView;
                    try
                    {
                        newView = ViewPlan.Create(_doc, areaPlanVft.Id, selected.Level.Id);
                    }
                    finally
                    {
                        uiApp.DialogBoxShowing -= OnDialogShowing;
                    }

                    newView.Name = EnsureUniqueViewName(_doc, $"{SchemeName} - {selected.Level.Name}");

                    _doc.Regenerate();

                    // Did the native feature produce a usable boundary/Area?
                    nativeBoundaryCreated =
                        KeyplanAreaSourceService.GetLargestOuterLoopFromView(_doc, newView) != null;

                    // Fallback: only create manual static boundary lines if the
                    // native path produced nothing (e.g. user's Revit settings or
                    // model state prevented it).
                    if (!nativeBoundaryCreated)
                    {
                        manualBoundaryCount = CreateBoundaryFromExteriorWalls(
                            _doc, newView, selected.Level, out boundaryWarning);
                    }

                    tx.Commit();
                }

                _options = BuildLevelOptions(_doc);
                PopulateListSelectingLevel(targetLevelId);

                string message = $"Area plan view created for level '{selected.LevelName}'.";

                if (nativeBoundaryCreated)
                {
                    message += "\n\nBoundary lines and Area were created automatically from " +
                               "the building's exterior walls. If the level now shows 'ready', " +
                               "you can proceed directly.";
                }
                else if (manualBoundaryCount > 0 && string.IsNullOrEmpty(boundaryWarning))
                {
                    message += $"\n\n{manualBoundaryCount} boundary line(s) were created from " +
                               "exterior wall centerlines and an Area element was placed.";
                }
                else if (manualBoundaryCount > 0)
                {
                    message += $"\n\n{manualBoundaryCount} boundary line(s) were created.\n\n" + boundaryWarning;
                }
                else
                {
                    message += "\n\n" + boundaryWarning +
                               "\n\nClick 'Open Area Plan View', add Area Boundary lines, " +
                               "place an Area, then click Refresh.";
                }

                MessageBox.Show(message, "Keyplan Grid",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Keyplan Grid - Setup Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Failed to create area plan view:\n\n" + ex.Message,
                    "Keyplan Grid - Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // -------------------------------------------------------------------------
        // Open view / Refresh
        // -------------------------------------------------------------------------

        private void BtnOpenView_Click(object sender, RoutedEventArgs e)
        {
            KeyplanLevelOption selected = LevelListBox.SelectedItem as KeyplanLevelOption;

            if (selected?.SourceView == null)
                return;

            try
            {
                _uiDoc.ActiveView = selected.SourceView;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Could not switch to the area plan view automatically.\n\n" +
                    $"Open the view '{selected.SourceView.Name}' manually from the Project Browser.\n\n" +
                    "Details: " + ex.Message,
                    "Keyplan Grid",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            ElementId currentLevelId =
                (LevelListBox.SelectedItem as KeyplanLevelOption)?.Level?.Id
                ?? ElementId.InvalidElementId;

            _options = BuildLevelOptions(_doc);
            PopulateListSelectingLevel(currentLevelId);
        }

        // -------------------------------------------------------------------------
        // ViewFamilyType resolution
        // -------------------------------------------------------------------------

        /// <summary>
        /// Finds the ViewFamilyType for area plans belonging to the KP_GrossArea(KeyPlan)
        /// AreaScheme. The AreaScheme itself cannot be created via the Revit API — it must
        /// be created once, manually, via Architecture > Area > Area and Volume Computations
        /// > Area Schemes > New. Revit then creates a matching ViewFamilyType
        /// (ViewFamily.AreaPlan) with the same name as the scheme.
        /// Throws InvalidOperationException with setup instructions if the scheme does not exist.
        /// </summary>
        private static ViewFamilyType ResolveAreaSchemeViewFamilyType(Document doc)
        {
            AreaScheme scheme = new FilteredElementCollector(doc)
                .OfClass(typeof(AreaScheme))
                .Cast<AreaScheme>()
                .FirstOrDefault(s => string.Equals(s.Name, SchemeName, StringComparison.OrdinalIgnoreCase));

            if (scheme == null)
            {
                throw new InvalidOperationException(
                    $"The area scheme '{SchemeName}' does not exist in this project and cannot " +
                    "be created automatically (the Revit API does not support creating area schemes).\n\n" +
                    "One-time setup:\n" +
                    "1. Architecture tab > Area > Area and Volume Computations.\n" +
                    "2. Go to the Area Schemes tab.\n" +
                    $"3. Click New and name the scheme '{SchemeName}'.\n" +
                    "4. Click OK.\n" +
                    "5. Run this command again.");
            }

            ViewFamilyType vft = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == ViewFamily.AreaPlan
                    && string.Equals(t.Name, SchemeName, StringComparison.OrdinalIgnoreCase));

            if (vft == null)
            {
                throw new InvalidOperationException(
                    $"Area scheme '{SchemeName}' exists but its associated area plan view family type " +
                    "could not be found by name. This usually means no area plan view has ever been " +
                    "created under this scheme yet — create one manually first:\n\n" +
                    "1. Architecture tab > Area > Area Plan.\n" +
                    $"2. Select '{SchemeName}' in the Type dropdown.\n" +
                    "3. Pick any level and click OK.\n\n" +
                    "After that, this button will work for the remaining levels.");
            }

            return vft;
        }

        private static string EnsureUniqueViewName(Document doc, string baseName)
        {
            if (KeyplanViewService.FindViewByName(doc, baseName) == null)
                return baseName;

            int suffix = 2;
            string candidate;

            do
            {
                candidate = $"{baseName} ({suffix})";
                suffix++;
            }
            while (KeyplanViewService.FindViewByName(doc, candidate) != null && suffix < 1000);

            return candidate;
        }

        // -------------------------------------------------------------------------
        // Selection handling
        // -------------------------------------------------------------------------

        private void LevelListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            KeyplanLevelOption selected = LevelListBox.SelectedItem as KeyplanLevelOption;
            UpdateButtonsForSelection(selected);
        }
        private static int CreateBoundaryFromExteriorWalls(
    Document doc,
    ViewPlan areaPlanView,
    Level level,
    out string warning)
        {
            warning = string.Empty;

            List<Wall> exteriorWalls = new FilteredElementCollector(doc)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .Where(w => w != null
                    && w.WallType != null
                    && w.WallType.Function == WallFunction.Exterior
                    && IsWallOnLevel(w, level))
                .ToList();

            if (exteriorWalls.Count == 0)
            {
                warning = "No exterior walls found on this level (WallType.Function == Exterior). " +
                          "Boundary lines were not created — draw them manually.";
                return 0;
            }

            double z = level.Elevation;
            Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0.0, 0.0, z));
            SketchPlane sketchPlane = SketchPlane.Create(doc, plane);

            int created = 0;
            List<XYZ> allEndpoints = new List<XYZ>();

            foreach (Wall wall in exteriorWalls)
            {
                LocationCurve locCurve = wall.Location as LocationCurve;
                Curve curve = locCurve?.Curve;
                if (curve == null)
                    continue;

                // Translate the curve to the level elevation so it lies on the
                // sketch plane. Wall location curves are horizontal, so a pure
                // Z translation is valid for lines and arcs alike.
                double curveZ = curve.GetEndPoint(0).Z;
                Curve flattened = Math.Abs(curveZ - z) > 1e-9
                    ? curve.CreateTransformed(Transform.CreateTranslation(new XYZ(0.0, 0.0, z - curveZ)))
                    : curve;

                try
                {
                    ModelCurve boundary = doc.Create.NewAreaBoundaryLine(sketchPlane, flattened, areaPlanView);
                    if (boundary != null)
                    {
                        created++;
                        allEndpoints.Add(flattened.GetEndPoint(0));
                        allEndpoints.Add(flattened.GetEndPoint(1));
                    }
                }
                catch
                {
                    // Individual curve failures (zero-length after transform, etc.)
                    // should not abort the rest of the boundary.
                }
            }

            if (created == 0)
            {
                warning = "Exterior walls were found but no boundary lines could be created. " +
                          "Draw them manually.";
                return 0;
            }

            // Attempt to place an Area at the centroid of the wall endpoints.
            // For concave footprints the centroid may fall outside the boundary,
            // in which case the Area is created "not enclosed" or placement fails —
            // the ready-check will catch it and the user places the Area manually.
            try
            {
                double cx = allEndpoints.Average(p => p.X);
                double cy = allEndpoints.Average(p => p.Y);

                doc.Create.NewArea(areaPlanView, new UV(cx, cy));
            }
            catch
            {
                warning = "Boundary lines were created, but the Area element could not be " +
                          "placed automatically. Place it manually (Architecture > Area > Area, " +
                          "click inside the boundary).";
            }

            return created;
        }

        private static bool IsWallOnLevel(Wall wall, Level level)
        {
            Parameter baseLevelParam = wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
            if (baseLevelParam == null)
                return false;

            ElementId baseLevelId = baseLevelParam.AsElementId();
            return baseLevelId != null && baseLevelId == level.Id;
        }
        private void UpdateButtonsForSelection(KeyplanLevelOption selected)
        {
            if (selected == null)
            {
                BtnOk.IsEnabled = false;
                BtnCreateView.Visibility = VIS.Collapsed;
                BtnOpenView.Visibility = VIS.Collapsed;
                BtnRefresh.Visibility = VIS.Collapsed;
                InstructionsText.Visibility = VIS.Collapsed;
                return;
            }

            if (selected.IsReady)
            {
                BtnOk.IsEnabled = true;
                BtnCreateView.Visibility = VIS.Collapsed;
                BtnOpenView.Visibility = VIS.Collapsed;
                BtnRefresh.Visibility = VIS.Collapsed;
                InstructionsText.Visibility = VIS.Collapsed;
                return;
            }

            BtnOk.IsEnabled = false;
            InstructionsText.Text = selected.NotReadyReason;
            InstructionsText.Visibility = VIS.Visible;

            // No view exists yet — offer to create it.
            BtnCreateView.Visibility = selected.CanCreateView
                ? VIS.Visible
                : VIS.Collapsed;

            // View exists but has no valid boundary — offer open + refresh.
            bool viewExistsButEmpty = !selected.CanCreateView && selected.SourceView != null;
            BtnOpenView.Visibility = viewExistsButEmpty
                ? VIS.Visible
                : VIS.Collapsed;
            BtnRefresh.Visibility = viewExistsButEmpty
                ? VIS.Visible
                : VIS.Collapsed;
        }

        private void LevelListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (BtnOk.IsEnabled)
                AcceptSelection();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            AcceptSelection();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            SelectedOption = null;
            DialogResult = false;
            Close();
        }

        private void AcceptSelection()
        {
            KeyplanLevelOption selected = LevelListBox.SelectedItem as KeyplanLevelOption;

            if (selected == null || !selected.IsReady)
                return;

            SelectedOption = selected;
            DialogResult = true;
            Close();
        }
    }
}
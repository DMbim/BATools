using Autodesk.Revit.DB;
using System;
using Autodesk.Revit.DB.Architecture;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BA.UI.KeyplanGrid
{
    public partial class KeyplanLevelPickerWindow : Window
    {
        public const string SchemeName = "KP_GrossArea(KeyPlan)";

        private readonly Document _doc;
        private List<KeyplanLevelOption> _options;

        /// <summary>
        /// Populated with the selected option after a successful OK.
        /// Null if the dialog was cancelled.
        /// </summary>
        public KeyplanLevelOption SelectedOption { get; private set; }

        public KeyplanLevelPickerWindow(Document doc)
        {
            InitializeComponent();

            _doc = doc ?? throw new ArgumentNullException(nameof(doc));

            _options = BuildLevelOptions(_doc);
            PopulateList(preselectActiveLevel: true);
        }

        // -------------------------------------------------------------------------
        // List population
        // -------------------------------------------------------------------------

        private void PopulateList(bool preselectActiveLevel)
        {
            object previouslySelectedLevelId = (LevelListBox.SelectedItem as KeyplanLevelOption)?.Level?.Id;

            LevelListBox.Items.Clear();

            foreach (KeyplanLevelOption option in _options)
                LevelListBox.Items.Add(option);

            KeyplanLevelOption toSelect = null;

            if (previouslySelectedLevelId != null)
            {
                toSelect = _options.FirstOrDefault(o =>
                    o.Level != null && o.Level.Id.Equals(previouslySelectedLevelId));
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



        private void UpdateButtonsForSelection(KeyplanLevelOption selected)
        {
            if (selected == null)
            {
                BtnOk.IsEnabled = false;
                BtnCreateView.Visibility = System.Windows.Visibility.Collapsed;
                InstructionsText.Visibility = System.Windows.Visibility.Collapsed;
                return;
            }

            if (selected.IsReady)
            {
                BtnOk.IsEnabled = true;
                BtnCreateView.Visibility = System.Windows.Visibility.Collapsed;
                InstructionsText.Visibility = System.Windows.Visibility.Collapsed;
                return;
            }

            BtnOk.IsEnabled = false;
            InstructionsText.Text = selected.NotReadyReason;
            InstructionsText.Visibility = System.Windows.Visibility.Visible;
            BtnCreateView.Visibility = selected.CanCreateView ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        
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
                    "If the area scheme already exists in this project, click 'Create Area Plan View " +
                    "for this Level' below.\n\n" +
                    "If this is the first time, set up the scheme manually first:\n" +
                    "1. Architecture tab > Area > Area and Volume Computations > Area Schemes tab.\n" +
                    $"2. Click New, name it '{SchemeName}', click OK.\n" +
                    "3. Then click 'Create Area Plan View for this Level' below (or re-open this dialog).\n" +
                    "4. In the new view, trace Area Boundary lines and Place Areas.\n" +
                    "5. Re-select this level in this dialog.";

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
                    "Open that view, add Area Boundary lines, then use Place Area " +
                    "(or Place Areas Automatically) so at least one Area element exists.\n\n" +
                    "Re-select this level in this dialog after placing the area.";

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

            try
            {
                using (Transaction tx = new Transaction(_doc, $"Create {SchemeName} Area Plan"))
                {
                    tx.Start();

                    ViewFamilyType areaPlanVft = ResolveAreaSchemeViewFamilyType(_doc);

                    ViewPlan newView = ViewPlan.Create(_doc, areaPlanVft.Id, selected.Level.Id);
                    newView.Name = EnsureUniqueViewName(_doc, $"{SchemeName} - {selected.Level.Name}");

                    tx.Commit();
                }

                _options = BuildLevelOptions(_doc);
                PopulateList(preselectActiveLevel: false);

                System.Windows.MessageBox.Show(
                    $"Area plan view created for level '{selected.LevelName}'.\n\n" +
                    "Open that view, add Area Boundary lines, and place at least one Area element. " +
                    "Then re-select this level here.",
                    "Keyplan Grid",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (InvalidOperationException ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "Keyplan Grid - Setup Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    "Failed to create area plan view:\n\n" + ex.Message,
                    "Keyplan Grid - Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// </summary>
        /// <summary>
        /// Finds the ViewFamilyType for area plans belonging to the KP_GrossArea(KeyPlan)
        /// AreaScheme. The AreaScheme itself cannot be created via the Revit API — it must
        /// be created once, manually, via Architecture > Area > Area and Volume Computations
        /// > Area Schemes > New. Revit then auto-creates a matching ViewFamilyType
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
                    "could not be found by name. This is unexpected for an existing scheme — " +
                    "verify in the Project Browser under Areas that the scheme is set up correctly.");
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
            UpdateButtonsForSelection(selected, GetVisibility());
        }

        private System.Windows.Visibility GetVisibility()
        {
            return Visibility;
        }

        private void UpdateButtonsForSelection(KeyplanLevelOption selected, System.Windows.Visibility visibility)
        {
            if (selected == null)
            {
                BtnOk.IsEnabled = false;
                BtnCreateView.Visibility = System.Windows.Visibility.Collapsed;
                InstructionsText.Visibility = System.Windows.Visibility.Collapsed;
                return;
            }

            if (selected.IsReady)
            {
                BtnOk.IsEnabled = true;
                BtnCreateView.Visibility = System.Windows.Visibility.Collapsed;
                InstructionsText.Visibility = System.Windows.Visibility.Collapsed;
                return;
            }

            BtnOk.IsEnabled = false;
            InstructionsText.Text = selected.NotReadyReason;
            InstructionsText.Visibility = System.Windows.Visibility.Visible;
            BtnCreateView.Visibility = selected.CanCreateView ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
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
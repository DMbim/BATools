using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Core.Views.ScopeBoxes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;

namespace BA.UI.Views.ScopeBoxes
{
    public partial class ScopeBoxManagerWindow : Window
    {
        private readonly UIApplication _uiApp;
        private readonly UIDocument _uiDoc;
        private readonly Document _doc;

        private readonly ObservableCollection<ScopeBoxInfo> _scopeBoxes = new();
        private readonly ObservableCollection<ViewScopeRow> _rows = new();

        private ICollectionView _viewCollection;

        public ScopeBoxManagerWindow(UIApplication uiApp, UIDocument uiDoc, Document doc)
        {
            InitializeComponent();

            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _uiDoc = uiDoc ?? throw new ArgumentNullException(nameof(uiDoc));
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));

            Loaded += ScopeBoxManagerWindow_Loaded;
        }

        private void ScopeBoxManagerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            helper.Owner = _uiApp.MainWindowHandle;

            LoadScopeBoxes();
            LoadViews();
        }

        private void LoadScopeBoxes()
        {
            _scopeBoxes.Clear();

            foreach (ScopeBoxInfo sb in ScopeBoxService.GetAllScopeBoxes(_doc))
                _scopeBoxes.Add(sb);

            CmbScopeBoxes.ItemsSource = _scopeBoxes;

            if (_scopeBoxes.Count > 0 && CmbScopeBoxes.SelectedIndex < 0)
                CmbScopeBoxes.SelectedIndex = 0;
        }

        private void LoadViews()
        {
            _rows.Clear();

            ICollection<ElementId> restrictTo = null;

            if (ChkOnlySelectedViews.IsChecked == true)
            {
                var selectedIds = _uiDoc.Selection.GetElementIds();
                if (selectedIds != null && selectedIds.Count > 0)
                {
                    restrictTo = new List<ElementId>(selectedIds);
                }
                else
                {
                    BindRows();
                    TxtSummary.Text = "Only selected views is enabled, but no views are selected.";
                    return;
                }
            }

            List<ViewScopeRow> rows = ScopeBoxService.GetEligibleViews(_doc, restrictTo);

            foreach (ViewScopeRow row in rows)
                _rows.Add(row);

            BindRows();
            TxtSummary.Text = $"Loaded {_rows.Count} eligible views and {_scopeBoxes.Count} scope boxes.";
        }

        private void BindRows()
        {
            GridViews.ItemsSource = _rows;

            _viewCollection = CollectionViewSource.GetDefaultView(GridViews.ItemsSource);
            if (_viewCollection != null)
            {
                _viewCollection.Filter = FilterRow;
                _viewCollection.Refresh();
            }
        }

        private bool FilterRow(object obj)
        {
            if (obj is not ViewScopeRow row)
                return false;

            string filter = TxtFilter?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(filter))
                return true;

            return row.ViewName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || row.ViewTypeName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || row.FamilyName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || row.CurrentScopeBoxName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || row.Status.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void TxtFilter_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _viewCollection?.Refresh();
        }

        private void BtnReload_Click(object sender, RoutedEventArgs e)
        {
            LoadScopeBoxes();
            LoadViews();
        }

        private void BtnCheckAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (ViewScopeRow row in _rows)
                row.IsChecked = true;

            TxtSummary.Text = $"Checked {_rows.Count} views.";
        }

        private void BtnUncheckAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (ViewScopeRow row in _rows)
                row.IsChecked = false;

            TxtSummary.Text = "Unchecked all views.";
        }

        private void BtnCheckUnlocked_Click(object sender, RoutedEventArgs e)
        {
            int count = 0;

            foreach (ViewScopeRow row in _rows)
            {
                row.IsChecked = !row.IsLocked;
                if (row.IsChecked)
                    count++;
            }

            TxtSummary.Text = $"Checked {count} unlocked views.";
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            if (CmbScopeBoxes.SelectedItem is not ScopeBoxInfo selectedScopeBox)
            {
                MessageBox.Show(this, "Select a scope box first.", "Scope Box Manager",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            List<ViewScopeRow> checkedRows = _rows.Where(x => x.IsChecked).ToList();
            if (checkedRows.Count == 0)
            {
                MessageBox.Show(this, "No views are checked.", "Scope Box Manager",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int changed = ScopeBoxService.ApplyScopeBox(_doc, checkedRows, selectedScopeBox.Id);

            foreach (ViewScopeRow row in checkedRows)
                ScopeBoxService.RefreshRowState(_doc, row);

            GridViews.Items.Refresh();
            TxtSummary.Text = $"Applied '{selectedScopeBox.Name}' to {changed} view(s).";
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            List<ViewScopeRow> checkedRows = _rows.Where(x => x.IsChecked).ToList();
            if (checkedRows.Count == 0)
            {
                MessageBox.Show(this, "No views are checked.", "Scope Box Manager",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int changed = ScopeBoxService.ClearScopeBox(_doc, checkedRows);

            foreach (ViewScopeRow row in checkedRows)
                ScopeBoxService.RefreshRowState(_doc, row);

            GridViews.Items.Refresh();
            TxtSummary.Text = $"Cleared scope box from {changed} view(s).";
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
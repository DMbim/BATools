using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BATools.SelectionManager.ExternalEvents;

namespace BATools.SelectionManager.Views
{
    public partial class PickFamilyTypeDialog : Window, INotifyPropertyChanged
    {
        // ── INotifyPropertyChanged ────────────────────────────────────────────
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // ── Dialog-local row model ────────────────────────────────────────────
        private class FamilyRow
        {
            public string UniqueId { get; init; } = string.Empty;
            public string FamilyName { get; init; } = string.Empty;
            public string TypeName { get; init; } = string.Empty;
            public string CategoryName { get; init; } = string.Empty;
            public string IconChar => FamilyName.Length > 0
                ? FamilyName[..1].ToUpperInvariant() : "F";
        }

        // ── State ─────────────────────────────────────────────────────────────
        private List<FamilyRow> _allRows = new();
        private string _activeCategory = string.Empty;

        private bool _isLoading = true;
        public bool IsLoading
        {
            get => _isLoading;
            private set { _isLoading = value; Notify(); }
        }

        /// <summary>
        /// Fires for each family the user confirms.
        /// Dialog stays open so the user can keep picking.
        /// </summary>
        public event Action<FamilyTypeInfo>? FamilySelected;

        // ── Constructor ───────────────────────────────────────────────────────
        public PickFamilyTypeDialog()
        {
            InitializeComponent();
            DataContext = this;
        }

        // ── Called by FamiliesToolbarTabViewModel after Revit data arrives ────
        public void PopulateList(List<FamilyTypeInfo> families)
        {
            IsLoading = false;

            _allRows = families.Select(f => new FamilyRow
            {
                UniqueId = f.UniqueId,
                FamilyName = f.FamilyName,
                TypeName = f.TypeName,
                CategoryName = f.CategoryName
            }).ToList();

            // Category pills: "All" + distinct sorted categories
            var categories = new List<string> { "All" };
            categories.AddRange(
                _allRows
                    .Select(r => r.CategoryName)
                    .Where(c => !string.IsNullOrEmpty(c))
                    .Distinct()
                    .OrderBy(c => c));

            CategoryPills.ItemsSource = categories;

            ApplyFilter();
        }

        // ── Filter ────────────────────────────────────────────────────────────
        private void ApplyFilter()
        {
            string search = TxtSearch.Text.Trim();

            IEnumerable<FamilyRow> filtered = _allRows;

            if (!string.IsNullOrEmpty(_activeCategory) && _activeCategory != "All")
                filtered = filtered.Where(r => r.CategoryName == _activeCategory);

            if (!string.IsNullOrEmpty(search))
                filtered = filtered.Where(r =>
                    r.FamilyName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    r.TypeName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    r.CategoryName.Contains(search, StringComparison.OrdinalIgnoreCase));

            var result = filtered.ToList();
            FamilyList.ItemsSource = result;

            if (result.Count > 0)
                FamilyList.SelectedIndex = 0;

            UpdateCount();
        }

        private void UpdateCount()
        {
            TxtCount.Text = _allRows.Count == 0
                ? string.Empty
                : $"{FamilyList.Items.Count} / {_allRows.Count}";
        }

        // ── Event handlers ────────────────────────────────────────────────────
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
            => ApplyFilter();

        private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down && FamilyList.Items.Count > 0)
            {
                FamilyList.Focus();
                FamilyList.SelectedIndex = 0;
                e.Handled = true;
            }
        }

        private void Pill_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is string cat)
            {
                _activeCategory = cat == "All" ? string.Empty : cat;
                ApplyFilter();
            }
        }

        private void FamilyList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
            => ConfirmSelected();

        private void Ok_Click(object sender, RoutedEventArgs e)
            => ConfirmSelected();

        private void Cancel_Click(object sender, RoutedEventArgs e)
            => Close();

        private void ConfirmSelected()
        {
            if (FamilyList.SelectedItem is not FamilyRow row) return;

            FamilySelected?.Invoke(new FamilyTypeInfo
            {
                UniqueId = row.UniqueId,
                FamilyName = row.FamilyName,
                TypeName = row.TypeName,
                CategoryName = row.CategoryName
            });

            // Keep dialog open — clear search for next pick
            TxtSearch.Text = string.Empty;
            FamilyList.Focus();
        }
    }
}
using Autodesk.Revit.DB;
using Autodesk.Revit.UI; // <- NEW
using BA.Subcategories.Models;
using BA.Subcategories.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using CtkRelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace BA.Subcategories.ViewModels
{
    public class SubcategoryManagerViewModel : ObservableObject
    {
        // ── Revit context (set before showing window, read only after) ─────────

        public Document Doc { get; init; } = null!;
        public UIDocument UiDoc { get; init; } = null!; // <- NEW
        public Category ParentCategory { get; init; } = null!;
        public Family OwnerFamily { get; init; } = null!;

        // ── Subcategory list ──────────────────────────────────────────────────

        public ObservableCollection<SubcategoryRow> Subcategories { get; } = new();

        private SubcategoryRow? _selectedSubcategory;
        public SubcategoryRow? SelectedSubcategory
        {
            get => _selectedSubcategory;
            set
            {
                SetProperty(ref _selectedSubcategory, value);
                DeleteSubcategoryCommand.NotifyCanExecuteChanged();
            }
        }

        private string _newSubcategoryName = string.Empty;
        public string NewSubcategoryName
        {
            get => _newSubcategoryName;
            set
            {
                SetProperty(ref _newSubcategoryName, value);
                AddSubcategoryCommand.NotifyCanExecuteChanged();
            }
        }

        // ── Geometry list ─────────────────────────────────────────────────────

        public ObservableCollection<FamilyGeometryRow> GeometryItems { get; } = new();

        private FamilyGeometryRow? _selectedGeometryRow;
        public FamilyGeometryRow? SelectedGeometryRow
        {
            get => _selectedGeometryRow;
            set => SetProperty(ref _selectedGeometryRow, value);
        }

        /// <summary>
        /// Pushes the given rows into the Revit selection so they highlight in
        /// the active view exactly as if picked there directly. Called from
        /// the geometry DataGrid's SelectionChanged handler in code behind,
        /// since DataGrid.SelectedItems is not bindable. Safe to call with an
        /// empty or null list, which clears the Revit selection. Depends on
        /// this window running on Revit's API thread, true as long as it is
        /// always shown with ShowDialog() directly inside an
        /// IExternalCommand.Execute(), the same assumption Apply() already
        /// relies on for its direct Transaction usage.
        /// </summary>
        public void UpdateGeometrySelection(IReadOnlyList<FamilyGeometryRow> selectedRows)
        {
            if (UiDoc == null) return;

            try
            {
                var ids = new List<ElementId>();

                if (selectedRows != null)
                {
                    foreach (var row in selectedRows)
                    {
                        if (row == null || row.Id == ElementId.InvalidElementId) continue;
                        if (Doc.GetElement(row.Id) == null) continue;
                        ids.Add(row.Id);
                    }
                }

                UiDoc.Selection.SetElementIds(ids);
            }
            catch
            {
                // Selection highlight is a convenience only, never let a failure
                // here propagate into the rest of the UI.
            }
        }

        // ── Assignment controls ───────────────────────────────────────────────

        public IEnumerable<ApplyScope> ApplyScopes { get; } = Enum.GetValues<ApplyScope>();

        private ApplyScope _selectedScope = ApplyScope.AllWithNoSubcategory;
        public ApplyScope SelectedScope
        {
            get => _selectedScope;
            set => SetProperty(ref _selectedScope, value);
        }

        private SubcategoryRow? _targetSubcategoryRow;
        public SubcategoryRow? TargetSubcategoryRow
        {
            get => _targetSubcategoryRow;
            set
            {
                SetProperty(ref _targetSubcategoryRow, value);
                AssignSubcategoryCommand.NotifyCanExecuteChanged();
            }
        }

        // ── Status ────────────────────────────────────────────────────────────

        private string _statusText = string.Empty;
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        // ── Result, read by code behind after dialog closes ──────────────────

        public bool Applied { get; private set; }
        public List<string> ApplyLog { get; } = new();

        // ── Commands ──────────────────────────────────────────────────────────

        public RelayCommand AddSubcategoryCommand { get; }
        public RelayCommand DeleteSubcategoryCommand { get; }
        public RelayCommand AssignSubcategoryCommand { get; }
        public RelayCommand EnsureCoreSubcategoriesCommand { get; }
        public RelayCommand PickColorCommand { get; }
        public RelayCommand ApplyCommand { get; }
        public RelayCommand CancelCommand { get; }

        // ── Action callbacks wired by code behind ─────────────────────────────

        public Action? RequestClose { get; set; }
        public Func<Color, Color?>? RequestColorPick { get; set; }

        // ── Constructor ───────────────────────────────────────────────────────

        public SubcategoryManagerViewModel()
        {
            AddSubcategoryCommand = new RelayCommand(AddSubcategory,
                () => !string.IsNullOrWhiteSpace(NewSubcategoryName));
            DeleteSubcategoryCommand = new RelayCommand(DeleteSubcategory,
                () => SelectedSubcategory != null);
            AssignSubcategoryCommand = new RelayCommand(AssignSubcategory,
                () => TargetSubcategoryRow != null);
            EnsureCoreSubcategoriesCommand = new RelayCommand(EnsureCoreSubcategories);
            PickColorCommand = new RelayCommand(PickColor,
                () => SelectedSubcategory != null);
            ApplyCommand = new RelayCommand(Apply);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke());
        }

        // ── Initialisation ────────────────────────────────────────────────────

        /// <summary>Call after setting Doc/UiDoc/ParentCategory/OwnerFamily.</summary>
        public void Initialise()
        {
            LoadSubcategories();
            LoadGeometry();
            StatusText = $"Family: {OwnerFamily?.Name ?? "?"} | Category: {ParentCategory?.Name ?? "?"}";
        }

        private void LoadSubcategories()
        {
            Subcategories.Clear();
            var rows = SubcategoryService.BuildRows(Doc, ParentCategory);
            foreach (var r in rows) Subcategories.Add(r);
        }

        private void LoadGeometry()
        {
            GeometryItems.Clear();
            var candidates = new FilteredElementCollector(Doc)
                .WhereElementIsNotElementType()
                .ToElements()
                .Where(GraphicsAssignmentService.IsFamilyGeometryCandidate)
                .ToList();

            foreach (var e in candidates)
            {
                string subcatName = GraphicsAssignmentService.GetSubcategoryName(Doc, e);
                GeometryItems.Add(new FamilyGeometryRow
                {
                    Id = e.Id,
                    DisplayName = $"{e.Id.Value} – {e.GetType().Name}",
                    CategoryName = e.Category?.Name ?? string.Empty,
                    SubcategoryName = subcatName
                });
            }
        }

        // ── Subcategory CRUD (pending, applied on Apply) ─────────────────────

        private void AddSubcategory()
        {
            string name = NewSubcategoryName.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;

            if (Subcategories.Any(s =>
                    string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                StatusText = $"Subcategory '{name}' already exists.";
                return;
            }

            Subcategories.Add(new SubcategoryRow
            {
                CategoryId = null,
                Name = name,
                LineWeight = 1,
                LineColor = Colors.Black,
                IsDirty = true
            });

            NewSubcategoryName = string.Empty;
            StatusText = $"Added '{name}', will be created on Apply.";
        }

        private void DeleteSubcategory()
        {
            if (SelectedSubcategory == null) return;

            if (SelectedSubcategory.IsNew)
            {
                Subcategories.Remove(SelectedSubcategory);
                StatusText = "Removed unsaved subcategory.";
            }
            else
            {
                SelectedSubcategory.PendingDelete = true;
                StatusText = $"'{SelectedSubcategory.Name}' marked for deletion on Apply.";
            }
        }

        private void EnsureCoreSubcategories()
        {
            int added = 0;
            foreach (var name in BaSubcategoryCatalog.Core)
            {
                if (Subcategories.Any(s =>
                        string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
                    continue;

                Subcategories.Add(new SubcategoryRow
                {
                    CategoryId = null,
                    Name = name,
                    LineWeight = 1,
                    LineColor = Colors.Black,
                    IsDirty = true
                });
                added++;
            }

            if (OwnerFamily?.FamilyCategory != null)
            {
                foreach (var name in BaSubcategoryCatalog.GetExtrasForFamilyCategory(
                    OwnerFamily.FamilyCategory))
                {
                    if (Subcategories.Any(s =>
                            string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    Subcategories.Add(new SubcategoryRow
                    {
                        CategoryId = null,
                        Name = name,
                        LineWeight = 1,
                        LineColor = Colors.Black,
                        IsDirty = true
                    });
                    added++;
                }
            }

            StatusText = added > 0
                ? $"Added {added} BA core subcategories, Apply to create in Revit."
                : "All core subcategories already present.";
        }

        private void AssignSubcategory()
        {
            if (TargetSubcategoryRow == null)
            {
                StatusText = "Select a target subcategory first.";
                return;
            }

            StatusText = $"Assignment staged: '{TargetSubcategoryRow.Name}' " +
                         $"to {SelectedScope}. Click Apply to execute.";
        }

        private void PickColor()
        {
            if (SelectedSubcategory == null || RequestColorPick == null) return;

            var picked = RequestColorPick(SelectedSubcategory.LineColor);
            if (picked.HasValue)
                SelectedSubcategory.LineColor = picked.Value;
        }

        // ── Apply, executes all pending changes in one transaction ───────────

        private void Apply()
        {
            ApplyLog.Clear();

            try
            {
                using var tx = new Transaction(Doc, "BA | Subcategory Manager");
                tx.Start();

                foreach (var row in Subcategories
                    .Where(r => r.PendingDelete && r.CategoryId != null)
                    .ToList())
                {
                    bool ok = SubcategoryService.DeleteSubcategory(
                        Doc, row.CategoryId!, ApplyLog);
                    if (ok) Subcategories.Remove(row);
                }

                foreach (var row in Subcategories.Where(r => r.IsNew).ToList())
                {
                    var created = SubcategoryService.CreateSubcategory(
                        Doc, ParentCategory, row.Name, ApplyLog);
                    if (created != null)
                    {
                        row.CategoryId = created.Id;
                        row.OriginalName = created.Name;
                    }
                }

                var existingMap = SubcategoryService.GetExistingSubcategories(ParentCategory);
                foreach (var row in Subcategories.Where(r => r.IsDirty && !r.PendingDelete).ToList())
                {
                    if (!existingMap.TryGetValue(row.OriginalName, out var cat))
                    {
                        ApplyLog.Add($"Could not resolve subcategory '{row.OriginalName}' for update.");
                        continue;
                    }

                    bool renameOk = true;

                    if (row.IsRenamed)
                    {
                        renameOk = SubcategoryService.RenameSubcategory(Doc, cat, row.Name, ApplyLog);
                        if (renameOk)
                            row.OriginalName = row.Name.Trim();
                    }

                    SubcategoryService.ApplyAppearance(Doc, cat, row, ApplyLog);

                    if (renameOk)
                        row.IsDirty = false;
                }

                if (TargetSubcategoryRow != null)
                {
                    var updatedMap = SubcategoryService.GetExistingSubcategories(ParentCategory);
                    if (updatedMap.TryGetValue(TargetSubcategoryRow.Name, out var targetCat))
                    {
                        GraphicsAssignmentService.ApplySubcategoryToFamilyGeometry(
                            Doc,
                            targetCat,
                            GeometryItems,
                            SelectedScope,
                            ApplyLog);
                    }
                    else
                    {
                        ApplyLog.Add($"Target subcategory '{TargetSubcategoryRow.Name}' not found after create step.");
                    }
                }

                tx.Commit();

                Applied = true;
                StatusText = "Applied successfully.";
                ApplyLog.Insert(0, "=== Apply completed ===");

                foreach (var row in GeometryItems)
                {
                    var e = Doc.GetElement(row.Id);
                    if (e != null)
                        row.SubcategoryName = GraphicsAssignmentService.GetSubcategoryName(Doc, e);
                }
            }
            catch (Exception ex)
            {
                ApplyLog.Add($"Transaction failed: {ex.Message}");
                StatusText = "Apply failed, see log.";
            }

            MessageBox.Show(
                string.Join(Environment.NewLine, ApplyLog),
                "BA Subcategory Manager, Result",
                MessageBoxButton.OK,
                Applied ? MessageBoxImage.Information : MessageBoxImage.Warning);

            if (Applied)
                RequestClose?.Invoke();
        }
    }
}
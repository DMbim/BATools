// File: BA.UI/Views/BASuperSelectorVm.cs
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Core.Selection;
using BA.Core.ViewFilters;
using BA.Settings;
using BA.UI.ExternalEvents;
using BA.UI.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace BA.UI.Views
{
    public sealed class BASuperSelectorVm : BA.UI.Mvvm.ObservableObject, IDisposable
    {
        private readonly UIApplication _uiApp;
        private readonly RevitExternalInvoker _revit;
        private readonly Window _window;
        private readonly SuperSelectorSettings _settings;

        public ObservableCollection<SuperSelectorCategoryItem> AllCategories { get; } = new();
        public ObservableCollection<SuperSelectorCategoryItem> FrequentCategories { get; } = new();
        public ObservableCollection<ParameterInfo> Parameters { get; } = new();
        public ObservableCollection<SuperSelectorFilterRow> Rows { get; } = new();

        private bool _useAnd = true;
        public bool UseAnd
        {
            get => _useAnd;
            set
            {
                if (!value) return;
                if (_useAnd) return;
                _useAnd = true;
                _useOr = false;
                OnPropertyChanged(nameof(UseAnd));
                OnPropertyChanged(nameof(UseOr));
            }
        }

        private bool _useOr;
        public bool UseOr
        {
            get => _useOr;
            set
            {
                if (!value) return;
                if (_useOr) return;
                _useOr = true;
                _useAnd = false;
                OnPropertyChanged(nameof(UseAnd));
                OnPropertyChanged(nameof(UseOr));
            }
        }

        private string _statusText = "Loading categories...";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public BA.UI.Mvvm.RelayCommand MoveToFrequentCommand { get; }
        public BA.UI.Mvvm.RelayCommand MoveToAllCommand { get; }
        public BA.UI.Mvvm.RelayCommand ToggleFrequentCommand { get; }
        public BA.UI.Mvvm.RelayCommand AddRowCommand { get; }
        public BA.UI.Mvvm.RelayCommand RemoveRowCommand { get; }
        public BA.UI.Mvvm.RelayCommand RunCommand { get; }
        public BA.UI.Mvvm.RelayCommand CloseCommand { get; }

        public BASuperSelectorVm(UIApplication uiApp, RevitExternalInvoker revit, Window window)
        {
            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _revit = revit ?? throw new ArgumentNullException(nameof(revit));
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _settings = SuperSelectorSettings.Load<SuperSelectorSettings>();

            MoveToFrequentCommand = new BA.UI.Mvvm.RelayCommand(_ => MoveSelectedToFrequent(), _ => AllCategories.Any(c => c.IsSelectedForMove));
            MoveToAllCommand = new BA.UI.Mvvm.RelayCommand(p => MoveToAll(p as SuperSelectorCategoryItem), p => p is SuperSelectorCategoryItem);
            ToggleFrequentCommand = new BA.UI.Mvvm.RelayCommand(p => ToggleFrequent(p as SuperSelectorCategoryItem), p => p is SuperSelectorCategoryItem);
            AddRowCommand = new BA.UI.Mvvm.RelayCommand(_ => AddRow(), _ => Parameters.Count > 0);
            RemoveRowCommand = new BA.UI.Mvvm.RelayCommand(p => RemoveRow(p as SuperSelectorFilterRow), p => p is SuperSelectorFilterRow);
            RunCommand = new BA.UI.Mvvm.RelayCommand(_ => RunSelection(), _ => FrequentCategories.Any(c => c.IsChecked));
            CloseCommand = new BA.UI.Mvvm.RelayCommand(_ => _window.Close());
        }

        public void EnsureCategoriesLoaded()
        {
            if (AllCategories.Count == 0 && FrequentCategories.Count == 0)
                LoadCategories();
        }

        public void Dispose()
        {
            SaveSettings();
        }

        private void SaveSettings()
        {
            _settings.FrequentCategoryNames = FrequentCategories.Select(c => c.Name).ToList();
            _settings.CheckedCategoryNames = FrequentCategories.Where(c => c.IsChecked).Select(c => c.Name).ToList();
            _settings.Save();
        }
        private void LoadCategories()
        {
            StatusText = "Loading categories...";

            _revit.Run(
                app =>
                {
                    var doc = app.ActiveUIDocument?.Document;
                    return ParameterEnumerationService.GetAllSelectableCategories(doc);
                },
                cats =>
                {
                    AllCategories.Clear();
                    FrequentCategories.Clear();

                    var byName = new Dictionary<string, SuperSelectorCategoryItem>(StringComparer.OrdinalIgnoreCase);
                    foreach (var c in cats)
                    {
                        var item = new SuperSelectorCategoryItem(c.Id, c.Name);
                        item.SelectionForMoveChanged += OnCategorySelectionForMoveChanged;
                        byName[c.Name] = item;
                        AllCategories.Add(item);
                    }

                    // Restore the saved Frequent shortlist in its saved
                    // order, and which of those were marked for use. A
                    // saved name that no longer exists in this document
                    // (renamed/removed category) is silently skipped rather
                    // than failing the whole restore.
                    foreach (var name in _settings.FrequentCategoryNames)
                    {
                        if (!byName.TryGetValue(name, out var item)) continue;
                        AllCategories.Remove(item);
                        item.IsChecked = _settings.CheckedCategoryNames.Contains(name, StringComparer.OrdinalIgnoreCase);
                        FrequentCategories.Add(item);
                    }

                    StatusText = $"Loaded {AllCategories.Count + FrequentCategories.Count} categories.";
                    RunCommand?.RaiseCanExecuteChanged();

                    if (FrequentCategories.Any(c => c.IsChecked))
                        RefreshParameters();
                },
                ex => StatusText = "Category load failed: " + ex.Message
            );
        }
        private void OnCategorySelectionForMoveChanged(object sender, EventArgs e)
        {
            MoveToFrequentCommand?.RaiseCanExecuteChanged();
        }
        private void MoveSelectedToFrequent()
        {
            var selected = AllCategories.Where(c => c.IsSelectedForMove).ToList();
            if (selected.Count == 0) return;

            foreach (var item in selected)
            {
                item.IsSelectedForMove = false;
                AllCategories.Remove(item);
                FrequentCategories.Add(item);
            }

            StatusText = $"Moved {selected.Count} categor{(selected.Count == 1 ? "y" : "ies")} to the frequent list.";
        }

        private void MoveToAll(SuperSelectorCategoryItem item)
        {
            if (item == null) return;

            bool wasChecked = item.IsChecked;
            item.IsChecked = false;
            FrequentCategories.Remove(item);

            var insertAt = 0;
            while (insertAt < AllCategories.Count &&
                   string.Compare(AllCategories[insertAt].Name, item.Name, StringComparison.OrdinalIgnoreCase) < 0)
                insertAt++;
            AllCategories.Insert(insertAt, item);

            if (wasChecked)
            {
                RunCommand?.RaiseCanExecuteChanged();
                RefreshParameters();
            }
        }

        private void ToggleFrequent(SuperSelectorCategoryItem item)
        {
            if (item == null) return;
            item.IsChecked = !item.IsChecked;
            RunCommand?.RaiseCanExecuteChanged();
            RefreshParameters();
        }

        private void RefreshParameters()
        {
            var checkedIds = FrequentCategories.Where(c => c.IsChecked).Select(c => c.Id).ToList();

            Parameters.Clear();
            Rows.Clear(); // existing rows may reference parameters no longer common to the new category set

            AddRowCommand?.RaiseCanExecuteChanged();

            if (checkedIds.Count == 0)
            {
                StatusText = "Click a category on the right to mark it for use.";
                return;
            }

            StatusText = "Loading parameters common to the marked categories...";

            _revit.Run(
                app =>
                {
                    var doc = app.ActiveUIDocument?.Document;
                    return ParameterEnumerationService.GetCommonParametersForCategories(doc, checkedIds);
                },
                pars =>
                {
                    Parameters.Clear();
                    foreach (var p in pars)
                        Parameters.Add(p);

                    StatusText = pars.Length > 0
                        ? $"{pars.Length} parameter(s) common to the marked categories."
                        : "No parameters are common to the marked categories.";

                    AddRowCommand?.RaiseCanExecuteChanged();
                },
                ex => StatusText = "Parameter load failed: " + ex.Message
            );
        }

        private void AddRow()
        {
            if (Parameters.Count == 0) return;

            var row = new SuperSelectorFilterRow(Parameters)
            {
                SelectedParameter = Parameters.First()
            };
            Rows.Add(row);
        }

        private void RemoveRow(SuperSelectorFilterRow row)
        {
            if (row == null) return;
            Rows.Remove(row);
        }

        private void RunSelection()
        {
            var checkedCategoryIds = FrequentCategories.Where(c => c.IsChecked).Select(c => c.Id).ToList();
            if (checkedCategoryIds.Count == 0)
            {
                StatusText = "Mark at least one category for use first.";
                return;
            }

            var criteria = new List<SuperSelectorCriterion>();
            var skipped = new List<string>();

            foreach (var row in Rows)
            {
                var built = row.ToCriterion();
                if (built == null)
                    skipped.Add(row.SelectedParameter?.Name ?? "(no parameter)");
                else
                    criteria.Add(built);
            }

            var logic = UseOr ? SuperSelectorLogic.Or : SuperSelectorLogic.And;

            StatusText = skipped.Count > 0
                ? $"Skipped {skipped.Count} incomplete row(s): {string.Join(", ", skipped)}. Waiting for pick..."
                : "Waiting for pick in the model. Box select or click elements, press Escape when done.";

            _revit.Run(
                app =>
                {
                    var uidoc = app.ActiveUIDocument;
                    var doc = uidoc?.Document;
                    if (uidoc == null || doc == null) return -2;

                    var pickFilter = new SuperSelectorPickFilter(doc, checkedCategoryIds, criteria, logic);

                    IList<Reference> picked;
                    try
                    {
                        picked = uidoc.Selection.PickObjects(
                            Autodesk.Revit.UI.Selection.ObjectType.Element,
                            pickFilter,
                            "Box select or click elements matching the criteria, press Escape when done");
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        return -1; // Escape with nothing picked, not an error
                    }

                    var ids = picked.Select(r => r.ElementId).ToList();
                    uidoc.Selection.SetElementIds(ids);
                    return ids.Count;
                },
                count =>
                {
                    StatusText = count switch
                    {
                        -2 => "No active document.",
                        -1 => "Selection canceled.",
                        _ => $"Selected {count} element(s) matching the criteria."
                    };
                },
                ex => StatusText = "Selection failed: " + ex.Message
            );
        }
    }

    public sealed class SuperSelectorCategoryItem : BA.UI.Mvvm.ObservableObject
    {
        public ElementId Id { get; }
        public string Name { get; }

        public event EventHandler CheckedChanged;

        // Tracks left-pane multi-select for the "move to frequent" arrow.
        // Two-way bound to ListBoxItem.IsSelected in the item container
        // style, since ListBox.SelectedItems has no native two-way binding.
        public event EventHandler SelectionForMoveChanged;

        private bool _isSelectedForMove;
        public bool IsSelectedForMove
        {
            get => _isSelectedForMove;
            set
            {
                if (SetProperty(ref _isSelectedForMove, value))
                    SelectionForMoveChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        // "Marked for use" - only meaningful once the item is in the
        // frequent list. Drives the actual category filter set.
        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (SetProperty(ref _isChecked, value))
                    CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public SuperSelectorCategoryItem(ElementId id, string name)
        {
            Id = id;
            Name = name;
        }

        public override string ToString() => Name;
    }

    public sealed class SuperSelectorFilterRow : BA.UI.Mvvm.ObservableObject
    {
        public ObservableCollection<ParameterInfo> AvailableParameters { get; }

        public IEnumerable<SuperSelectorModifier> AvailableModifiers { get; } =
            (SuperSelectorModifier[])Enum.GetValues(typeof(SuperSelectorModifier));

        private ParameterInfo _selectedParameter;
        public ParameterInfo SelectedParameter
        {
            get => _selectedParameter;
            set
            {
                if (!SetProperty(ref _selectedParameter, value)) return;
                OnPropertyChanged(nameof(IsNumericParameter));
                OnPropertyChanged(nameof(IsStringParameter));

                if (!IsModifierApplicable(SelectedModifier))
                    SelectedModifier = SuperSelectorModifier.HasValue;
            }
        }

        private SuperSelectorModifier _selectedModifier = SuperSelectorModifier.HasValue;
        public SuperSelectorModifier SelectedModifier
        {
            get => _selectedModifier;
            set
            {
                if (!SetProperty(ref _selectedModifier, value)) return;
                OnPropertyChanged(nameof(NeedsSingleValue));
                OnPropertyChanged(nameof(NeedsRangeValues));
                OnPropertyChanged(nameof(NeedsListValue));
            }
        }

        private string _valueA = string.Empty;
        public string ValueA { get => _valueA; set => SetProperty(ref _valueA, value); }

        private string _valueB = string.Empty;
        public string ValueB { get => _valueB; set => SetProperty(ref _valueB, value); }

        private string _valueList = string.Empty;
        public string ValueList { get => _valueList; set => SetProperty(ref _valueList, value); }

        public bool IsNumericParameter =>
            SelectedParameter != null &&
            (SelectedParameter.StorageType == StorageType.Double || SelectedParameter.StorageType == StorageType.Integer);

        public bool IsStringParameter => SelectedParameter != null && !IsNumericParameter;

        public bool NeedsSingleValue =>
            SelectedModifier != SuperSelectorModifier.HasValue &&
            SelectedModifier != SuperSelectorModifier.NoValue &&
            SelectedModifier != SuperSelectorModifier.Between &&
            SelectedModifier != SuperSelectorModifier.IsOneOf;

        public bool NeedsRangeValues => SelectedModifier == SuperSelectorModifier.Between;

        public bool NeedsListValue => SelectedModifier == SuperSelectorModifier.IsOneOf;

        public SuperSelectorFilterRow(ObservableCollection<ParameterInfo> availableParameters)
        {
            AvailableParameters = availableParameters;
        }

        public bool IsModifierApplicable(SuperSelectorModifier modifier)
        {
            if (SelectedParameter == null) return false;

            bool numericOnly = modifier == SuperSelectorModifier.GreaterThan
                || modifier == SuperSelectorModifier.LessThan
                || modifier == SuperSelectorModifier.GreaterThanOrEqual
                || modifier == SuperSelectorModifier.LessThanOrEqual
                || modifier == SuperSelectorModifier.Between;

            return numericOnly ? IsNumericParameter : true;
        }

        public SuperSelectorCriterion ToCriterion()
        {
            if (SelectedParameter == null) return null;
            if (!IsModifierApplicable(SelectedModifier)) return null;

            var criterion = new SuperSelectorCriterion
            {
                ParameterId = SelectedParameter.Id,
                ParameterName = SelectedParameter.Name,
                StorageType = SelectedParameter.StorageType,
                IsInstance = SelectedParameter.IsInstance,
                Modifier = SelectedModifier
            };

            switch (SelectedModifier)
            {
                case SuperSelectorModifier.HasValue:
                case SuperSelectorModifier.NoValue:
                    return criterion;

                case SuperSelectorModifier.Between:
                    if (string.IsNullOrWhiteSpace(ValueA) || string.IsNullOrWhiteSpace(ValueB)) return null;
                    criterion.ValueA = ValueA.Trim();
                    criterion.ValueB = ValueB.Trim();
                    return criterion;

                case SuperSelectorModifier.IsOneOf:
                    if (string.IsNullOrWhiteSpace(ValueList)) return null;
                    criterion.ValueList = ValueList
                        .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(v => v.Trim())
                        .Where(v => v.Length > 0)
                        .ToList();
                    return criterion.ValueList.Count == 0 ? null : criterion;

                default:
                    if (string.IsNullOrWhiteSpace(ValueA)) return null;
                    criterion.ValueA = ValueA.Trim();
                    return criterion;
            }
        }
    }

    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is bool b && b) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
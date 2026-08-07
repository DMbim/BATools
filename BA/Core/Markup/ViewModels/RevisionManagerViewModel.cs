// BA/Markup/ViewModels/RevisionManagerViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using BA.Markup.Models;

namespace BA.Markup.ViewModels
{
    public sealed class RevisionManagerViewModel : INotifyPropertyChanged
    {
        // ------------------------------------------------------------------ //
        //  INotifyPropertyChanged
        // ------------------------------------------------------------------ //
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }

        // ------------------------------------------------------------------ //
        //  Revit API delegates, synchronous, called on the API thread
        // ------------------------------------------------------------------ //
        private readonly Func<IReadOnlyList<RevisionItem>> _loadRevisions;
        private readonly Func<RevisionEditModel, RevisionItem> _saveRevision;
        private readonly Func<RevisionEditModel, RevisionItem> _createRevision;

        // ------------------------------------------------------------------ //
        //  Construction
        // ------------------------------------------------------------------ //
        public RevisionManagerViewModel(
            IEnumerable<RevisionItem> initialRevisions,
            Func<IReadOnlyList<RevisionItem>> loadRevisions,
            Func<RevisionEditModel, RevisionItem> saveRevision,
            Func<RevisionEditModel, RevisionItem> createRevision)
        {
            _loadRevisions = loadRevisions ?? throw new ArgumentNullException(nameof(loadRevisions));
            _saveRevision = saveRevision ?? throw new ArgumentNullException(nameof(saveRevision));
            _createRevision = createRevision ?? throw new ArgumentNullException(nameof(createRevision));

            foreach (var r in initialRevisions)
                _masterList.Add(r);

            foreach (var f in RevisionItem.FilterableFields)
            {
                FilterFields.Add(f);
                GroupFields.Add(f);
            }

            // Set backing fields directly, avoids triggering
            // RebuildView before commands are constructed.
            _selectedFilterField = FilterFields.Count > 0 ? FilterFields[0] : string.Empty;
            _selectedGroupField = GroupFields.Count > 0 ? GroupFields[0] : string.Empty;

            // Build commands before calling RebuildView.
            RefreshCommand = new BA.Core.Mvvm.RelayCommand(_ => ExecuteRefresh());
            EditCommand = new BA.Core.Mvvm.RelayCommand(_ => ExecuteEdit(),
                                    _ => SelectedRevision != null);
            NewCommand = new BA.Core.Mvvm.RelayCommand(_ => ExecuteNew());
            SaveCommand = new BA.Core.Mvvm.RelayCommand(_ => ExecuteSave(),
                                    _ => EditorViewModel?.CanSave == true);
            CancelEditCommand = new BA.Core.Mvvm.RelayCommand(_ => ExecuteCancelEdit());
            SelectCommand = new BA.Core.Mvvm.RelayCommand(_ => ExecuteSelect(),
                                    _ => SelectedRevision != null);

            RebuildView();
        }

        // ------------------------------------------------------------------ //
        //  Master list + filtered display list
        // ------------------------------------------------------------------ //
        private readonly List<RevisionItem> _masterList = new();

        public ObservableCollection<RevisionItem> FilteredRevisions { get; } = new();

        private void RebuildView()
        {
            var filter = _filterText.Trim();
            var field = _selectedFilterField;

            FilteredRevisions.Clear();

            foreach (var item in _masterList)
            {
                if (string.IsNullOrEmpty(filter))
                {
                    FilteredRevisions.Add(item);
                }
                else
                {
                    var val = item.GetFieldValue(field);
                    if (val.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                        FilteredRevisions.Add(item);
                }
            }

            if (_groupingEnabled && !string.IsNullOrEmpty(_selectedGroupField))
            {
                var groupField = _selectedGroupField;
                var sorted = new List<RevisionItem>(FilteredRevisions);
                sorted.Sort((a, b) => string.Compare(
                    a.GetFieldValue(groupField),
                    b.GetFieldValue(groupField),
                    StringComparison.OrdinalIgnoreCase));

                FilteredRevisions.Clear();
                foreach (var item in sorted)
                    FilteredRevisions.Add(item);
            }
        }

        // ------------------------------------------------------------------ //
        //  Selection
        // ------------------------------------------------------------------ //
        private RevisionItem? _selectedRevision;
        public RevisionItem? SelectedRevision
        {
            get => _selectedRevision;
            set
            {
                if (Set(ref _selectedRevision, value))
                    RaiseCommandsCanExecuteChanged();
            }
        }

        // ------------------------------------------------------------------ //
        //  Filter
        // ------------------------------------------------------------------ //
        public ObservableCollection<string> FilterFields { get; } = new();

        private string _selectedFilterField = string.Empty;
        public string SelectedFilterField
        {
            get => _selectedFilterField;
            set
            {
                if (Set(ref _selectedFilterField, value ?? string.Empty))
                    RebuildView();
            }
        }

        private string _filterText = string.Empty;
        public string FilterText
        {
            get => _filterText;
            set
            {
                if (Set(ref _filterText, value ?? string.Empty))
                    RebuildView();
            }
        }

        // ------------------------------------------------------------------ //
        //  Grouping
        // ------------------------------------------------------------------ //
        public ObservableCollection<string> GroupFields { get; } = new();

        private string _selectedGroupField = string.Empty;
        public string SelectedGroupField
        {
            get => _selectedGroupField;
            set
            {
                if (Set(ref _selectedGroupField, value ?? string.Empty))
                    RebuildView();
            }
        }

        private bool _groupingEnabled;
        public bool GroupingEnabled
        {
            get => _groupingEnabled;
            set
            {
                if (Set(ref _groupingEnabled, value))
                    RebuildView();
            }
        }

        // ------------------------------------------------------------------ //
        //  Editor panel
        // ------------------------------------------------------------------ //
        private RevisionEditorViewModel? _editorViewModel;
        public RevisionEditorViewModel? EditorViewModel
        {
            get => _editorViewModel;
            private set
            {
                if (_editorViewModel != null)
                    _editorViewModel.PropertyChanged -= OnEditorPropertyChanged;

                Set(ref _editorViewModel, value);
                OnPropertyChanged(nameof(IsEditorVisible));

                if (value != null)
                    value.PropertyChanged += OnEditorPropertyChanged;

                RaiseCommandsCanExecuteChanged();
            }
        }

        public bool IsEditorVisible => _editorViewModel != null;

        private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RevisionEditorViewModel.CanSave))
                RaiseCommandsCanExecuteChanged();
        }

        // ------------------------------------------------------------------ //
        //  Status
        // ------------------------------------------------------------------ //
        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            private set => Set(ref _statusMessage, value ?? string.Empty);
        }

        // ------------------------------------------------------------------ //
        //  Dialog result
        // ------------------------------------------------------------------ //
        public RevisionItem? SelectedResult { get; private set; }
        public event EventHandler? CloseRequested;

        // ------------------------------------------------------------------ //
        //  Commands
        // ------------------------------------------------------------------ //
        public ICommand RefreshCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand NewCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelEditCommand { get; }
        public ICommand SelectCommand { get; }

        // ------------------------------------------------------------------ //
        //  Command implementations
        // ------------------------------------------------------------------ //

        private void ExecuteRefresh()
        {
            StatusMessage = "Loading...";
            try
            {
                var items = _loadRevisions();
                _masterList.Clear();
                foreach (var r in items)
                    _masterList.Add(r);
                RebuildView();
                StatusMessage = $"{_masterList.Count} revision(s).";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                BA.BAApplication.AppLogger.LogError("RevisionManagerViewModel.ExecuteRefresh", ex);
            }
        }

        // <- INSTRUMENTED: was uncaught. An exception here previously propagated
        //    straight into the WPF command invocation with no handler, which is
        //    consistent with the observed silent failure followed by process exit.
        private void ExecuteEdit()
        {
            if (SelectedRevision == null) return;

            try
            {
                EditorViewModel = RevisionEditorViewModel.ForEdit(SelectedRevision);
                StatusMessage = string.Empty;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error opening editor: {ex.Message}";
                BA.BAApplication.AppLogger.LogError("RevisionManagerViewModel.ExecuteEdit", ex);
                Autodesk.Revit.UI.TaskDialog.Show(
                    "BA Revision Manager - Edit Error",
                    ex.ToString());
            }
        }

        // <- INSTRUMENTED: same treatment as ExecuteEdit.
        private void ExecuteNew()
        {
            try
            {
                EditorViewModel = RevisionEditorViewModel.ForCreate();
                StatusMessage = string.Empty;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error opening editor: {ex.Message}";
                BA.BAApplication.AppLogger.LogError("RevisionManagerViewModel.ExecuteNew", ex);
                Autodesk.Revit.UI.TaskDialog.Show(
                    "BA Revision Manager - New Error",
                    ex.ToString());
            }
        }

        private void ExecuteSave()
        {
            if (EditorViewModel == null || !EditorViewModel.CanSave) return;

            var model = EditorViewModel.BuildEditModel();
            StatusMessage = "Saving...";

            try
            {
                if (model.IsNew)
                {
                    var created = _createRevision(model);
                    _masterList.Add(created);
                    RebuildView();
                    SelectedRevision = created;
                    StatusMessage = "Revision created.";
                }
                else
                {
                    var updated = _saveRevision(model);
                    for (int i = 0; i < _masterList.Count; i++)
                    {
                        if (_masterList[i].ElementId == updated.ElementId)
                        {
                            _masterList[i] = updated;
                            break;
                        }
                    }
                    RebuildView();
                    SelectedRevision = updated;
                    StatusMessage = "Revision saved.";
                }

                EditorViewModel = null;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                BA.BAApplication.AppLogger.LogError("RevisionManagerViewModel.ExecuteSave", ex);
            }
        }

        public void Initialize()
        {
            RebuildView();
        }

        private void ExecuteCancelEdit()
        {
            EditorViewModel = null;
            StatusMessage = string.Empty;
        }

        private void ExecuteSelect()
        {
            if (SelectedRevision == null) return;
            SelectedResult = SelectedRevision;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void RaiseCommandsCanExecuteChanged()
        {
            ((BA.Core.Mvvm.RelayCommand)EditCommand).RaiseCanExecuteChanged();
            ((BA.Core.Mvvm.RelayCommand)NewCommand).RaiseCanExecuteChanged();
            ((BA.Core.Mvvm.RelayCommand)SelectCommand).RaiseCanExecuteChanged();
            ((BA.Core.Mvvm.RelayCommand)SaveCommand).RaiseCanExecuteChanged();
        }

        // ------------------------------------------------------------------ //
        //  Source collection accessor for PlaceMarkupCommand
        // ------------------------------------------------------------------ //
        public IReadOnlyList<RevisionItem> GetAllRevisions()
            => _masterList.AsReadOnly();
    }

    internal sealed class RevisionGroupKeyConverter : System.Windows.Data.IValueConverter
    {
        private readonly string _fieldName;

        public RevisionGroupKeyConverter(string fieldName)
        {
            _fieldName = fieldName;
        }

        public object Convert(
            object value, Type targetType,
            object parameter, System.Globalization.CultureInfo culture)
        {
            if (_fieldName.Equals("Issued", StringComparison.OrdinalIgnoreCase)
                && value is bool b)
                return b ? "Issued" : "Not Issued";

            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(
            object value, Type targetType,
            object parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }
}
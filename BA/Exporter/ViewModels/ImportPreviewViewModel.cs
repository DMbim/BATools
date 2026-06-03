using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BA_Tools.ScheduleExporter.Models;

namespace BA_Tools.ScheduleExporter.ViewModels
{
    /// <summary>
    /// ViewModel for the import preview dialog.
    /// Shows compare statistics, type parameter warnings, and deleted element IDs.
    /// The user must explicitly confirm before any writes occur.
    /// </summary>
    public partial class ImportPreviewViewModel : ObservableObject
    {
        public ImportCompareResult CompareResult { get; }

        // ─── Summary stats ────────────────────────────────────────────────────

        public int TotalRows          => CompareResult.TotalRows;
        public int ChangedRowCount    => CompareResult.ChangedRowCount;
        public int UnchangedRowCount  => CompareResult.UnchangedRowCount;
        public int SkippedCount       => CompareResult.SkippedCount;
        public int DeletedCount       => CompareResult.DeletedElementIds.Count;
        public int InvalidCount       => CompareResult.InvalidValueCount;

        public bool HasChanges             => ChangedRowCount > 0;
        public bool HasNoChanges           => !HasChanges;
        public bool HasTypeParameterWarnings => TypeParameterWarnings.Any();
        public bool HasDeletedElements     => CompareResult.DeletedElementIds.Any();
        public bool HasConflicts           => TypeParameterWarnings.Any(w => w.HasConflict);

        public bool CanConfirm => HasChanges; // Nothing to write if no changes

        // ─── Collections ─────────────────────────────────────────────────────

        public ObservableCollection<TypeParameterWarningViewModel> TypeParameterWarnings { get; }
        public ObservableCollection<long> DeletedElementIds { get; }

        // ─── Outcome ─────────────────────────────────────────────────────────

        public bool UserConfirmed { get; private set; }
        public event Action CloseRequested;

        // ─── Constructor ─────────────────────────────────────────────────────

        public ImportPreviewViewModel(ImportCompareResult compareResult)
        {
            CompareResult = compareResult ?? throw new ArgumentNullException(nameof(compareResult));

            TypeParameterWarnings = new ObservableCollection<TypeParameterWarningViewModel>(
                compareResult.TypeParameterWarnings
                    .OrderBy(w => w.ElementTypeName)
                    .ThenBy(w => w.ParameterName)
                    .Select(w => new TypeParameterWarningViewModel(w)));

            DeletedElementIds = new ObservableCollection<long>(compareResult.DeletedElementIds);
        }

        // ─── Commands ────────────────────────────────────────────────────────

        [RelayCommand(CanExecute = nameof(CanConfirm))]
        private void Confirm()
        {
            UserConfirmed = true;
            CloseRequested?.Invoke();
        }

        [RelayCommand]
        private void Cancel()
        {
            UserConfirmed = false;
            CloseRequested?.Invoke();
        }
    }

    /// <summary>
    /// Row ViewModel for one (typeId, parameterId) type parameter warning entry.
    /// </summary>
    public partial class TypeParameterWarningViewModel : ObservableObject
    {
        public string ParameterName       { get; }
        public string CurrentValue        { get; }
        public string NewValue            { get; }
        public string ElementTypeName     { get; }
        public int    AffectedInstanceCount { get; }
        public bool   HasConflict         { get; }
        public string InstanceLabel       { get; }

        /// <summary>Formatted summary of conflicting values, shown in red when HasConflict is true.</summary>
        public string ConflictSummary { get; }

        /// <summary>Arrow label: "CurrentValue → NewValue" or conflict indicator.</summary>
        public string ValueChangeLabel { get; }

        [ObservableProperty]
        private bool _isExpanded;

        public TypeParameterWarningViewModel(TypeParameterWarning warning)
        {
            ParameterName         = warning.ParameterName;
            CurrentValue          = warning.CurrentValue;
            NewValue              = warning.NewValue;
            ElementTypeName       = warning.ElementTypeName;
            AffectedInstanceCount = warning.AffectedInstanceCount;
            HasConflict           = warning.HasConflict;

            InstanceLabel = AffectedInstanceCount == 1
                ? "1 instance in model"
                : $"{AffectedInstanceCount} instances in model";

            if (HasConflict)
            {
                int n = warning.ConflictingValues.Count;
                string preview = string.Join(", ", warning.ConflictingValues.Take(3));
                if (n > 3) preview += $", ... ({n - 3} more)";
                ConflictSummary  = $"CONFLICT: {n} different values found for this type — {preview}. Last-write-wins will apply.";
                ValueChangeLabel = $"\"{CurrentValue}\" → [conflict — last-write-wins: \"{warning.ConflictingValues.Last()}\"]";
            }
            else
            {
                ConflictSummary  = null;
                ValueChangeLabel = $"\"{CurrentValue}\" → \"{NewValue}\"";
            }
        }
    }
}

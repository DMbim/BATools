using BA.QA.FamilyVersioning.Engine;
using BA.QA.FamilyVersioning.Hook;
using BA.QA.FamilyVersioning.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace BA.QA.FamilyVersioning.ViewModels
{
    public partial class FamilyVersioningConfirmViewModel : ObservableObject
    {
        private readonly PendingDetection _detection;

        // ─── Display properties ────────────────────────────────────────────────

        public string FamilyName => _detection.FamilyName;
        public string CategoryName => _detection.CategoryName;
        public string BuildingName { get; }
        public string DiffSummary => _detection.Diff.ToSummaryString();
        public string InferredBumpLabel => _detection.InferredBumpKind.ToString().ToUpper();
        public string CurrentVersion => _detection.CurrentCatalogVersion;
        public bool IsBreaking => _detection.InferredBumpKind == FamilyBumpKind.Major;

        // ─── Observable properties ─────────────────────────────────────────────

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
        private string _finalVersion;

        [ObservableProperty]
        private string _comment = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ExceptionReasonRequired))]
        private bool _markedAsException;

        /// <summary>
        /// True if the user has manually changed the version from the suggested one.
        /// </summary>
        public bool Overridden =>
            !string.Equals(_finalVersion, _detection.SuggestedVersion, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// When true, this detection will also update the canonical version in the
        /// Families table, designating this family state as the office standard.
        /// Typically only checked by a BIM lead.
        /// </summary>
        [ObservableProperty]
        private bool _setAsCanonical;

        public bool ExceptionReasonRequired => MarkedAsException;

        // ─── Dialog result ─────────────────────────────────────────────────────

        public bool UserConfirmed { get; private set; }
        public event Action? CloseRequested;

        // ─── Constructor ───────────────────────────────────────────────────────

        public FamilyVersioningConfirmViewModel(PendingDetection detection, string buildingName)
        {
            _detection = detection ?? throw new ArgumentNullException(nameof(detection));
            BuildingName = buildingName ?? throw new ArgumentNullException(nameof(buildingName));
            _finalVersion = detection.SuggestedVersion;
        }

        // ─── Commands ──────────────────────────────────────────────────────────

        private bool CanConfirm() =>
            !string.IsNullOrWhiteSpace(FinalVersion) &&
            IsValidVersion(FinalVersion);

        [RelayCommand(CanExecute = nameof(CanConfirm))]
        private void Confirm()
        {
            UserConfirmed = true;
            CloseRequested?.Invoke();
        }

        [RelayCommand]
        private void Dismiss()
        {
            UserConfirmed = false;
            CloseRequested?.Invoke();
        }

        // ─── Partial callbacks ─────────────────────────────────────────────────

        partial void OnFinalVersionChanged(string value)
        {
            OnPropertyChanged(nameof(Overridden));
        }

        // ─── Private helpers ───────────────────────────────────────────────────

        private static bool IsValidVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) return false;
            var parts = version.Split('.');
            if (parts.Length != 3) return false;
            return int.TryParse(parts[0], out _) &&
                   int.TryParse(parts[1], out _) &&
                   int.TryParse(parts[2], out _);
        }
    }
}

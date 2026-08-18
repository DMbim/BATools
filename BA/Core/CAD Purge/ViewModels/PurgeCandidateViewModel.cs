// File: BA_Tools/CadPurge/ViewModels/PurgeCandidateViewModel.cs
using System;
using System.Collections.Generic;
using BA.CadPurge.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BA.CadPurge.ViewModels
{
    /// <summary>
    /// Binding wrapper around a single PurgeCandidate. The underlying Model is mutated directly
    /// by PurgeScanService (at scan time) and PurgeBatchExecutor (at apply time), neither of which
    /// knows this ViewModel exists. Call RefreshFromModel() after any operation that mutates Model
    /// outside of this class's own property setters.
    /// </summary>
    public sealed class PurgeCandidateViewModel : ObservableObject
    {
        private static readonly IReadOnlyList<PurgeAction> ActionsWithMapping =
            new[] { PurgeAction.None, PurgeAction.Delete, PurgeAction.MapToStandard };

        private static readonly IReadOnlyList<PurgeAction> ActionsWithoutMapping =
            new[] { PurgeAction.None, PurgeAction.Delete };

        public PurgeCandidate Model { get; }

        public PurgeItemType ItemType => Model.ItemType;
        public string Name => Model.Name;
        public int UsageCount => Model.UsageCount;
        public string ProposedTargetName => Model.ResolvedRule?.TargetName;
        public bool HasProposedMapping => Model.ResolvedRule != null;

        /// <summary>
        /// The set of actions valid for this specific candidate. MapToStandard is only included
        /// when a rule matched at scan time, so the UI never offers a choice the ViewModel would
        /// silently reject.
        /// </summary>
        public IReadOnlyList<PurgeAction> AvailableActions => HasProposedMapping ? ActionsWithMapping : ActionsWithoutMapping;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        private PurgeAction _selectedAction = PurgeAction.None;

        /// <summary>
        /// The action this candidate will receive when the batch is applied. Setting this to
        /// MapToStandard is silently ignored if HasProposedMapping is false, since there is no
        /// rule to map against. AvailableActions keeps the UI from offering that choice in the
        /// first place; this is a defensive second guard, not a substitute for it.
        /// </summary>
        public PurgeAction SelectedAction
        {
            get => _selectedAction;
            set
            {
                if (value == PurgeAction.MapToStandard && !HasProposedMapping)
                    return;

                if (SetProperty(ref _selectedAction, value))
                    OnPropertyChanged(nameof(StatusDisplay));

                Model.RequestedAction = value;
            }
        }

        private PurgeCandidateStatus _status;
        public PurgeCandidateStatus Status
        {
            get => _status;
            private set => SetProperty(ref _status, value);
        }

        private string _statusDetail;
        public string StatusDetail
        {
            get => _statusDetail;
            private set => SetProperty(ref _statusDetail, value);
        }

        public string StatusDisplay
        {
            get
            {
                if (Status == PurgeCandidateStatus.ActionApplied) return "Applied";
                if (Status == PurgeCandidateStatus.ActionFailed) return "Failed";

                switch (SelectedAction)
                {
                    case PurgeAction.None: return "No action selected";
                    case PurgeAction.Delete: return "Will delete";
                    case PurgeAction.MapToStandard:
                        return HasProposedMapping ? $"Will map to '{ProposedTargetName}'" : "No mapping rule matched. Delete only.";
                    default: return "Unknown";
                }
            }
        }

        public PurgeCandidateViewModel(PurgeCandidate model)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            _status = model.Status;
            _statusDetail = model.StatusDetail;
        }

        /// <summary>
        /// Refreshes Status/StatusDetail/StatusDisplay from the underlying Model. Call this on
        /// every candidate after PurgeBatchExecutor.ExecuteBatch returns, since that executor
        /// works against plain PurgeCandidate objects and has no dependency on this WPF wrapper.
        /// </summary>
        public void RefreshFromModel()
        {
            Status = Model.Status;
            StatusDetail = Model.StatusDetail;
            OnPropertyChanged(nameof(StatusDisplay));
        }
    }
}
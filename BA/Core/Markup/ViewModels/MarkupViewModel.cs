// BA/Markup/ViewModels/MarkupViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using BA.Markup.Models;

namespace BA.Markup.ViewModels
{
    public sealed class MarkupViewModel : INotifyPropertyChanged
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
        //  Construction — single constructor, no duplicates
        // ------------------------------------------------------------------ //
        public MarkupViewModel(
            string currentUser,
            string currentDate,
            IEnumerable<RevisionItem> revisions,
            IEnumerable<string> activeAssigneeUsers,
            Action refreshRevisionsCallback)
        {
            _refreshRevisionsCallback = refreshRevisionsCallback
                ?? throw new ArgumentNullException(nameof(refreshRevisionsCallback));

            BaAuthor = currentUser ?? string.Empty;
            BaDate = currentDate ?? string.Empty;

            foreach (var r in revisions)
                Revisions.Add(r);

            foreach (var t in MarkupTypeOptions.All)
                TypeOptions.Add(t);

            foreach (var a in MarkupActionOptions.All)
                ActionOptions.Add(a);

            // <- NEW: assignee options, sourced from MarkupUserRegistryService.GetActiveUsers
            //    via PlaceMarkupCommand. Free text is always allowed regardless of this list,
            //    enforced in the XAML by using an editable ComboBox rather than a locked
            //    dropdown; this collection only supplies the suggestions.
            foreach (var u in activeAssigneeUsers ?? Array.Empty<string>())
                AssigneeOptions.Add(u);

            SelectedTypeOption = TypeOptions.Count > 0 ? TypeOptions[0] : string.Empty;
            SelectedActionOption = string.Empty;

            ConfirmCommand = new RelayCommand(ExecuteConfirm, CanConfirm);
            CancelCommand = new RelayCommand(ExecuteCancel);
            RefreshRevisionsCommand = new RelayCommand(ExecuteRefresh);
        }

        private readonly Action _refreshRevisionsCallback;

        // ------------------------------------------------------------------ //
        //  Static option lists
        // ------------------------------------------------------------------ //
        public ObservableCollection<string> TypeOptions { get; } = new();
        public ObservableCollection<string> ActionOptions { get; } = new();
        public ObservableCollection<RevisionItem> Revisions { get; } = new();

        // <- NEW: assignee suggestions for the editable ComboBox.
        public ObservableCollection<string> AssigneeOptions { get; } = new();

        // ------------------------------------------------------------------ //
        //  Mode
        // ------------------------------------------------------------------ //
        private MarkupMode _mode = MarkupMode.InternalComment;
        public MarkupMode Mode
        {
            get => _mode;
            set
            {
                if (Set(ref _mode, value))
                {
                    OnPropertyChanged(nameof(IsInternalMode));
                    OnPropertyChanged(nameof(IsOfficialMode));
                    ((RelayCommand)ConfirmCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsInternalMode
        {
            get => Mode == MarkupMode.InternalComment;
            set { if (value) Mode = MarkupMode.InternalComment; }
        }

        public bool IsOfficialMode
        {
            get => Mode == MarkupMode.OfficialRevision;
            set { if (value) Mode = MarkupMode.OfficialRevision; }
        }

        // ------------------------------------------------------------------ //
        //  BA_Type
        // ------------------------------------------------------------------ //
        private string _selectedTypeOption = string.Empty;
        public string SelectedTypeOption
        {
            get => _selectedTypeOption;
            set => Set(ref _selectedTypeOption, value ?? string.Empty);
        }

        // ------------------------------------------------------------------ //
        //  BA_Comments
        // ------------------------------------------------------------------ //
        private string _selectedActionOption = string.Empty;
        public string SelectedActionOption
        {
            get => _selectedActionOption;
            set => Set(ref _selectedActionOption, value ?? string.Empty);
        }

        private string _commentFreeText = string.Empty;
        public string CommentFreeText
        {
            get => _commentFreeText;
            set => Set(ref _commentFreeText, value ?? string.Empty);
        }

        public string ResolvedComment
        {
            get
            {
                var action = SelectedActionOption.Trim();
                var text = CommentFreeText.Trim();
                if (action.Length > 0 && text.Length > 0) return $"{action}: {text}";
                if (text.Length > 0) return text;
                if (action.Length > 0) return action;
                return string.Empty;
            }
        }

        // ------------------------------------------------------------------ //
        //  Author / Date
        // ------------------------------------------------------------------ //
        private string _baAuthor = string.Empty;
        public string BaAuthor
        {
            get => _baAuthor;
            set => Set(ref _baAuthor, value ?? string.Empty);
        }

        private string _baDate = string.Empty;
        public string BaDate
        {
            get => _baDate;
            set => Set(ref _baDate, value ?? string.Empty);
        }

        // ------------------------------------------------------------------ //
        //  Assignee (BA_Tls_AssignedUser)
        // ------------------------------------------------------------------ //

        // <- NEW: bound to an editable ComboBox. Text is always accepted as typed;
        //    AssigneeOptions only supplies the dropdown suggestions, it never restricts
        //    what can be entered. Empty string is valid, means unassigned.
        private string _assignedUser = string.Empty;
        public string AssignedUser
        {
            get => _assignedUser;
            set => Set(ref _assignedUser, value ?? string.Empty);
        }

        // ------------------------------------------------------------------ //
        //  Revision (Official mode)
        // ------------------------------------------------------------------ //
        private RevisionItem? _selectedRevision;
        public RevisionItem? SelectedRevision
        {
            get => _selectedRevision;
            set
            {
                if (Set(ref _selectedRevision, value))
                    ((RelayCommand)ConfirmCommand).RaiseCanExecuteChanged();
            }
        }

        // ------------------------------------------------------------------ //
        //  Dialog result
        // ------------------------------------------------------------------ //
        public bool Confirmed { get; private set; }

        public MarkupInputModel BuildModel() => new()
        {
            Mode = Mode,
            BaType = SelectedTypeOption.Trim(),
            BaComments = ResolvedComment,
            BaAuthor = BaAuthor.Trim(),
            BaDate = BaDate.Trim(),
            RevisionElementId = SelectedRevision?.ElementId ?? -1,
            RevisionDisplayName = SelectedRevision?.DisplayName ?? string.Empty,
            AssignedUser = AssignedUser.Trim()
        };

        // ------------------------------------------------------------------ //
        //  Commands
        // ------------------------------------------------------------------ //
        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand RefreshRevisionsCommand { get; }

        private bool CanConfirm(object? _)
        {
            if (Mode == MarkupMode.OfficialRevision && SelectedRevision == null)
                return false;
            return true;
        }

        private void ExecuteConfirm(object? _)
        {
            Confirmed = true;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ExecuteCancel(object? _)
        {
            Confirmed = false;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ExecuteRefresh(object? _)
            => _refreshRevisionsCallback.Invoke();

        public event EventHandler? CloseRequested;

        public void UpdateRevisions(IEnumerable<RevisionItem> items)
        {
            var previously = _selectedRevision?.ElementId;
            Revisions.Clear();

            foreach (var r in items)
                Revisions.Add(r);

            SelectedRevision = null;
            if (previously.HasValue)
            {
                foreach (var r in Revisions)
                {
                    if (r.ElementId == previously.Value)
                    {
                        SelectedRevision = r;
                        break;
                    }
                }
            }
        }
    }

    // ---------------------------------------------------------------------- //
    //  RelayCommand
    // ---------------------------------------------------------------------- //
    internal sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public void RaiseCanExecuteChanged()
            => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object? parameter) => _execute(parameter);
    }
}
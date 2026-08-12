// BA/Markup/ViewModels/RevisionEditorViewModel.cs
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using BA.Markup.Models;

namespace BA.Markup.ViewModels
{
    /// <summary>
    /// Drives the inline editor panel inside RevisionManagerWindow.
    /// Populated from a RevisionItem (edit) or blank (create).
    /// Call BuildEditModel() to extract the DTO for the handler.
    /// </summary>
    public sealed class         RevisionEditorViewModel : INotifyPropertyChanged
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
        //  Construction
        // ------------------------------------------------------------------ //

        /// <summary>Loads an existing revision into the editor.</summary>
        public static RevisionEditorViewModel ForEdit(RevisionItem item)
        {
            var vm = new RevisionEditorViewModel
            {
                _isNew = false,
                _sequenceNumber = item.SequenceNumber,
                _revisionDate = item.RevisionDate,
                _description = item.Description,
                _issued = item.Issued,
                _issuedBy = item.IssuedBy,
                _issuedTo = item.IssuedTo,
                _sourceElementId = item.ElementId
            };
            return vm;
        }

        /// <summary>Prepares a blank editor for creating a new revision.</summary>
        public static RevisionEditorViewModel ForCreate()
        {
            var vm = new RevisionEditorViewModel
            {
                _isNew = true,
                _sequenceNumber = 0,
                _revisionDate = DateTime.Today.ToString("yyyy-MM-dd"),
                _description = string.Empty,
                _issued = false,
                _issuedBy = string.Empty,
                _issuedTo = string.Empty,
                _sourceElementId = -1
            };
            return vm;
        }

        private RevisionEditorViewModel() { }

        private int _sourceElementId;
        private bool _isNew;

        // ------------------------------------------------------------------ //
        //  Properties
        // ------------------------------------------------------------------ //

        /// <summary>
        /// True when this editor is in create mode.
        /// Exposed so the XAML can show "New Revision" vs "Edit Revision" header.
        /// </summary>
        public bool IsNew => _isNew;

        public string EditorTitle => _isNew ? "New Revision" : "Edit Revision";

        /// <summary>
        /// Read-only. Revit assigns sequence numbers internally.
        /// Shown as "Auto" in create mode.
        /// </summary>
        public string SequenceNumberDisplay =>
            _isNew ? "Auto" : _sequenceNumber.ToString();

        private int _sequenceNumber;

        private string _revisionDate = string.Empty;
        public string RevisionDate
        {
            get => _revisionDate;
            set
            {
                if (Set(ref _revisionDate, value ?? string.Empty))
                    ValidateDate();
            }
        }

        private string _description = string.Empty;
        public string Description
        {
            get => _description;
            set
            {
                if (Set(ref _description, value ?? string.Empty))
                    OnPropertyChanged(nameof(CanSave));
            }
        }

        private bool _issued;
        public bool Issued
        {
            get => _issued;
            set => Set(ref _issued, value);
        }

        private string _issuedBy = string.Empty;
        public string IssuedBy
        {
            get => _issuedBy;
            set => Set(ref _issuedBy, value ?? string.Empty);
        }

        private string _issuedTo = string.Empty;
        public string IssuedTo
        {
            get => _issuedTo;
            set => Set(ref _issuedTo, value ?? string.Empty);
        }

        // ------------------------------------------------------------------ //
        //  Validation
        // ------------------------------------------------------------------ //

        private string _dateError = string.Empty;
        public string DateError
        {
            get => _dateError;
            private set
            {
                if (Set(ref _dateError, value ?? string.Empty))
                    OnPropertyChanged(nameof(HasDateError));
                OnPropertyChanged(nameof(CanSave));
            }
        }

        public bool HasDateError => !string.IsNullOrEmpty(_dateError);

        /// <summary>
        /// Description is mandatory. Date must be parseable if provided.
        /// </summary>
        public bool CanSave =>
            !string.IsNullOrWhiteSpace(_description) && !HasDateError;

        private void ValidateDate()
        {
            if (string.IsNullOrWhiteSpace(_revisionDate))
            {
                DateError = string.Empty;
                return;
            }

            DateError = DateTime.TryParse(_revisionDate, out _)
                ? string.Empty
                : "Date format not recognised. Use yyMMdd.";
        }

        // ------------------------------------------------------------------ //
        //  Output
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Builds the DTO passed to RevisionManagerHandler.
        /// Only call when CanSave is true.
        /// </summary>
        public RevisionEditModel BuildEditModel() => new()
        {
            ElementId = _sourceElementId,
            RevisionDate = _revisionDate.Trim(),
            Description = _description.Trim(),
            Issued = _issued,
            IssuedBy = _issuedBy.Trim(),
            IssuedTo = _issuedTo.Trim()
        };
    }
}
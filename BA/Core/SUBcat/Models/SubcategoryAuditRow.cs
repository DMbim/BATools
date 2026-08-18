using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BA.Core.Standards
{
    public enum AuditRowStatus
    {
        Clean,
        Warning,
        Error,
        Skipped
    }

    public sealed class SubcategoryAuditRow : INotifyPropertyChanged
    {
        private long _familyId;
        private string _familyName = "";
        private string _categoryName = "";
        private string _statusText = "";
        private AuditRowStatus _status;
        private string _existingSubcategories = "";
        private string _validBaNames = "";
        private string _missingRequired = "";
        private string _allowedNonBaNames = "";
        private string _nonCompliantNames = "";
        private string _notes = "";

        public long FamilyId
        {
            get => _familyId;
            set { _familyId = value; OnPropertyChanged(); }
        }

        public string FamilyName
        {
            get => _familyName;
            set { _familyName = value ?? ""; OnPropertyChanged(); }
        }

        public string CategoryName
        {
            get => _categoryName;
            set { _categoryName = value ?? ""; OnPropertyChanged(); }
        }

        public AuditRowStatus Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged();
                StatusText = value.ToString();
            }
        }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value ?? ""; OnPropertyChanged(); }
        }

        public string ExistingSubcategories
        {
            get => _existingSubcategories;
            set { _existingSubcategories = value ?? ""; OnPropertyChanged(); }
        }

        public string ValidBaNames
        {
            get => _validBaNames;
            set { _validBaNames = value ?? ""; OnPropertyChanged(); }
        }

        public string MissingRequired
        {
            get => _missingRequired;
            set { _missingRequired = value ?? ""; OnPropertyChanged(); }
        }

        public string AllowedNonBaNames
        {
            get => _allowedNonBaNames;
            set { _allowedNonBaNames = value ?? ""; OnPropertyChanged(); }
        }

        public string NonCompliantNames
        {
            get => _nonCompliantNames;
            set { _nonCompliantNames = value ?? ""; OnPropertyChanged(); }
        }

        public string Notes
        {
            get => _notes;
            set { _notes = value ?? ""; OnPropertyChanged(); }
        }

        public bool HasIssues =>
            Status == AuditRowStatus.Warning || Status == AuditRowStatus.Error;

        public bool HasMissingRequired =>
            !string.IsNullOrWhiteSpace(MissingRequired);

        public bool HasNonCompliantNames =>
            !string.IsNullOrWhiteSpace(NonCompliantNames);

        public bool HasAnyBaNames =>
            !string.IsNullOrWhiteSpace(ValidBaNames);

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
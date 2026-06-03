using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BA.Families.Models
{
    public enum FamilySaveStatus
    {
        Pending,
        Saving,
        Saved,
        Skipped,
        Error
    }

    public class FamilyExportItem : ObservableObject
    {
        // Immutable identity
        public string Name { get; init; } = string.Empty;
        public string CategoryName { get; init; } = string.Empty;
        public ElementId FamilyId { get; init; } = ElementId.InvalidElementId;

        private bool _isSelected;
        private FamilySaveStatus _status = FamilySaveStatus.Pending;
        private string? _statusMessage;

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public FamilySaveStatus Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        public string? StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private string? _thumbnailViewName;

        /// <summary>
        /// Optional per-family thumbnail view name override.
        /// If null or empty, the global default from SaveFamiliesOptions is used.
        /// </summary>
        public string? ThumbnailViewName
        {
            get => _thumbnailViewName;
            set => SetProperty(ref _thumbnailViewName, value);
        }
    }
}

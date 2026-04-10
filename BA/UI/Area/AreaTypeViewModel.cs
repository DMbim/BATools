using BA.Core.Enums;

namespace BA.UI.ViewModels
{
    public sealed class AreaTypeViewModel : ViewModelBase
    {
        private bool _isSelected;

        public required AreaType AreaType { get; init; }
        public required string DisplayName { get; init; }
        public required string LegalCitation { get; init; }
        public required string ValidFrom { get; init; }
        public string? ScopeNote { get; init; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public bool HasScopeNote => !string.IsNullOrEmpty(ScopeNote);
    }
}
using System.Collections.ObjectModel;

namespace BA.Core.Content.Models
{
    public sealed class FolderNode
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;

        public ObservableCollection<FolderNode> Children { get; } = new();

        public string DisplayText => Name;
    }
}
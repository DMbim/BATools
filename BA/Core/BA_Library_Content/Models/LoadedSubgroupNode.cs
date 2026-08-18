using System.Collections.ObjectModel;

namespace BA.Core.Content.Models
{
    public sealed class LoadedSubgroupNode
    {
        public string Name { get; set; } = string.Empty;
        public ObservableCollection<LoadedCategoryNode> Categories { get; } = new();
       

        public string DisplayText => $"{Name} ({Categories.Count})";
    }
}
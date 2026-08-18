using System.Collections.ObjectModel;

namespace BA.Core.Content.Models
{
    public sealed class LoadedDisciplineTabNode
    {
        public string Name { get; set; } = string.Empty;
        public ObservableCollection<LoadedSubgroupNode> Subgroups { get; } = new();

        public string DisplayText => $"{Name} ({Subgroups.Count})";
    }
}
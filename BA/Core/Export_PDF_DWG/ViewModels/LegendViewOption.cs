namespace BA.ViewModels.Export
{
    public class LegendViewOption
    {
        public string Name { get; }
        public string UniqueId { get; }

        public LegendViewOption(string name, string uniqueId)
        {
            Name = name;
            UniqueId = uniqueId;
        }

        public override string ToString() => Name;
    }
}

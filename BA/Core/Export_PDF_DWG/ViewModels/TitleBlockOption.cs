namespace BA.ViewModels.Export
{
    public class TitleBlockOption
    {
        public string Name { get; }
        public string UniqueId { get; }

        public TitleBlockOption(string name, string uniqueId)
        {
            Name = name;
            UniqueId = uniqueId;
        }

        public override string ToString() => Name;
    }
}

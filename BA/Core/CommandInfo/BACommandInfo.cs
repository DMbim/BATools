namespace BA.Core.CommandCatalog
{
    public class BACommandInfo
    {
        public string Key { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string FullClassName { get; set; } = string.Empty;
        public string SmallIconResourceName { get; set; } = string.Empty;
        public string LargeIconResourceName { get; set; } = string.Empty;
    }
}
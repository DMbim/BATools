namespace BA.ViewModels.Export
{
    /// <summary>
    /// One row in the read only "Available Parameters" grid in
    /// NamingTemplateBuilderWindow. Plain display data, no
    /// INotifyPropertyChanged needed, the list is built once per window
    /// open and never mutated afterward.
    /// </summary>
    public class AvailableTokenRowViewModel
    {
        public string TokenName { get; }
        public string TypeLabel { get; }

        public AvailableTokenRowViewModel(string tokenName, bool isBuiltIn)
        {
            TokenName = tokenName;
            TypeLabel = isBuiltIn ? "Built-in" : "Parameter";
        }
    }
}

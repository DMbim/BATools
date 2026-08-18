
namespace BA.UI
{
    internal class RevisionSelectionResult
    {
        public bool Cancelled { get; set; }

        // true = use existing revision; false = create new
        public bool UseExisting { get; set; }

        public ElementId ExistingRevisionId { get; set; }

        // If true, apply Description/Date to the existing revision
        public bool EditExisting { get; set; }

        public string Description { get; set; }
        public string DateText { get; set; }
    }
}

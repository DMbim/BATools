using Autodesk.Revit.UI;

namespace BA.SelectionManager.Models
{
    public record RevitCommandEntry(
        PostableCommand Command,
        string DisplayName,
        string Category);
}
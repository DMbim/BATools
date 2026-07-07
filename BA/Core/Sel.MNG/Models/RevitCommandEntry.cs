using Autodesk.Revit.UI;

namespace BATools.SelectionManager.Models
{
    public record RevitCommandEntry(
        PostableCommand Command,
        string DisplayName,
        string Category);
}
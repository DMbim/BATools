
using Autodesk.Revit.UI;
using BA.Commands.Standards;
using BA.Ribbon;
using BATools.SelectionManager.Commands;

namespace BA.BAApplication.Ribbon
{
    internal static class SelectionPanelFactory
    {
        internal static void Build(RibbonPanel panel)
        {
            panel.AddPushButton<OpenSelectionManagerCommand>(
                "OpenSelectionManager",
                "Selection\nManager",
                "Open the Selection Manager — a tool for saving, loading, and managing selection sets.",
                IconResources.SelectionManager16, IconResources.SelectionManager32);

            panel.AddPushButton<OpenRecentsCommand>(
                "OpenRecents",
                "Recents",
                "Open the Recents — a tool for quickly accessing recent selection sets.",
                IconResources.SelectionManager16, IconResources.SelectionManager32);
            
            panel.AddPushButton<Cmd_SubcategoryAuditor>(
                "SubcategorySken", "Subcategory\nSken",
                "Skens families and collects subcategories.",
                IconResources.SubCatSK16, IconResources.SubCatSK32);
        }

    }
}
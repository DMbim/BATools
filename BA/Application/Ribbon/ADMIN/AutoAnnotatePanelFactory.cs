using System.Reflection;
using Autodesk.Revit.UI;
using BA.BIM.Commands.Dimension;


namespace BA.AutoAnnotate.Ribbon
{
    /// <summary>
    /// Written against plain Autodesk.Revit.UI.PushButtonData rather than a
    /// Nice3point.Revit.Toolkit ribbon extension, because I have not seen
    /// DrawingProductionPanelFactory.cs or any other existing panel factory in
    /// this project and won't guess at a helper method signature. If you paste
    /// me an existing factory, I'll convert this to match its exact idiom.
    /// </summary>
    public static class AutoAnnotatePanelFactory
    {
        public static void Build(RibbonPanel panel)
        {
            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            var dimensionButtonData = new PushButtonData(
                "BA_CmdAutoDimension",
                "Auto\nDimension",
                assemblyPath,
                typeof(BA_CmdAutoDimension).FullName)
            {
                ToolTip = "Scan selected views for wall openings and place aligned dimension chains, with preview/approval before committing."
            };

          

            panel.AddItem(dimensionButtonData);

        }
    }
}
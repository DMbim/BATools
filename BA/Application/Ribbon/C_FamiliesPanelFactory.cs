// FILE: BA_Tools/Application/Ribbon/FamiliesPanelFactory.cs
using Autodesk.Revit.UI;
using BA.App.Commands;
using BA.BIM.Commands.Anno;
using BA.Classification;
using BA.Commands;
using BA.Commands.Families;
using BA.Families.Commands;
using BA.Ribbon;
using BA.RoomClassification;
using BA.Subcategories.Commands;
namespace BA.BAApplication.Ribbon
{
    internal static class FamiliesPanelFactory
    {
        internal static void Build(RibbonPanel panel)
        {
            var (Load, Save) = panel.AddStackedButtons<Cmd_OpenContentBrowserCommand, SaveFamiliesCommand>(
                "ContentBrowser", "Load\nFamilies",
                "SaveFamilies", "Save\nFamilies",
                IconResources.SaveFamilies16, IconResources.ContentBrowser16,
                "Browse the BA content library and place family types directly into the model.",
                "Browse the BA content library and place family types directly into the model.");

            panel.AddPushButton<Cmd_FamilyParameters>(
                "HarmonizeFamilyParams", "Manage\nFamily Parameters",
                "Add, rename or replace parameters in project families to match BA shared parameter standards.",
                IconResources.FamilyParams16, IconResources.FamilyParams32);

            #region Family From Geometry + Get Volume + Subcategory Auditor (stacked)
            var (familyFromGeoBtn, getVolumeBtn, subcatAuditorBtn) =
                panel.AddStackedButtons<Cmd_FamilyFromGeometry, Cmd_GetVolume, SubcategoryManagerCommand>(
                    "FamilyFromGeometry", "Family\nFrom Geometry",
                    "GetVolume", "Get\nVolume",
                    "SubcategoryAuditor", "Subcategory\nAuditor",
                    IconResources.FamilyFromSelect_16, IconResources.GetVolume_16, IconResources.SubCat16,
                    "Create a new family from selected geometry.",
                    "For categories where Volume is not a native built-in parameter, calculates and writes the element volume into a prepared parameter.",
                    "Audit family subcategories against BA naming conventions.");
            #endregion

            panel.AddPushButton<Cmd_WindowOrientation>(
                 "WindowOrientation", "Window\nOrientation",
                "Set the orientation of selected windows.",
                IconResources.Orient_16, IconResources.Orient_32);
        }
    }
}
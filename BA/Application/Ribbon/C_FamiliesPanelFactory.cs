// FILE: BA_Tools/Application/Ribbon/FamiliesPanelFactory.cs
using Autodesk.Revit.UI;
using BA.App.Commands;
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
            panel.AddPushButton<Cmd_OpenContentBrowserCommand>(
                "ContentBrowser", "Load\nFamilies",
                "Browse the BA content library and place family types directly into the model.",
                IconResources.ContentBrowser16, IconResources.ContentBrowser32);

            panel.AddPushButton<Cmd_FamilyParameters>(
                "HarmonizeFamilyParams", "Manage\nFamily Parameters",
                "Add, rename or replace parameters in project families to match BA shared parameter standards.",
                IconResources.FamilyParams16, IconResources.FamilyParams32);

            panel.AddPushButton<SubcategoryManagerCommand>(
                "SubcategoryAuditorr", "Subcategory\nAuditor",
                "Audit family subcategories against BA naming conventions.",
                IconResources.SubCat16, IconResources.SubCat32);

            panel.AddPushButton<SaveFamiliesCommand>(
                "SaveFamilies", "Save\nFamilies",
                "Save selected families to disk.",
                IconResources.SaveFamilies16, IconResources.SaveFamilies32);

            panel.AddPushButton<Cmd_FamilyFromGeometry>(
                    "FamilyFromGeometry", "Family\nFrom Geometry",
                    "Create a new family from selected geometry.",
                    IconResources.FamilyFromSelect_16, IconResources.FamilyFromSelect_32);

            panel.AddPushButton<Cmd_WindowOrientation>(
                 "WindowOrientation", "Window\nOrientation",
                "Set the orientation of selected windows.",
                IconResources.Orient_16, IconResources.Orient_32);
        }
    }
}
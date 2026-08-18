// FILE: BA_Tools/Application/Ribbon/FamiliesPanelFactory.cs
using Autodesk.Revit.UI;
using BA.App.Commands;
using BA.BIM.Commands.Anno;
using BA.Classification;
using BA.Commands;
using BA.Commands.Content;
using BA.Commands.Export;
using BA.Commands.Families;
using BA.Families.Commands;
using BA.Ribbon;
using BA.RoomClassification;
using BA.Subcategories.Commands;
using BA.UI.Commands.Anno;
namespace BA.BAApplication.Ribbon
{
    internal static class FamiliesPanelFactory
    {
        internal static void Build(RibbonPanel panel)
        {
            var (Load, Save, LoadedFamilyBrowser) = panel.AddStackedButtons<Cmd_OpenContentBrowserCommand, SaveFamiliesCommand, Cmd_LoadedFamilyBrowser>(
                "ContentBrowser", "Load\nFamilies",
                "SaveFamilies", "Save\nFamilies",
                "LoadedFamilyBrowser", "Loaded\nFamily Browser",
                IconResources.SaveFamilies16, IconResources.ContentBrowser16, IconResources.ContentBrowser16,
                "Browse the BA content library and place family types directly into the model.",
                "Browse the BA content library and place family types directly into the model.",
                "Browse the BA content library and place family types directly into the model.");



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

            #region Parameter Manager (moved from ProjectPanelFactory, now retired)
            var (projPar, famPar) = panel.AddStackedButtons<Cmd_RevitParameters, Cmd_FamilyParameters>(
                    "ParameterManager", "Manage\nParameters",
                    "HarmonizeFamilyParams", "Manage\nFamily Parameters",
                    IconResources.RevPar16, IconResources.FamilyParams16,
                     "View and manage Parameters in the active document.",
                    "Add, rename or replace parameters in project families to match BA shared parameter standards.");
            #endregion


        }
    }
}
// FILE: BA_Tools/Application/Ribbon/FamiliesPanelFactory.cs
using Autodesk.Revit.UI;
using BA.App.Commands;
using BA.Classification;
using BA.Commands;
using BA.Commands.Families;
using BA.Ribbon;
using BA.RoomClassification;
using BA.Families.Commands;

namespace BA.BAApplication.Ribbon
{
    internal static class FamiliesPanelFactory
    {
        internal static void Build(RibbonPanel panel)
        {
            panel.AddPushButton<Cmd_OpenContentBrowserCommand>(
                "ContentBrowser", "Content\nBrowser",
                "Browse the BA content library and place family types directly into the model.",
                IconResources.ContentBrowser16, IconResources.ContentBrowser32);

            panel.AddPushButton<Cmd_FamilyParameters>(
                "HarmonizeFamilyParams", "Harmonize\nFamily Params",
                "Add, rename or replace parameters in project families to match BA shared parameter standards.",
                IconResources.FamilyParams16, IconResources.FamilyParams32);

            panel.AddPushButton<Cmd_ClassifyElements>(
                "ClassifyTypes", "Classify\nTypes",
                "Classify element types against a rule set loaded from an Excel file.",
                IconResources.Classify16, IconResources.Classify32);

            panel.AddPushButton<RoomClassificationImportCommand>(
                "RoomClassificationImport", "Room\nClassification",
                "Import room program data (type, department, function, code, group) from an Excel matrix.",
                IconResources.ClsRoom16, IconResources.ClsRoom32);

            panel.AddPushButton<SaveFamiliesCommand>(
                "SaveFamilies", "Save\nFamilies",
                "Save selected families to disk.",
                IconResources.SaveFamilies16, IconResources.SaveFamilies32);
        }
    }
}
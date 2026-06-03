// FILE: BA_Tools/Application/Ribbon/ViewsPanelFactory.cs
using Autodesk.Revit.UI;
using BA.Classification;
using BA.Commands;
using BA.Commands.Views.ScopeBoxes;
using BA.Commands.Standards;
using BA.Ribbon;

namespace BA.BAApplication.Ribbon
{
    internal static class ViewsPanelFactory
    {
        internal static void Build(RibbonPanel panel)
        {
            panel.AddPushButton<Cmd_ViewTemplateTransfer>(
                "TransferViewTemplate", "Transfer\nView Template",
                "Copy selected properties from a source view template to one or more target templates.",
                IconResources.ViewTemplate16, IconResources.ViewTemplate32);



            panel.AddPushButton<Cmd_SubcategoryAuditor>(
                "SubcategoryAuditor", "Subcategory\nAuditor",
                "Audit family subcategories against BA naming conventions.",
                IconResources.SubCat16, IconResources.SubCat32);

            panel.AddPushButton<ScopeBoxManagerCommand>(
                "ScopeBoxManager", "Scope Box\nManager",
                "Manage, rename and assign scope boxes to views.",
                IconResources.Sbox16, IconResources.Sbox32);
        }
    }
}
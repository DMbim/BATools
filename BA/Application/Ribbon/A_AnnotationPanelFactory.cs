// FILE: BA_Tools/Application/Ribbon/AnnotationPanelFactory.cs
using Autodesk.Revit.UI;
using BA.App.Overhead;
using BA.BIM.Commands.Anno;
using BA.Commands;
using BA.Commands.Anno;
using BA.KeyplanGrid;
using BA.Markup.Commands;
using BA.Ribbon;
using BA.UI.Commands.Anno;
using BATools.ParamCopy.Commands;
using BATools.Rooms.Commands;
using Nice3point.Revit.Extensions;

namespace BA.BAApplication.Ribbon
{
    internal static class AnnotationPanelFactory
    {
        internal static void Build(RibbonPanel panel)
        {
            #region ANNOTATION ARRANGEMENT
            panel.AddPushButton<ArrangeAnnotationsCommand>(
                "ArrangeAnnotations", "Arrange\nAnnotations",
                "Auto-arrange selected annotations to resolve overlaps.",
                IconResources.ArAnno16, IconResources.ArAnno32);
            #endregion

            #region DIMENSION EDITING

            var pdDim = panel.AddPulldownButton<Cmd_Dim_ValueOverride>(
                "DimEditPulldown", "Dim\nEdit",
                "Dimension editing tools.",
                IconResources.DimOverride16, IconResources.DimOverride32);


            pdDim.AddPushButton<Cmd_Dim_ValueOverride>(
                "OverrideText", "Override\nText",
                "Override selected dimensions with custom text.",
                IconResources.DimValueOverride16, IconResources.DimValueOverride32);

            pdDim.AddPushButton<Cmd_Dim_AddBelow>(
                "AddBelow", "Add\nBelow",
                "Add an extra numeric value below the existing dimension value.",
                IconResources.DimAddBelow16, IconResources.DimAddBelow32);

            #endregion
            #region Area to Room Transfer
            panel.AddPushButton<TransferAreaValuesToRoomsCommand>(
                "AreasToRooms", "Areas\n-> Rooms",
                "Transfer area values to rooms based on their number",
                IconResources.CzechAreas16, IconResources.CzechAreas32);
            #endregion
            #region Keyplan Grid Generation
            var pdKeyPlan = panel.AddPulldownButton<Cmd_KeyplanGridGenerator>(
                "KeyPlan", "Key\nPlan",
                "Keyplan generation tools.",
                IconResources.KeyPlan16, IconResources.KeyPlan32);

            pdKeyPlan.AddPushButton<Cmd_KeyplanGridGenerator>(
                "GenerateKeyplanGrids", "Generate\nGrids",
                "Generate grid cells in the keyplan drafting view based on the current model grids.",
                IconResources.KeyPlan16, IconResources.KeyPlan32);
            #endregion
            #region Markup Placement
            panel.AddPushButton<PlaceMarkupCommand>(
                "PlaceMarkup", "Place Markup",
                "Place markup annotations in the active view.",
                IconResources.Markup16, IconResources.Markup32);
            #endregion

            var Ovr = panel.AddPulldownButton<Cmd_OverheadAutoDash>(
                "OverheadAutoDashPulldown", "Overhead\nAuto Dash",
                "Overhead auto dash tools.",
                IconResources.Overhead16, IconResources.Overhead32);
            Ovr.AddPushButton<Cmd_OverheadAutoDash>(
                "OverheadAutoDashPulldown", "Overhead\nAuto Dash",
                "Overhead auto dash tools.",
                IconResources.Overhead16, IconResources.Overhead32);
            Ovr.AddPushButton<ToggleOverheadProxyCommand>(
                "OverheadLines", "Overhead\nLines",
                "Automatically generate overhead dash patterns in the active plan view.",
                IconResources.Overhead16, IconResources.Overhead32);

            panel.AddPushButton<Cmd_ColourPalette>(
                                "ColourPalette", "Colour\nPalette",
                "Manage view filter colors.",
                IconResources.ColourPalette16, IconResources.ColourPalette32);

            panel.AddPushButton<Cmd_ClearAllOverrides>(
                            "ClearAllOverrides", "Clear\nOverrides",
            "Clear all graphic overrides in the active view.",
            IconResources.ClearOverrides16, IconResources.ClearOverrides32);
            
            panel.AddPushButton<Cmd_UnhideAllElements>(
                "UnhideAllElements", "Unhide\nAll Elements",
        "Unhide all elements in the active view.",
        IconResources.UnhideAllElements16, IconResources.UnhideAllElements32);
        }
    }



}
using Autodesk.Revit.UI;
using BA.App.Overhead;
using BA.BIM.Commands.Anno;
using BA.Commands;
using BA.Commands.Anno;
using BA.Commands.Dimensioning;
using BA.Commands.Rooms;
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

            #region Markup Placement
            panel.AddPushButton<PlaceMarkupCommand>(
                "PlaceMarkup", "Place Markup",
                "Place markup annotations in the active view.",
                IconResources.Markup16, IconResources.Markup32);
            #endregion
            #region ANNOTATION ARRANGEMENT (stacked)
            var (arrangeBtn, tagAllBtn) = panel.AddStackedButtons<ArrangeAnnotationsCommand, TagAllSelectedCommand>(
                "ArrangeAnnotations", "Arrange\nAnnotations",
                "TagAllSelected", "Tag All\nSelected",
                IconResources.ArAnno16, IconResources.ArAnno16,
                "Auto-arrange selected annotations in the active view to resolve overlaps between them.",
                "Tag all selected elements with the chosen tag type. Tag placement is calculated to avoid overlapping existing annotations.");
            #endregion

            #region DIMENSION EDITING (stacked with Dimension Elements and Reveal Host)
            var (dimElementsBtn, pdDim, revealHostBtn) = panel.AddStackedPushPulldownPush<Cmd_DimensionElementsToReference, Cmd_RevealHost>(
                "DimensionElementsToReference", "Dimension\nElements",
                "Create a multi-segment dimension from selected elements to a picked reference.",
                IconResources.DimOverride16,

                "DimEditPulldown", "Dim\nEdit",
                "Dimension editing tools.",
                IconResources.DimOverride16,

                "RevealHost", "Reveal\nHost",
                "Zoom to and highlight the host element of the selected element. Currently supports dimensions only.",
                IconResources.gethost_16);

            pdDim.AddPushButton<Cmd_Dim_ValueOverride>(
                "OverrideText", "Override\nText",
                "Override the displayed value of selected dimensions with custom text.",
                IconResources.DimValueOverride16, IconResources.DimValueOverride32);

            pdDim.AddPushButton<Cmd_Dim_AddBelow>(
                "AddBelow", "Add\nBelow",
                "Add an extra numeric value below the existing dimension value, without replacing the original value.",
                IconResources.DimAddBelow16, IconResources.DimAddBelow32);
            #endregion

            #region Area to Room Transfer
            panel.AddPushButton<TransferAreaValuesToRoomsCommand>(
                "AreasToRooms", "Areas\n-> Rooms",
                "Transfer area values to matching rooms, matched by room number.",
                IconResources.CzechAreas16, IconResources.CzechAreas32);
            #endregion


            #region Overhead / Schemes / Keyplan (stacked)
            var (Ovr, schemesBtn, pdKeyPlan) = panel.AddStackedPulldownPushPulldown<Cmd_ColourPalette>(
                "OverheadAutoDashPulldown", "Overhead\nAuto Dash",
                "Overhead auto dash tools.",
                IconResources.Overhead16,

                "Schemes + Legends", "Schemes\n+Legends",
                "Create color schemes and legends for the active view.",
                IconResources.ColourPalette16,

                "KeyPlan", "Key\nPlan",
                "Keyplan generation tools.",
                IconResources.KeyPlan16);

            Ovr.AddPushButton<Cmd_OverheadAutoDash>(
                "OverheadAutoDash", "Overhead\nAuto Dash",
                "Generate overhead dash line patterns for elements above the current view's cut plane.",
                IconResources.Overhead16, IconResources.Overhead32);
            Ovr.AddPushButton<ToggleOverheadProxyCommand>(
                "OverheadLines", "Overhead\nLines",
                "Toggle automatic overhead dash pattern generation in the active plan view.",
                IconResources.Overhead16, IconResources.Overhead32);

            pdKeyPlan.AddPushButton<Cmd_KeyplanGridGenerator>(
                "GenerateKeyplanGrids", "Generate\nGrids",
                "Generate grid cells in the keyplan drafting view.",
                IconResources.KeyPlan16, IconResources.KeyPlan32);
            #endregion

            #region View Overrides (stacked)
            var (clearOverridesBtn, unhideAllBtn) = panel.AddStackedButtons<Cmd_ClearAllOverrides, Cmd_UnhideAllElements>(
                "ClearAllOverrides", "Clear\nOverrides",
                "UnhideAllElements", "Unhide\nAll Elements",
                IconResources.ClearOverrides16, IconResources.UnhideAllElements16,
                "Clear all graphic overrides in the active view.",
                "Unhide all elements in the active view.");
            #endregion

        }
    }
}
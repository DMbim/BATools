using Autodesk.Revit.UI;
using BA.App.Overhead;
using BA.BIM.Commands.Anno;
using BA.Commands;
using BA.Commands.Anno;
using BA.Commands.CurveToElement;
using BA.Commands.Dimensioning;
using BA.Commands.Export;
using BA.Markup.Commands;
using BA.Ribbon;
using BA.UI.Commands.Anno;
using BATools.Rooms.Commands;
using BA.Zoom.Commands;
using Nice3point.Revit.Extensions;

namespace BA.BAApplication.Ribbon
{
    internal static class DrawingProductionPanelFactory
    {
        internal static void Build(RibbonPanel panel)
        {
            #region Primary Tools (stacked: Markup, Super Selector, Schemes+Legends)
            panel.AddStackedButtons<PlaceMarkupCommand, Cmd_SuperSelector, Cmd_ColourPalette>(
                "PlaceMarkup", "Place\nMarkup",
                "SuperSelector", "Super\nSelector",
                "SchemesLegends", "Schemes\n+Legends",
                IconResources.Markup16, IconResources.superS16, IconResources.ColourPalette16,
                "Place markup annotations in the active view.",
                "Select elements based on various criteria.",
                "Create color schemes and legends for the active view.");

            #endregion

            #region ANNOTATION ARRANGEMENT (stacked)
            var (arrangeBtn, transf, tagAllBtn) = panel.AddStackedPushPulldownPush<ArrangeAnnotationsCommand, TagAllSelectedCommand>(
                "ArrangeAnnotations", "Arrange\nAnnotations",
                "Auto-arrange selected annotations in the active view to resolve overlaps between them.",
                IconResources.tags16,

                "Area To Room", "Areas\n-> Room",
                "Assign area values to rooms based on selected elements.",
                IconResources.CzechAreas16,


                "Tag & Arrange", "Tag &\nArrange",
                "Tag all selected elements with the chosen tag type. Tag placement is calculated to avoid overlapping existing annotations.",
                IconResources.ta16,
                "Get Area values to rooms"
                );

            transf.AddPushButton<TransferAreaValuesToRoomsCommand>(
                "AreaToRoom", "Area\n-> Room",
                "Assign area values to rooms based on selected elements.",
                IconResources.CzechAreas16, IconResources.CzechAreas32);

            transf.AddPushButton<Cmd_TransferAreaValuesToRooms_Settings>(
                "AreaToRoomSettings", "Area\n-> Room\nSettings",
                "Configure settings for transferring area values to rooms.",
                IconResources.CzechAreas16, IconResources.CzechAreas32);
            #endregion

            #region Secondary Tools (stacked: Window Orientation, Zoom pulldown, Curve to Wall)
            var (winOriBtn, pdZoom, curveToWallBtn) = panel.AddStackedPushPulldownPush<Cmd_WindowOrientation, CurveToElementCommand>(
                "Cmd_WindowOrientation", "Window\nOrientation",
                "Configure the window orientation settings.",
                IconResources.Orient_16,

                "Zoom", "Zoom\n->",
                "Zoom to rooms in the model, in a linked model, or to a selected element.",
                IconResources.Zoom16,

                "CurveToElement", "Curve\nTo Wall",
                "Convert curves to walls.",
                IconResources.LWall_16,
                "Zoom targets: room in local model, room in linked model, or selected element."
                );

            pdZoom.AddPushButton<Cmd_ZoomToRoom>(
                "ZoomToRoomLocal", "ToRoom\nLocal",
                "Zoom to rooms in the active model.",
                IconResources.ZoomLo16, IconResources.ZoomLo32);

            pdZoom.AddPushButton<Cmd_ZoomToRoom_Link>(
                "ZoomToRoomLink", "ToRoom\nLinked",
                "Zoom to rooms in a linked model.",
                IconResources.ZoomL16, IconResources.ZoomL32);

            pdZoom.AddPushButton<Cmd_ZoomToSelectedElement>(
                "ZoomToSelectedElement", "To Selected\nElement",
                "Zoom to the selected element in the model.",
                IconResources.ZoomE16, IconResources.ZoomE32);
            #endregion

            #region Dimension Elements to Reference (stacked with Reveal Host)
            var (dimElementsBtn, pdDim, revealHostBtn) = panel.AddStackedPushPulldownPush<Cmd_DimensionElementsToReference, Cmd_RevealHost>(
                "DimensionElementsToReference", "Dimension\nElements",
                "Create a multi-segment dimension from selected elements to a picked reference.",
                IconResources.DimOverride16,

                "DimEditPulldown", "Dim\nEdit",
                "Dimension editing tools.",
                IconResources.DimOverride16,

                "RevealHost", "Reveal\nHost",
                "Zoom to and highlight the host element of the selected element. Currently supports dimensions only.",
                IconResources.gethost_16,

                "Edit dimension values and add below text to selected dimensions."


            );
            pdDim.AddPushButton<Cmd_Dim_ValueOverride>(
                "OverrideText", "Override\nText",
                "Override the displayed value of selected dimensions with custom text.",
                IconResources.DimValueOverride16, IconResources.DimValueOverride32);

            pdDim.AddPushButton<Cmd_Dim_AddBelow>(
                "AddBelow", "Add\nBelow",
                "Add an extra numeric value below the existing dimension value, without replacing the original value.",
                IconResources.DimAddBelow16, IconResources.DimAddBelow32);
            #endregion
            #region View Overrides (stacked)
            var (clearOverridesBtn, Ovr, unhideAllBtn) = panel.AddStackedPushPulldownPush<Cmd_ClearAllOverrides, Cmd_UnhideAllElements>(
                "ClearAllOverrides", "Clear\nOverrides",
                "Clear all graphic overrides in the active view.",
                IconResources.ClearOverrides16,

                 "Show Overhead", "Show \nOverhead",
                 "Hide or display overhead elements in the active view.",
                IconResources.Overhead16,


                "UnhideAllElements", "Unhide\nAll Elements",
                 "Unhide all elements in the active view.",
                 IconResources.UnhideAllElements16,
                 "Overhead auto dash tools."
               );

            Ovr.AddPushButton<Cmd_OverheadAutoDash>(
                "OverheadSettings", "Overhead\nSettings",
                "Generate overhead dash line patterns for elements above the current view's cut plane.",
                IconResources.Overhead16, IconResources.Overhead32);
            Ovr.AddPushButton<ToggleOverheadProxyCommand>(
                "ToggleOverhead", "Overhead\nToggle",
                "Toggle automatic overhead dash pattern generation in the active plan view.",
                IconResources.Overhead16, IconResources.Overhead32);


            #endregion

        }
    }
}
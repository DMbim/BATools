using Autodesk.Revit.UI;
using BA.App.Commands;
using BA.App.Overhead;
using BA.BIM.Commands.Anno;
using BA.BIM.Commands.Dimension;
using BA.Commands;
using BA.Commands.Anno;
using BA.Commands.Content;
using BA.Commands.CurveToElement;
using BA.Commands.Dimensioning;
using BA.Commands.Export;
using BA.Commands.Finishes;
using BA.Commands.Rooms;
using BA.Families.Commands;
using BA.Markup.Commands;
using BA.Ribbon;
using BA.Subcategories.Commands;
using BA.UI.Commands.Anno;
using BA.Zoom.Commands;
using BATools.Rooms.Commands;
using Nice3point.Revit.Extensions;

namespace BA.BAApplication.Ribbon
{
    internal static class DrawingProductionPanelFactory
    {
        internal static void Build(RibbonPanel panel)
        {
            #region Super Selector

            panel.AddPushButton<Cmd_SuperSelector>(
                "SuperSelector", "Super\nSelector",
                "Select elements based on various criteria.",
                IconResources.superS16, IconResources.superS32);

            #endregion

            #region Drawing Tools (big pulldown: dimensioning, annotation, tagging, view overrides, overhead)

            // This single pulldown absorbs everything that used to be spread across the
            // Dimension Elements stack, the Arrange Annotations / Tag & Arrange stack slots,
            // the Clear Overrides / Unhide All Elements stack slots, and the former Show
            // Overhead sub-pulldown (dissolved here, its 2 items flattened into this list).
            // "DrawingToolsPulldown" / "Drawing\nTools" is a placeholder header, rename it and
            // swap the icon (currently reusing the Dimension Elements icon) to fit a pulldown
            // this broad.
            PulldownButton drawingTools = panel.AddPulldownButton<Cmd_DimensionElementsToReference>(
                "DrawingToolsPulldown", "Annotation\nTools",
                "Dimensioning, annotation, tagging, and view override tools.",
                IconResources.ArAnn_16, IconResources.ArAnn_32);


            drawingTools.AddPushButton<ArrangeAnnotationsCommand>(
                "ArrangeAnnotations", "Arrange\nAnnotations",
                "Auto-arrange selected annotations in the active view to resolve overlaps between them.",
                IconResources.ArAnn_16, IconResources.Arrang_32);
            drawingTools.AddPushButton<TagAllSelectedCommand>(
                "Tag & Arrange", "Tag &\nArrange",
                "Tag all selected elements with the chosen tag type. Tag placement is calculated to avoid overlapping existing annotations.",
                IconResources.TagNAr_16, IconResources.TagNAr_32);
            drawingTools.AddSeparator();


            drawingTools.AddPushButton<BA_CmdAutoDimension>(
                "AutoDimension", "Auto\nDimension",
                "Automatically create dimensions for selected elements based on predefined rules.",
                IconResources.ArAnno16, IconResources.ArAnno32);

            drawingTools.AddPushButton<Cmd_ClearAllOverrides>(
                "ClearAllOverrides", "Clear\nOverrides",
                "Clear all graphic overrides in the active view.",
                IconResources.ClearOverrides16, IconResources.ClearOverrides32);
            drawingTools.AddPushButton<Cmd_UnhideAllElements>(
                "UnhideAllElements", "Unhide\nAll Elements",
                "Unhide all elements in the active view.",
                IconResources.UnhideAllElements16, IconResources.UnhideAllElements32);

            drawingTools.AddSeparator();





            drawingTools.AddPushButton<Cmd_Dim_ValueOverride>(
                "OverrideText", "DimEdit\nOverride",
                "Override the displayed value of selected dimensions with custom text.",
                IconResources.DimValueOverride16, IconResources.DimValueOverride32);
            drawingTools.AddPushButton<Cmd_Dim_AddBelow>(
                "AddBelow", "DimEdit\nAdd Below",
                "Add an extra numeric value below the existing dimension value, without replacing the original value.",
                IconResources.DimAddBelow16, IconResources.DimAddBelow32);






            drawingTools.AddSeparator();

            drawingTools.AddPushButton<Cmd_RevealHost>(
                "RevealHost", "DimInfo\nReveal Host",
                "Zoom to and highlight the host element of the selected element. Currently supports dimensions only.",
                IconResources.gethost_16, IconResources.gethost_32);

            #endregion
            #region Primary Tools (stacked: Markup, Schemes+Legends)

            panel.AddStackedButtons<PlaceMarkupCommand, Cmd_ColourPalette>(
                "PlaceMarkup", "Place\nMarkup",
                "SchemesLegends", "Schemes\n+Legends",
                IconResources.Markup16, IconResources.ColourPalette16,
                "Place markup annotations in the active view.",
                "Create color schemes and legends for the active view.");

            #endregion

            #region Room Management Tools (big pulldown: dimensioning, annotation, tagging, view overrides, overhead)
            PulldownButton roomNSpace = panel.AddPulldownButton<Cmd_DimensionElementsToReference>(
                "Room Mng Tools", "Room\nMng Tool",
                "¨Collection ofRoom management tools",
                IconResources.RoomMng_16, IconResources.RoomMng_32);




            roomNSpace.AddPushButton<Cmd_ZoomToRoom>(
                "ZoomToRoomLocal", "Zoom\nToRoom\nLocal",
                "Zoom to rooms in the active model.",
                IconResources.ZoomLo16, IconResources.ZoomLo32);
            roomNSpace.AddPushButton<Cmd_ZoomToRoom_Link>(
                "ZoomToRoomLink", "Zoom\nToRoom\nLinked",
                "Zoom to rooms in a linked model.",
                IconResources.ZoomL16, IconResources.ZoomL32);
            roomNSpace.AddPushButton<Cmd_ZoomToSelectedElement>(
                "ZoomToSelectedElement", "Zoom\nTo Selected\nElement",
                "Zoom to the selected element in the model.",
                IconResources.ZoomE16, IconResources.ZoomE32);
            roomNSpace.AddPushButton<Cmd_ZoomToRoom_Settings>(
                "ZoomToSelectedElementSettings", "Zoom\nTo Selected\nElement\nSettings",
                "Configure settings for zooming to the selected element.",
                IconResources.ZmSet16, IconResources.ZmSet32);

            roomNSpace.AddSeparator();

            roomNSpace.AddPushButton<Cmd_ElementToRoom_Link>(
                "ElementToRoomLink", "RoomNumber\nFrom Link",
                "Write room data to host-model elements from rooms in a linked model.",
                IconResources.ElementToRoomLink16, IconResources.ElementToRoomLink32);
            roomNSpace.AddPushButton<Cmd_ElementToRoom_Local>(
                "ElementToRoomLocal", "RoomNumber\nFrom Local",
                "Write room data to elements from rooms in the same document.",
                IconResources.ElementToRoomLocal16, IconResources.ElementToRoomLocal32);
            roomNSpace.AddPushButton<Cmd_ElementToRoom_Settings>(
                "ElementToRoomSettings", "Settings",
                "Show or hide the Element \u2192 Room settings panel (category, parameters, link instance).",
                IconResources.E2r_16, IconResources.E2r_32);

            roomNSpace.AddSeparator();

            roomNSpace.AddPushButton<Cmd_AxisToRoom_Link>(
                "AxisToRoomLink", "RoomAxis\nFrom Link",
                "Resolve rooms from a linked model via selected room tags.",
                IconResources.AxisToRoomLink16, IconResources.AxisToRoomLink32);
            roomNSpace.AddPushButton<Cmd_AxisToRoom_Local>(
                "AxisToRoomLocal", "RoomAxis\nFrom Local",
                "Resolve rooms from the active model via selected room tags.",
                IconResources.AxisToRoomLocal16, IconResources.AxisToRoomLocal32);
            roomNSpace.AddPushButton<Cmd_AxisToRoom_Settings>(
                "AxisToRoomSettings", "Settings",
                "Configure the Revit link and BA_Axis placement used by Axis \u2192 Room.",
                IconResources.A2r_16, IconResources.A2r_32);

            roomNSpace.AddSeparator();

            roomNSpace.AddPushButton<TransferAreaValuesToRoomsCommand>(
                "AreaToRoom", "Area\n-> Room",
                "Assign area values to rooms based on selected elements.",
                IconResources.CzechAreas16, IconResources.CzechAreas32);
            roomNSpace.AddPushButton<Cmd_TransferAreaValuesToRooms_Settings>(
                "AreaToRoomSettings", "Area\n-> Room\nSettings",
                "Configure settings for transferring area values to rooms.",
                IconResources.CzechAreas16, IconResources.CzechAreas32);

            #endregion

            #region Element Management Tools (big pulldown: dimensioning, annotation, tagging, view overrides, overhead)
            PulldownButton elementMng = panel.AddPulldownButton<Cmd_WindowOrientation>(
                 "Element Mng Tools", "Element\nMng Tool",
                 "¨Collection ofElement management tools",
                 IconResources.ElemMng_16, IconResources.ElemMng_32);

            elementMng.AddPushButton<Cmd_WindowOrientation>(
                "Cmd_WindowOrientation", "Window\nOrientation",
                "Configure the window orientation settings.",
                IconResources.Orient_16, IconResources.Orient_32);

            elementMng.AddPushButton<CurveToElementCommand>(
                "CurveToElement", "Curve\nTo Wall",
                "Convert curves to walls.",
                IconResources.LWall_16, IconResources.LWall_32);


            elementMng.AddPushButton<Cmd_FamilyFromGeometry>(
                "FamilyFromGeometry", "Family\nFrom Geometry",
                "Create a new family from selected geometry.",
                IconResources.FamilyFromSelect_16, IconResources.FamilyFromSelect_32);

            elementMng.AddPushButton<Cmd_GetVolume>(
                "GetVolume", "Get\nVolume",
                "For categories where Volume is not a native built-in parameter, calculates and writes the element volume into a prepared parameter.",
                IconResources.GetVolume_16, IconResources.GetVolume_32);

            elementMng.AddPushButton<Cmd_WriteElementIds>(
                "WriteElementIds", "Write\nElement IDs",
                "Write the element IDs of selected elements to a text file.",
                IconResources.EID_16, IconResources.EID_32);

            elementMng.AddSeparator();

            elementMng.AddPushButton<Cmd_OverheadAutoDash>(
                "OverheadSettings", "Overhead\nSettings",
                "Generate overhead dash line patterns for elements above the current view's cut plane.",
                IconResources.Overhead16, IconResources.Overhead32);
            elementMng.AddPushButton<ToggleOverheadProxyCommand>(
                "ToggleOverhead", "Overhead\nToggle",
                "Toggle automatic overhead dash pattern generation in the active plan view.",
                IconResources.Overhead16, IconResources.Overhead32);


            elementMng.AddPushButton<Cmd_RayBounceCeiling>(
                "RayBounceCeiling", "Element\n->Ceiling",
                "Detect the ceiling above each selected element using ray casting.",
                IconResources.RayBounce16, IconResources.RayBounce32);


            #endregion








            #region Parameter Manager (moved from ProjectPanelFactory, now retired)
            var (projPar, famPar, viewTemplateTransfer) = panel.AddStackedButtons<Cmd_RevitParameters, Cmd_FamilyParameters, Cmd_ViewTemplateTransfer>(
                    "ParameterManager", "Manage\nParameters",
                    "HarmonizeFamilyParams", "Manage\nFamily Parameters",
                    "ViewTemplateTransfer", "View Template\nTransfer",
                    IconResources.RevPar16, IconResources.FamilyParams16, IconResources.ViewTemplate16,
                     "View and manage Parameters in the active document.",
                    "Add, rename or replace parameters in project families to match BA shared parameter standards.",
                    "Transfer view templates from one view to another.");
            #endregion
            var (roomClassificationBtn, pdFin, scheduleCopyValueBtn) = panel.AddStackedPushPulldownPush<BA.RoomClassification.Commands.RoomClassificationImportCommand, Cmd_ScheduleSync>(
                    "RoomClassificationImport", "Room\nClassification",
                    "Import room classification data from an external source.",
                    IconResources.RoomCl_16,

                    "FinishToRoom", "Finishes",
                    "Transfer hosted finish element parameters up to their containing room.",
                    IconResources.Fi16,

                    "ScheduleCopyValue", "Schedule\nCopy Value",
                    "Synchronize schedule data across multiple schedules based on matching parameters.",
                    IconResources.ScheduleSync16,
                    "Finish transfer tools: extract finish codes to a room, or apply finishes to room-boundary elements."
                    );

            #region Finishes
            pdFin.AddPushButton<Cmd_FinishToRoom>(
                "FinishToRoomLocal", "Finish?\nToRoom",
                "Extracts finish codes in a room and writes the value to room.",
                IconResources.Fi16, IconResources.Fi32);
            pdFin.AddPushButton<ApplyFinishesByRoomsCommand>(
                "ApplyFinishesByRooms", "Apply Finishes",
                "Apply finish parameters to room-boundary elements based on room data.",
                IconResources.RoomFinishes16, IconResources.RoomFinishes32);
            #endregion


 
        }
    }

}
    

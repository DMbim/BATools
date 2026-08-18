using Autodesk.Revit.UI;
using BA.Commands;
using BA.Commands.Finishes;
using BA.Commands.Rooms;
using BA.Ribbon;
using BA.RoomClassification;
using BA_Tools.ScheduleExporter.Commands;
using BA.Zoom.Commands;
namespace BA.BAApplication.Ribbon
{
    internal static class RoomsPanelFactory
    {
        internal static void Build(RibbonPanel panel)
        {
            // Room Number to Element / Axis to Room / Element to Ceiling stacked together as
            // one column instead of three separate full-height items. The first two keep their
            // own pulldown of sub-items, added below exactly as before. Element to Ceiling used
            // to be a standalone push button (Cmd_RayBounceCeiling); it now takes the slot
            // Finishes used to occupy here. Finishes moved to the second stack below, next to
            // Room Classification Import and Schedule Copy Value.
            var (pdE2R, pdAxis, elementToCeilingBtn) = panel.AddStackedPulldownPulldownPush<Cmd_RayBounceCeiling>(
                "ElementToRoom", "RoomNum.\n-> Element",
                "Copy a room parameter value to elements spatially located inside that room.",
                IconResources.ElementToRoom16, IconResources.ElementToRoom32,

                "AxisToRoom", "Axis\n-> Room",
                "Place BA_Axis detail items into rooms selected by room tags.",
                IconResources.AxisToRoom16, IconResources.AxisToRoom32,

                "RayBounceCeiling", "Element\n->Ceiling",
                "Detect the ceiling above each selected element using ray casting.",
                IconResources.RayBounce16, IconResources.RayBounce32);

            #region Room Number to Element
            pdE2R.AddPushButton<Cmd_ElementToRoom_Link>(
                "ElementToRoomLink", "From Linked\nModel+++",
                "Write room data to host-model elements from rooms in a linked model.",
                IconResources.ElementToRoomLink16, IconResources.ElementToRoomLink32);
            pdE2R.AddPushButton<Cmd_ElementToRoom_Local>(
                "ElementToRoomLocal", "From Local\nModel+++",
                "Write room data to elements from rooms in the same document.",
                IconResources.ElementToRoomLocal16, IconResources.ElementToRoomLocal32);
            pdE2R.AddPushButton<Cmd_ElementToRoom_Settings>(
                "ElementToRoomSettings", "Settings",
                "Show or hide the Element \u2192 Room settings panel (category, parameters, link instance).",
                IconResources.E2r_16, IconResources.E2r_32);
            #endregion

            #region Axis to Room
            pdAxis.AddPushButton<Cmd_AxisToRoom_Link>(
                "AxisToRoomLink", "From Linked\nModel+++",
                "Resolve rooms from a linked model via selected room tags.",
                IconResources.AxisToRoomLink16, IconResources.AxisToRoomLink32);
            pdAxis.AddPushButton<Cmd_AxisToRoom_Local>(
                "AxisToRoomLocal", "From Local\nModel+++",
                "Resolve rooms from the active model via selected room tags.",
                IconResources.AxisToRoomLocal16, IconResources.AxisToRoomLocal32);
            pdAxis.AddPushButton<Cmd_AxisToRoom_Settings>(
                "AxisToRoomSettings", "Settings",
                "Configure the Revit link and BA_Axis placement used by Axis \u2192 Room.",
                IconResources.A2r_16, IconResources.A2r_32);
            #endregion

            // Room Classification Import / Finishes / Schedule Copy Value stacked together.
            // Room Classification Import moved here from QaStandardsPanelFactory (it used to
            // sit next to Classify Types there). Finishes moved here from the stack above,
            // replaced there by Element to Ceiling. Schedule Copy Value moved here from the
            // Copy pulldown in SchedulingPanelFactory, which now only holds Copy Type
            // Parameters.
            var (roomClassificationBtn, pdFin, scheduleCopyValueBtn) = panel.AddStackedPushPulldownPush<RoomClassificationImportCommand, Cmd_ScheduleSync>(
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
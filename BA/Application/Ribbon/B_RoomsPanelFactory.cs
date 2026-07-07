
using Autodesk.Revit.UI;
using BA.Commands;
using BA.Commands.Finishes;
using BA.Commands.Rooms;
using BA.Ribbon;
using BATools.Rooms.Commands;
using BATools.Zoom.Commands;

namespace BA.BAApplication.Ribbon
{
    internal static class RoomsPanelFactory
    {
        internal static void Build(RibbonPanel panel)
        {
            #region Room Number to Element


            var pdE2R = panel.AddPulldownButton<Cmd_ElementToRoom_Link>(
                "ElementToRoom", "RoomNum.\n-> Element",
                "Copy a room parameter value to elements spatially located inside that room.",
                IconResources.ElementToRoom16, IconResources.ElementToRoom32);

            pdE2R.AddPushButton<Cmd_ElementToRoom_Link>(
                "ElementToRoomLink", "From Linked\nModel+++",
                "Write room data to host-model elements from rooms in a linked model.",
                IconResources.ElementToRoomLink16, IconResources.ElementToRoomLink32);

            pdE2R.AddPushButton<Cmd_ElementToRoom_Local>(
                "ElementToRoomLocal", "From Local\nModel+++",
                "Write room data to elements from rooms in the same document.",
                IconResources.ElementToRoomLocal16, IconResources.ElementToRoomLocal32);
            #endregion

            #region Axis to Room


            var pdAxis = panel.AddPulldownButton<Cmd_AxisToRoom_Link>(
                "AxisToRoom", "Axis\n-> Room",
                "Place BA_Axis detail items into rooms selected by room tags.",
                IconResources.AxisToRoom16, IconResources.AxisToRoom32);

            pdAxis.AddPushButton<Cmd_AxisToRoom_Link>(
                "AxisToRoomLink", "From Linked\nModel+++",
                "Resolve rooms from a linked model via selected room tags.",
                IconResources.AxisToRoomLink16, IconResources.AxisToRoomLink32);

            pdAxis.AddPushButton<Cmd_AxisToRoom_Local>(
                "AxisToRoomLocal", "From Local\nModel+++",
                "Resolve rooms from the active model via selected room tags.",
                IconResources.AxisToRoomLocal16, IconResources.AxisToRoomLocal32);

            panel.AddPushButton<Cmd_RayBounceCeiling>(
                "RayBounceCeiling", "Element\n->Ceiling",
                "Detect the ceiling above each selected element using ray casting.",
                IconResources.RayBounce16, IconResources.RayBounce32);

            #endregion
            #region Finishes
            var pdFin = panel.AddPulldownButton<Cmd_FinishToRoom>(
                "FinishToRoom", "Finishes",
                "Transfer hosted finish element parameters up to their containing room.",
                IconResources.Fi16, IconResources.Fi32);
            
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
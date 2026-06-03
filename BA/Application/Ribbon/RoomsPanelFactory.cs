
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
            var pdE2R = panel.AddPulldownButton<Cmd_ElementToRoom_Link>(
                "ElementToRoom", "Element\n-> Room",
                "Copy a room parameter value to elements spatially located inside that room.",
                IconResources.ElementToRoom16, IconResources.ElementToRoom32);

            pdE2R.AddPushButton<Cmd_ElementToRoom_Link>(
                "ElementToRoomLink", "From Linked\nModel",
                "Write room data to host-model elements from rooms in a linked model.",
                IconResources.ElementToRoomLink16, IconResources.ElementToRoomLink32);

            pdE2R.AddPushButton<Cmd_ElementToRoom_Local>(
                "ElementToRoomLocal", "From Local\nModel",
                "Write room data to elements from rooms in the same document.",
                IconResources.ElementToRoomLocal16, IconResources.ElementToRoomLocal32);

            var pdAxis = panel.AddPulldownButton<Cmd_AxisToRoom_Link>(
                "AxisToRoom", "Axis\n-> Room",
                "Place BA_Axis detail items into rooms selected by room tags.",
                IconResources.AxisToRoom16, IconResources.AxisToRoom32);

            pdAxis.AddPushButton<Cmd_AxisToRoom_Link>(
                "AxisToRoomLink", "From Linked\nModel",
                "Resolve rooms from a linked model via selected room tags.",
                IconResources.AxisToRoomLink16, IconResources.AxisToRoomLink32);

            pdAxis.AddPushButton<Cmd_AxisToRoom_Local>(
                "AxisToRoomLocal", "From Local\nModel",
                "Resolve rooms from the active model via selected room tags.",
                IconResources.AxisToRoomLocal16, IconResources.AxisToRoomLocal32);

            panel.AddPushButton<Cmd_RayBounceCeiling>(
                "RayBounceCeiling", "Element\n->Ceiling",
                "Detect the ceiling above each selected element using ray casting.",
                IconResources.RayBounce16, IconResources.RayBounce32);

            panel.AddPushButton<TransferAreaValuesToRoomsCommand>(
                "AreasToRooms", "Areas\n-> Rooms",
                "Transfer area values to rooms based on their number",
                IconResources.CzechAreas16, IconResources.CzechAreas32);

            var pdFin = panel.AddPulldownButton<Cmd_FinishToRoom>(
                "FinishToRoom", "Finish\n-> Room",
                "Transfer hosted finish element parameters up to their containing room.",
                IconResources.Fi16, IconResources.Fi32);
            
            pdFin.AddPushButton<Cmd_FinishToRoom>(
                "FinishToRoomLocal", "From Local\nModel",
                "Transfer finish parameters from hosted elements to rooms in the same document.",
                IconResources.Fi16, IconResources.Fi32);
            
            pdFin.AddPushButton<ApplyFinishesByRoomsCommand>(
                "ApplyFinishesByRooms", "Apply Finishes",
                "Apply finish parameters to room-boundary elements based on room data.",
                IconResources.RoomFinishes16, IconResources.RoomFinishes32);
            
            var Zoom = panel.AddPulldownButton<Cmd_ZoomToRoom>(
                "Zoom", "Zoom\n->",
                "Zoom to rooms in the model.",
                IconResources.Zoom16, IconResources.Zoom32);
            
            Zoom.AddPushButton<Cmd_ZoomToRoom>(
                "ZoomToRoomLocal", "From Local\nModel",
                "Zoom to rooms in the active model.",
                IconResources.ZoomLo16, IconResources.ZoomLo32);
           
            Zoom.AddPushButton<Cmd_ZoomToRoom_Link>(
                "ZoomToRoomLink", "From Linked\nModel",
                "Zoom to rooms in a linked model.",
                IconResources.ZoomL16, IconResources.ZoomL32);
            
            Zoom.AddPushButton<Cmd_ZoomToSelectedElement>(
                "ZoomToSelectedElement", "To Selected\nElement",
                "Zoom to the selected element in the model.",
                IconResources.ZoomE16, IconResources.ZoomE32);
        }
    }
}
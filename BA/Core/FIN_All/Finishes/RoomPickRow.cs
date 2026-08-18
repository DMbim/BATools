using Autodesk.Revit.DB;
using System;

namespace BA.UI.Core.Finishes
{
    public sealed class RoomPickRow
    {
        public ElementId RoomId { get; }
        public string Number { get; }
        public string Name { get; }
        public string LevelName { get; }

        public string Display => $"{Number} - {Name} ({LevelName})";

        public RoomPickRow(ElementId roomId, string number, string name, string levelName)
        {
            RoomId = roomId ?? throw new ArgumentNullException(nameof(roomId));
            Number = number ?? "";
            Name = name ?? "";
            LevelName = levelName ?? "";
        }
    }
}
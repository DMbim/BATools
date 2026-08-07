using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace BA.Core.Rooms
{
    public class RoomHostFinishTransferSettings
    {
        public List<RoomHostParamMapping> Mappings { get; set; } = new();
    }

    public class RoomHostParamMapping
    {
        /// <summary>"Ceiling" or "Floor"</summary>
        public string SourceCategory { get; set; } = "Ceiling";

        /// <summary>Parameter name read from Floor/Ceiling</summary>
        public string SourceParameterName { get; set; } = "";

        /// <summary>Parameter name written to Room</summary>
        public string TargetRoomParameterName { get; set; } = "";

        /// <summary>If true, only write when target parameter is empty</summary>
        public bool WriteOnlyIfEmpty { get; set; } = true;
    }

    public class RoomHostFinishTransferResult
    {
        public int RoomsProcessed { get; set; }
        public int ValuesWritten { get; set; }
        public int Skipped { get; set; }
    }

    /// <summary>
    /// Immutable row for the room picker list in RoomHostFinishTransferWindow.
    /// Deliberately not shared with BA.UI.Core.Finishes.RoomPickRow to avoid
    /// a cross-module dependency between the Finishes and Rooms features.
    /// Selection state is tracked externally by the window (HashSet of ElementId),
    /// not on this object, so no INotifyPropertyChanged is needed here.
    /// </summary>
    public sealed class RoomPickRow
    {
        public ElementId RoomId { get; }
        public string Number { get; }
        public string Name { get; }
        public string LevelName { get; }
        public double AreaSqFt { get; }

        public string Display => $"{Number} - {Name} ({LevelName})";

        public RoomPickRow(ElementId roomId, string number, string name, string levelName, double areaSqFt)
        {
            RoomId = roomId ?? throw new ArgumentNullException(nameof(roomId));
            Number = number ?? "";
            Name = name ?? "";
            LevelName = levelName ?? "";
            AreaSqFt = areaSqFt;
        }
    }
}
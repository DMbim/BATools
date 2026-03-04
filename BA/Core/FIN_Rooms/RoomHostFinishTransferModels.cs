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
}
namespace BA.Commands.Rooms
{
    public class TransferDebugStats
    {
        public int RoomsSeen;
        public int RoomsProcessed;
        public int Written;
        public int Skipped_NoRoomSolidOrBBox;
        public int Skipped_NoSamplePoint;
        public int Skipped_NoFloorHit;
        public int Skipped_NoCeilingHit;
        public int Skipped_SourceEmpty;
        public int Skipped_TargetFail;
        public int Skipped_InvalidMapping;
    }
}
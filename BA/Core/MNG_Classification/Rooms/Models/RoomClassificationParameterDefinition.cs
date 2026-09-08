using System;

namespace BA.RoomClassification.Models
{
    internal sealed class RoomClassificationParameterDefinition
    {
        public RoomClassificationParameterDefinition(string name, Guid guid)
        {
            Name = name;
            Guid = guid;
        }

        public string Name { get; }
        public Guid Guid { get; }
    }
}

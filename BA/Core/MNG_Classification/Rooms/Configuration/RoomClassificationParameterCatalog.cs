using System;
using System.Collections.Generic;
using BA.RoomClassification.Models;

namespace BA.RoomClassification.Configuration
{
    internal static class RoomClassificationParameterCatalog
    {
        // GUIDs copied verbatim from BA_SharedParametersWIP2 (group BA_Tools).
        // These are used to defensively verify, at bind time, that the definition Revit
        // resolved by name actually IS the one this add-in expects - not a same-named
        // definition from a stale or duplicated copy of the shared parameter file.
        //
        // FinishTier and RoomFinishBase use Guid.Empty as a deliberate placeholder. Guid.Empty
        // can never equal a real ExternalDefinition.GUID, so RoomClassificationSharedParameterService
        // will throw immediately and clearly if you try to run this before adding the two new
        // parameters to the real shared parameter file. That is intentional: failing loudly here
        // is much better than silently binding to nothing, or to the wrong definition.
        public static IList<RoomClassificationParameterDefinition> BuildDefault()
        {
            return new List<RoomClassificationParameterDefinition>
            {
                new RoomClassificationParameterDefinition(RoomClassificationParameterNames.RoomKey,           new Guid("ff5efbc8-1c4e-4029-9ff2-6914cfa7dbd2")),
                new RoomClassificationParameterDefinition(RoomClassificationParameterNames.ProgramType,       new Guid("76f6c728-a27e-4db5-a0be-0ce8fb84ebde")),
                new RoomClassificationParameterDefinition(RoomClassificationParameterNames.Department,        new Guid("5fe01f22-0e6f-476d-962f-f2c4712b4b04")),
                new RoomClassificationParameterDefinition(RoomClassificationParameterNames.RoomFunction,      new Guid("a4123cc6-b061-496f-adb5-2a99c8d7c1ad")),
                new RoomClassificationParameterDefinition(RoomClassificationParameterNames.RoomCode,          new Guid("aad8447a-484d-40b5-9ef2-cddeee46b002")),
                new RoomClassificationParameterDefinition(RoomClassificationParameterNames.RoomGroup,         new Guid("85168c2e-f8b6-4e88-b975-b41b93381416")),
                new RoomClassificationParameterDefinition(RoomClassificationParameterNames.FinishTier,        new Guid("f58bb8b0-bc29-4a56-83f3-02ab0ca2eab6")), // REPLACE before running
                new RoomClassificationParameterDefinition(RoomClassificationParameterNames.RoomFinishFloor,   new Guid("7c49abd4-5b84-4ae6-8081-76687bd4fba6")),
                new RoomClassificationParameterDefinition(RoomClassificationParameterNames.RoomFinishWall,    new Guid("d731eae0-e34f-40b3-9455-7697d2602395")),
                new RoomClassificationParameterDefinition(RoomClassificationParameterNames.RoomFinishCeiling, new Guid("03a2b7d7-fb6a-45cb-ae50-13a0e46b8fa6")),
                new RoomClassificationParameterDefinition(RoomClassificationParameterNames.RoomFinishBase,    new Guid("e455df9c-f793-4d5c-9ff6-17a0645e8b42")), // REPLACE before running
            };
        }
    }
}

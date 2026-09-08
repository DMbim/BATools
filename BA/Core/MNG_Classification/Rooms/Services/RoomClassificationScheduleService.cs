using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using BA.RoomClassification.Models;

namespace BA.RoomClassification.Services
{
    internal static class RoomClassificationScheduleService
    {
        private const string ScheduleName = "BA.Tls_RoomClassification_Keys";

        public static ViewSchedule EnsureRoomKeySchedule(
            Document doc,
            IList<RoomClassificationParameterDefinition> parameterDefinitions)
        {
            ViewSchedule existing = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .FirstOrDefault(x =>
                    !x.IsTemplate &&
                    string.Equals(x.Name, ScheduleName, StringComparison.OrdinalIgnoreCase));

            ViewSchedule schedule = existing ??
                ViewSchedule.CreateKeySchedule(doc, new ElementId(BuiltInCategory.OST_Rooms));

            if (!string.Equals(schedule.Name, ScheduleName, StringComparison.OrdinalIgnoreCase))
                schedule.Name = ScheduleName;

            EnsureFields(doc, schedule, parameterDefinitions);
            return schedule;
        }

        private static void EnsureFields(
            Document doc,
            ViewSchedule schedule,
            IList<RoomClassificationParameterDefinition> parameterDefinitions)
        {
            ScheduleDefinition definition = schedule.Definition;
            IList<SchedulableField> schedulableFields = definition.GetSchedulableFields();

            foreach (RoomClassificationParameterDefinition p in parameterDefinitions)
            {
                bool fieldExists = definition.GetFieldOrder()
                    .Select(id => definition.GetField(id))
                    .Any(f => string.Equals(f.GetName(), p.Name, StringComparison.OrdinalIgnoreCase));
                if (fieldExists) continue;

                SchedulableField sf = schedulableFields.FirstOrDefault(x =>
                    string.Equals(x.GetName(doc), p.Name, StringComparison.OrdinalIgnoreCase));
                if (sf != null)
                    definition.AddField(sf);
            }
            // All eleven fields, including FinishTier and the four finish fields, are handled
            // uniformly above - they are all real shared parameters bound to Rooms via
            // RoomClassificationParameterCatalog. This method scales to whatever
            // RoomClassificationParameterCatalog.BuildDefault() returns without needing changes here.
        }
    }
}

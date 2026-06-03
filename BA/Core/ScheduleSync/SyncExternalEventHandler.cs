using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;

namespace BA
{
    internal sealed class SyncExternalEventHandler : IExternalEventHandler
    {
        private readonly object _lock = new();
        private SyncRequest? _pending;

        public void SetRequest(SyncRequest req)
        {
            lock (_lock) _pending = req;
        }

        public void Execute(UIApplication app)
        {
            SyncRequest? req;
            lock (_lock)
            {
                req = _pending;
                _pending = null;
            }

            if (req == null) return;

            var doc = app.ActiveUIDocument?.Document;
            if (doc == null)
            {
                req.Done("No active document.");
                return;
            }

            try
            {
                var result = ScheduleSyncEngine.Execute(doc, req.Schedule, req.Mappings);
                req.Done(result);
            }
            catch (Exception ex)
            {
                req.Done("Sync failed: " + ex.Message);
            }
        }

        public string GetName() => "BA.ScheduleSync";
    }

    internal sealed class SyncRequest
    {
        public Autodesk.Revit.DB.ViewSchedule Schedule { get; }
        public List<ScheduleMappingRow> Mappings { get; }
        public Action<string> Done { get; }

        public SyncRequest(
            Autodesk.Revit.DB.ViewSchedule schedule,
            List<ScheduleMappingRow> mappings,
            Action<string> done)
        {
            Schedule = schedule;
            Mappings = mappings;
            Done = done;
        }
    }
}
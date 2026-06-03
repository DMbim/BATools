using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BATools.ParamCopy.Models;
using BATools.ParamCopy.Services;
using System;
using System.Collections.Generic;

namespace BATools.ParamCopy.Handlers
{
    public class RunCopyHandler : IExternalEventHandler
    {
        private readonly object _lock = new();
        private RunCopyRequest? _request;

        public void SetRequest(RunCopyRequest req)
        {
            lock (_lock) _request = req;
        }

        public void Execute(UIApplication app)
        {
            RunCopyRequest? req;
            lock (_lock) { req = _request; _request = null; }
            if (req == null) return;

            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) { req.Done("No active document."); return; }

            try
            {
                using var tx = new Transaction(doc, "BA · Parameter Copy");
                tx.Start();
                var result = ParamCopyEngine.Execute(doc, req.Pairs, req.Mappings);
                tx.Commit();

                string msg = $"Done — Written: {result.Written}, " +
                             $"Skipped: {result.Skipped}, Errors: {result.Errors}";
                if (result.ErrorMessages.Count > 0)
                    msg += $" | First error: {result.ErrorMessages[0]}";

                System.Windows.Application.Current.Dispatcher.BeginInvoke(
                    () => req.Done(msg));
            }
            catch (Exception ex)
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(
                    () => req.Done("Copy failed: " + ex.Message));
            }
        }

        public string GetName() => "BA.ParamCopy.RunCopy";
    }

    public class RunCopyRequest
    {
        public IReadOnlyList<ElementPair> Pairs { get; }
        public IReadOnlyList<ParamMapping> Mappings { get; }
        public Action<string> Done { get; }

        public RunCopyRequest(
            IReadOnlyList<ElementPair> pairs,
            IReadOnlyList<ParamMapping> mappings,
            Action<string> done)
        {
            Pairs = pairs;
            Mappings = mappings;
            Done = done;
        }
    }
}

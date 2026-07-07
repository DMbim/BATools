using Autodesk.Revit.UI;
using BA.UI.LineStyleHub.ExternalEvents;
using System;
using System.Collections.Generic;

namespace BA.UI.LineStyleHub
{
    /// <summary>
    /// Owns the ExternalEvent and handler for the LineStyleHub window.
    /// Constructed once per window instance; holds strong references to prevent GC collection.
    /// </summary>
    public sealed class LineStyleExternalInvoker : IDisposable
    {
        private readonly ExternalEvent _exEvent;
        private readonly LineStyleExternalHandler _handler;

        public LineStyleExternalInvoker(UIApplication uiApp)
        {
            if (uiApp == null) throw new ArgumentNullException(nameof(uiApp));
            _handler = new LineStyleExternalHandler();
            _exEvent = ExternalEvent.Create(_handler);
        }

        public void ApplyEdits(
            List<LineStyleRow> rows,
            List<PatternEntry> patternEntries,
            Action<string, IReadOnlyList<string>> onDone)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (onDone == null) throw new ArgumentNullException(nameof(onDone));

            // Resolve pattern ids from names before handing off to the handler.
            // The handler runs on the Revit thread and must not touch WPF objects.
            var patternLookup = new Dictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in patternEntries)
                patternLookup[p.Name] = p.PatternId;

            foreach (var r in rows)
            {
                if (r.HasPatternChange)
                {
                    r.ResolvedPatternId = patternLookup.TryGetValue(r.PatternName, out var pid)
                        ? pid
                        : Autodesk.Revit.DB.ElementId.InvalidElementId;
                }
            }

            _handler.SetRequest(new ApplyLineStyleEditsRequest(rows, onDone));
            _exEvent.Raise();
        }

        public void Dispose()
        {
            // ExternalEvent does not implement IDisposable.
            // Kept for symmetry and future cleanup.
        }
    }
}

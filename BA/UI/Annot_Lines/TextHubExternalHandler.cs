using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.UI.TextHub.ExternalEvents
{
    internal sealed class TextHubExternalHandler : IExternalEventHandler
    {
        private readonly object _lock = new();
        private ApplyEditsRequest? _request;

        public void SetRequest(ApplyEditsRequest req)
        {
            lock (_lock) _request = req;
        }

        public void Execute(UIApplication app)
        {
            ApplyEditsRequest? req;
            lock (_lock)
            {
                req = _request;
                _request = null;
            }

            if (req == null) return;

            var uidoc = app.ActiveUIDocument;
            var doc = uidoc?.Document;

            if (doc == null)
            {
                req.Done("No active document.");
                return;
            }

            // Work on a snapshot (avoid WPF binding side-effects)
            var rows = req.Rows.ToList();

            int ok = 0;
            int fail = 0;
            var errors = new List<string>();

            try
            {
                using (var tx = new Transaction(doc, "BA · Apply text style edits"))
                {
                    tx.Start();

                    foreach (var r in rows)
                    {
                        var e = doc.GetElement(r.TypeId);
                        if (e == null)
                        {
                            fail++;
                            continue;
                        }

                        bool changedAny = false;

                        if (r.HasTextSize && r.TextSizeMm.HasValue)
                        {
                            // basic sanity
                            if (r.TextSizeMm.Value <= 0.1)
                            {
                                fail++;
                                errors.Add($"{r}: invalid size {r.TextSizeMm.Value:F2} mm");
                            }
                            else
                            {
                                if (ParamUtil.TrySetTextSizeMm(e, r.TextSizeMm.Value))
                                    changedAny = true;
                                else
                                {
                                    fail++;
                                    errors.Add($"{r}: couldn't set Text Size (read-only or missing param)");
                                }
                            }
                        }

                        if (r.HasTextFont)
                        {
                            if (!string.IsNullOrWhiteSpace(r.TextFont))
                            {
                                if (ParamUtil.TrySetTextFont(e, r.TextFont))
                                    changedAny = true;
                                else
                                {
                                    fail++;
                                    errors.Add($"{r}: couldn't set Font (read-only or missing param)");
                                }
                            }
                        }

                        if (changedAny) ok++;
                    }

                    tx.Commit();
                }

                var msg = $"Applied. OK: {ok}, Failed: {fail}";
                if (errors.Count > 0)
                {
                    // Keep it short; user can expand later into a dedicated log panel
                    msg += $" | First error: {errors[0]}";
                }
                req.Done(msg);
            }
            catch (Exception ex)
            {
                req.Done("Apply failed: " + ex.Message);
            }
        }

        public string GetName() => "BA.TextHub.ExternalHandler";
    }

    internal sealed class ApplyEditsRequest
    {
        public IReadOnlyList<TextStyleRow> Rows { get; }
        public Action<string> Done { get; }

        public ApplyEditsRequest(IReadOnlyList<TextStyleRow> rows, Action<string> done)
        {
            Rows = rows ?? throw new ArgumentNullException(nameof(rows));
            Done = done ?? throw new ArgumentNullException(nameof(done));
        }
    }
}
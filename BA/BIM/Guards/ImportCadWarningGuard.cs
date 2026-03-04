using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using BA.UI.Views.Warnings;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace BA.App.Guards
{
    /// <summary>
    /// Warns against importing CAD:
    /// - Intercepts ribbon/menu Import commands via AddInCommandBinding (BeforeExecuted)
    /// - ALSO catches "drag & drop DWG" (and other bypass paths) via ControlledApplication.DocumentChanged
    ///
    /// Drag&drop cannot be intercepted BEFORE it happens (no command id is fired).
    /// So we detect the added ImportInstance(s) and then prompt on next Idling,
    /// allowing you to delete them and optionally open Link CAD.
    /// </summary>
    public static class ImportCadWarningGuard
    {
        // ---------- Settings ----------
        public static bool Enabled { get; set; } = true;

        /// <summary>
        /// Bind generic import as well; many builds route CAD import via ID_FILE_IMPORT.
        /// </summary>
        public static bool BindGenericImport { get; set; } = true;

        // ---------- State ----------
        private static bool _suppressForSession;
        private static bool _isHandling;

        private static UIControlledApplication? _uiControlledApp;
        private static UIApplication? _cachedUiApp;
        private static bool _capturedUiAppOnce;

        // If we allow an import/link from our own prompt, ignore the resulting doc changes briefly.
        private static DateTime _ignoreDocChangesUntilUtc = DateTime.MinValue;

        // Pending CAD insertions detected via DocumentChanged (drag & drop / paste / etc.)
        private sealed class PendingCad
        {
            public Document? Doc;
            public HashSet<ElementId> AddedImportInstanceIds { get; } = new HashSet<ElementId>();
            public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        }

        private static readonly object _pendingLock = new object();
        private static PendingCad? _pending;

        private sealed class Hook
        {
            public AddInCommandBinding Binding { get; }
            public string CmdId { get; }
            public EventHandler<BeforeExecutedEventArgs> Handler { get; }

            public Hook(AddInCommandBinding binding, string cmdId, EventHandler<BeforeExecutedEventArgs> handler)
            {
                Binding = binding;
                CmdId = cmdId;
                Handler = handler;
            }
        }

        private static readonly List<Hook> _hooks = new();

        // Command IDs
        private const string CmdImportGeneric = "ID_FILE_IMPORT";
        private const string CmdImportCad = "ID_FILE_CADFORMAT_IMPORT";
        private const string CmdLinkCad = "ID_FILE_CADFORMAT_LINK";

        private static readonly string[] ImportCommandIds =
        {
            CmdImportGeneric,
            CmdImportCad,
            "ID_FILE_IMPORT_CAD",
            "ID_FILE_DWG_IMPORT"
        };

        public static void Register(UIControlledApplication app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (_hooks.Count > 0) return;

            _uiControlledApp = app;

            // Idling: capture UIApplication once AND process any pending CAD insertions.
            app.Idling += OnIdling;

            // DocumentChanged: catch drag&drop / paste / any path that inserts ImportInstance without a command binding.
            app.ControlledApplication.DocumentChanged += OnDocumentChanged;

            foreach (var cmdId in ImportCommandIds.Distinct())
            {
                if (!BindGenericImport && cmdId.Equals(CmdImportGeneric, StringComparison.OrdinalIgnoreCase))
                    continue;

                TryBind(app, cmdId);
            }

            TryWriteStartupBindingLog();
        }

        public static void Unregister(UIControlledApplication app)
        {
            if (app == null) return;

            try { app.Idling -= OnIdling; } catch { }
            try { app.ControlledApplication.DocumentChanged -= OnDocumentChanged; } catch { }

            foreach (var h in _hooks.ToList())
            {
                try { h.Binding.BeforeExecuted -= h.Handler; } catch { }
            }
            _hooks.Clear();

            foreach (var cmdId in ImportCommandIds.Distinct())
            {
                try
                {
                    var revitCmd = RevitCommandId.LookupCommandId(cmdId);
                    if (revitCmd != null)
                        app.RemoveAddInCommandBinding(revitCmd);
                }
                catch { }
            }

            _cachedUiApp = null;
            _capturedUiAppOnce = false;
            _uiControlledApp = null;

            _suppressForSession = false;
            _isHandling = false;

            lock (_pendingLock) { _pending = null; }
        }

        private static void OnIdling(object sender, IdlingEventArgs e)
        {
            // 1) Capture UIApplication once
            if (!_capturedUiAppOnce && sender is UIApplication uiapp)
            {
                _cachedUiApp = uiapp;
                _capturedUiAppOnce = true;
            }

            // 2) Process pending CAD insertions (drag & drop etc.)
            if (!Enabled) return;
            if (_suppressForSession) return;

            PendingCad? pending;
            lock (_pendingLock)
            {
                pending = _pending;
                _pending = null;
            }

            if (pending == null) return;
            if (pending.Doc == null) return;
            if (pending.AddedImportInstanceIds.Count == 0) return;

            // We must show UI on the UI thread => Idling is correct place.
            var uiApp = (sender as UIApplication) ?? _cachedUiApp;
            if (uiApp == null) return;

            // Only act if the doc is still alive and (ideally) active
            // If the user inserted into another open doc, ActiveUIDocument may differ.
            // We'll still attempt, but safely.
            TryPromptForDetectedCad(uiApp, pending.Doc, pending.AddedImportInstanceIds.ToList());
        }

        private static void OnDocumentChanged(object sender, DocumentChangedEventArgs e)
        {
            if (!Enabled) return;
            if (_suppressForSession) return;
            if (_isHandling) return;

            if (DateTime.UtcNow < _ignoreDocChangesUntilUtc)
                return;

            Document doc;
            try { doc = e.GetDocument(); }
            catch { return; }

            if (doc == null) return;

            // Look for added ImportInstance that is CAD (CADLinkType behind it).
            var added = e.GetAddedElementIds();
            if (added == null || added.Count == 0) return;

            List<ElementId> cadImports = new List<ElementId>();

            foreach (var id in added)
            {
                Element? el = null;
                try { el = doc.GetElement(id); }
                catch { }

                if (el is ImportInstance ii)
                {
                    if (IsCadImportInstance(doc, ii))
                        cadImports.Add(id);
                }
            }

            if (cadImports.Count == 0) return;

            lock (_pendingLock)
            {
                if (_pending == null || _pending.Doc != doc)
                    _pending = new PendingCad { Doc = doc };

                foreach (var id in cadImports)
                    _pending.AddedImportInstanceIds.Add(id);
            }
        }

        private static bool IsCadImportInstance(Document doc, ImportInstance ii)
        {
            // For DWG/DXF/DGN etc Revit uses CADLinkType as the type element.
            // CADLinkType represents both links and imports. (IsLink distinguishes)
            try
            {
                var type = doc.GetElement(ii.GetTypeId());
                return type is CADLinkType;
            }
            catch
            {
                return false;
            }
        }

        private static void TryPromptForDetectedCad(UIApplication uiapp, Document doc, List<ElementId> importInstanceIds)
        {
            // Validate ids still exist (user might undo/delete before idling)
            var stillThere = importInstanceIds
                .Where(id =>
                {
                    try { return doc.GetElement(id) is ImportInstance; }
                    catch { return false; }
                })
                .ToList();

            if (stillThere.Count == 0)
                return;

            _isHandling = true;
            try
            {
                var w = new ImportCadWarningWindow();
                SetOwnerToRevit(w);

                // Optional: if your WPF window supports changing text, this is where you’d set it.
                // e.g. w.MainInstruction = "CAD was added (drag & drop). What do you want to do?";

                bool? ok = w.ShowDialog();

                if (w.SuppressForSession)
                    _suppressForSession = true;

                // If user closes window oddly => do nothing (keep whatever got inserted)
                if (ok != true)
                    return;

                // Reuse your existing decisions:
                // - ContinueImport => keep inserted CAD
                // - CancelImport   => delete inserted CAD
                // - UseLinkCad     => delete inserted CAD then open Link CAD command
                if (w.Decision == ImportCadDecision.ContinueImport)
                {
                    TryLog(uiapp, "DOC_CHANGED", "KeepInsertedCad");
                    // ignore ensuing doc changes for a moment (safety)
                    _ignoreDocChangesUntilUtc = DateTime.UtcNow.AddSeconds(2);
                    return;
                }

                if (w.Decision == ImportCadDecision.CancelImport)
                {
                    TryLog(uiapp, "DOC_CHANGED", "DeleteInsertedCad");
                    DeleteElementsSafe(doc, stillThere);
                    _ignoreDocChangesUntilUtc = DateTime.UtcNow.AddSeconds(2);
                    return;
                }

                // UseLinkCad
                TryLog(uiapp, "DOC_CHANGED", "DeleteThenLinkCad");
                DeleteElementsSafe(doc, stillThere);

                _ignoreDocChangesUntilUtc = DateTime.UtcNow.AddSeconds(3);

                var linkCmd = RevitCommandId.LookupCommandId(CmdLinkCad);
                if (linkCmd != null)
                    uiapp.PostCommand(linkCmd);
            }
            finally
            {
                _isHandling = false;
            }
        }

        private static void DeleteElementsSafe(Document doc, List<ElementId> ids)
        {
            try
            {
                using var t = new Transaction(doc, "BA – Remove CAD");
                t.Start();
                doc.Delete(ids);
                t.Commit();
            }
            catch
            {
                // swallow: never crash Revit because of a guard
            }
        }

        private static void TryBind(UIControlledApplication app, string cmdId)
        {
            var revitCmd = RevitCommandId.LookupCommandId(cmdId);
            if (revitCmd == null)
                return;

            // Replace any prior binding
            try { app.RemoveAddInCommandBinding(revitCmd); } catch { }

            var binding = app.CreateAddInCommandBinding(revitCmd);

            EventHandler<BeforeExecutedEventArgs> handler = (s, e) => OnBeforeImport(cmdId, s, e);
            binding.BeforeExecuted += handler;

            _hooks.Add(new Hook(binding, cmdId, handler));
        }

        private static void OnBeforeImport(string boundCmdId, object sender, BeforeExecutedEventArgs e)
        {
            if (!Enabled) return;
            if (_suppressForSession) return;
            if (_isHandling) return;
            if (!e.Cancellable) return;

            _isHandling = true;
            try
            {
                // Grab UIApplication (sender is often UIApplication, otherwise fallback to cached Idling capture)
                var uiapp = TryGetUIApplicationFromSender(sender) ?? _cachedUiApp;

                var w = new ImportCadWarningWindow();
                SetOwnerToRevit(w);

                bool? ok = w.ShowDialog();

                if (w.SuppressForSession)
                    _suppressForSession = true;

                // If user closed dialog oddly, do nothing (let command proceed)
                if (ok != true)
                    return;

                if (w.Decision == ImportCadDecision.ContinueImport)
                {
                    TryLog(uiapp, boundCmdId, "ContinueImport");
                    // This import/link is allowed by us => ignore resulting doc changes briefly
                    _ignoreDocChangesUntilUtc = DateTime.UtcNow.AddSeconds(3);
                    return;
                }

                // Cancel the import command
                e.Cancel = true;

                if (w.Decision == ImportCadDecision.CancelImport)
                {
                    TryLog(uiapp, boundCmdId, "CancelImport");
                    return;
                }

                // Use Link CAD instead
                TryLog(uiapp, boundCmdId, "UseLinkCad");

                _ignoreDocChangesUntilUtc = DateTime.UtcNow.AddSeconds(3);

                if (uiapp != null)
                {
                    var linkCmd = RevitCommandId.LookupCommandId(CmdLinkCad);
                    if (linkCmd != null)
                        uiapp.PostCommand(linkCmd);
                }
            }
            finally
            {
                _isHandling = false;
            }
        }

        private static UIApplication? TryGetUIApplicationFromSender(object sender)
        {
            if (sender is UIApplication uiapp)
                return uiapp;

            try
            {
                var t = sender.GetType();
                var prop =
                    t.GetProperty("Application") ??
                    t.GetProperty("UIApplication") ??
                    t.GetProperty("UIApp");

                if (prop != null)
                {
                    var val = prop.GetValue(sender);
                    if (val is UIApplication uiapp2)
                        return uiapp2;
                }
            }
            catch { }

            return null;
        }

        private static void SetOwnerToRevit(System.Windows.Window wpfWindow)
        {
            try
            {
                var handle = Process.GetCurrentProcess().MainWindowHandle;
                if (handle == IntPtr.Zero) return;

                var helper = new System.Windows.Interop.WindowInteropHelper(wpfWindow)
                {
                    Owner = handle
                };
            }
            catch { }
        }

        private static void TryWriteStartupBindingLog()
        {
            try
            {
                var logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BA",
                    "Logs");

                Directory.CreateDirectory(logDir);

                var logPath = Path.Combine(logDir, "BA_ImportCadBindingLog.txt");

                var lines = new List<string>
                {
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ImportCadWarningGuard startup",
                    $"BindGenericImport = {BindGenericImport}",
                    "LookupCommandId results:"
                };

                foreach (var id in ImportCommandIds.Distinct())
                {
                    var cmd = RevitCommandId.LookupCommandId(id);
                    lines.Add($" - {id} => {(cmd == null ? "NULL" : "OK")}");
                }

                lines.Add("DocumentChanged hook: ENABLED");

                File.WriteAllLines(logPath, lines);
            }
            catch { }
        }

        private static void TryLog(UIApplication? uiapp, string boundCmdId, string action)
        {
            try { LogAttempt(uiapp, boundCmdId, action); } catch { }
        }

        private static void LogAttempt(UIApplication? uiapp, string commandId, string action)
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BA",
                "Logs");

            Directory.CreateDirectory(logDir);

            var logPath = Path.Combine(logDir, "BA_ImportCadAttempts.csv");
            var newFile = !File.Exists(logPath);

            var now = DateTime.Now;
            var user = Environment.UserName;

            var docTitle = "(NoDoc)";
            var docPath = "(NoPath)";
            var revitVersion = "(Unknown)";

            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc != null)
            {
                docTitle = doc.Title ?? "(NoTitle)";
                docPath = string.IsNullOrWhiteSpace(doc.PathName) ? "(NotSaved)" : doc.PathName;
                revitVersion = uiapp?.Application?.VersionNumber ?? "(Unknown)";
            }

            using var sw = new StreamWriter(logPath, append: true);
            if (newFile)
                sw.WriteLine("Timestamp,User,RevitVersion,CommandId,Action,DocumentTitle,DocumentPath");

            sw.WriteLine(string.Join(",",
                Csv(now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                Csv(user),
                Csv(revitVersion),
                Csv(commandId),
                Csv(action),
                Csv(docTitle),
                Csv(docPath)
            ));
        }

        private static string Csv(string? s)
        {
            s ??= "";
            if (s.Contains(",") || s.Contains("\"") || s.Contains("\n") || s.Contains("\r"))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}

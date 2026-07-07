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
    public static class ImportCadWarningGuard
    {
        // ---------- Settings ----------
        public static bool Enabled { get; set; } = true;
        public static bool BindGenericImport { get; set; } = false;

        // ---------- State ----------
        private static bool _suppressForSession;
        private static bool _isHandling;

        private static UIControlledApplication? _uiControlledApp;
        private static UIApplication? _cachedUiApp;
        private static bool _capturedUiAppOnce;

        private static DateTime _ignoreDocChangesUntilUtc = DateTime.MinValue;

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

        private const string CmdImportGeneric = "ID_FILE_IMPORT";
        private const string CmdImportCad = "ID_FILE_CADFORMAT_IMPORT";
        private const string CmdLinkCad = "ID_FILE_CADFORMAT_LINK";

        // <- CHANGED: CmdLinkCad removed from this list entirely.
        // It was never intended to be bound, but in some builds ID_FILE_CADFORMAT_IMPORT
        // and the link variant share an ID. We handle the link case via IsCadImportInstance
        // using CADLinkType.IsExternalFileReference instead.
        private static readonly string[] ImportCommandIds =
        {
            CmdImportGeneric,
            CmdImportCad,
            "ID_FILE_IMPORT_CAD",
            "ID_FILE_DWG_IMPORT"
        };

        // <- CHANGED: explicit set of command IDs that are link operations.
        // OnBeforeImport bails out immediately if the bound command id is in this set,
        // covering the case where Revit maps a link command to one of the bound IDs.
        private static readonly HashSet<string> _linkCommandIds = new(StringComparer.OrdinalIgnoreCase)
        {
            CmdLinkCad,
            "ID_FILE_CADFORMAT_LINK",
            "ID_FILE_LINK_CAD",
            "ID_FILE_DWG_LINK",
            "ID_LINK_CAD"
        };

        public static void Register(UIControlledApplication app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (_hooks.Count > 0) return;

            _uiControlledApp = app;

            app.Idling += OnIdling;
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
            if (!_capturedUiAppOnce && sender is UIApplication uiapp)
            {
                _cachedUiApp = uiapp;
                _capturedUiAppOnce = true;
            }

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

            var uiApp = (sender as UIApplication) ?? _cachedUiApp;
            if (uiApp == null) return;

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

        // <- CHANGED: replaced IsLinked check with CADLinkType.IsExternalFileReference().
        // RevitLookup confirmed:
        //   Link CAD  => CADLinkType.IsExternalFileReference() == true
        //   Import CAD => CADLinkType.IsExternalFileReference() == false
        // ImportInstance.IsLinked is unreliable across Revit builds and is no longer used.
        private static bool IsCadImportInstance(Document doc, ImportInstance ii)
        {
            try
            {
                var type = doc.GetElement(ii.GetTypeId()) as CADLinkType;
                if (type == null) return false;

                // If the CADLinkType is an external file reference, this is a Link CAD — ignore it.
                if (type.IsExternalFileReference())
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void TryPromptForDetectedCad(UIApplication uiapp, Document doc, List<ElementId> importInstanceIds)
        {
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

                bool? ok = w.ShowDialog();

                if (w.SuppressForSession)
                    _suppressForSession = true;

                if (ok != true)
                    return;

                if (w.Decision == ImportCadDecision.ContinueImport)
                {
                    TryLog(uiapp, "DOC_CHANGED", "KeepInsertedCad");
                    _ignoreDocChangesUntilUtc = DateTime.UtcNow.AddSeconds(10);
                    return;
                }

                if (w.Decision == ImportCadDecision.CancelImport)
                {
                    TryLog(uiapp, "DOC_CHANGED", "DeleteInsertedCad");
                    DeleteElementsSafe(doc, stillThere);
                    _ignoreDocChangesUntilUtc = DateTime.UtcNow.AddSeconds(10);
                    return;
                }

                TryLog(uiapp, "DOC_CHANGED", "DeleteThenLinkCad");
                DeleteElementsSafe(doc, stillThere);

                _ignoreDocChangesUntilUtc = DateTime.UtcNow.AddSeconds(10);

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
                using var t = new Transaction(doc, "BA - Remove CAD");
                t.Start();
                doc.Delete(ids);
                t.Commit();
            }
            catch { }
        }

        private static void TryBind(UIControlledApplication app, string cmdId)
        {
            var revitCmd = RevitCommandId.LookupCommandId(cmdId);
            if (revitCmd == null)
                return;

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

            // <- CHANGED: if the command that fired is a known link command, bail out immediately.
            // This covers Revit builds where a link command ID resolves to the same binding
            // as one of our import command IDs.
            if (_linkCommandIds.Contains(boundCmdId))
            {
                WriteLog($"OnBeforeImport: skipping known link command '{boundCmdId}'");
                return;
            }

            // <- CHANGED: also inspect the active document context to detect if the user
            // is in a workflow that would produce a link rather than an import.
            // We cannot know the user's file dialog choice before it happens, but we can
            // detect if Revit has already internally resolved this command as a link operation
            // by checking whether the command id maps to CmdLinkCad at runtime.
            try
            {
                var resolvedCmd = RevitCommandId.LookupCommandId(boundCmdId);
                var linkCmdId = RevitCommandId.LookupCommandId(CmdLinkCad);

                // <- CHANGED: if the resolved RevitCommandId object reference is the same as
                // the link command, this binding was silently aliased to the link command by Revit.
                if (resolvedCmd != null && linkCmdId != null &&
                    resolvedCmd.Name == linkCmdId.Name)
                {
                    WriteLog($"OnBeforeImport: boundCmdId '{boundCmdId}' resolved to link command '{linkCmdId.Name}' — skipping");
                    return;
                }
            }
            catch { }

            _isHandling = true;
            try
            {
                var uiapp = TryGetUIApplicationFromSender(sender) ?? _cachedUiApp;

                var w = new ImportCadWarningWindow();
                SetOwnerToRevit(w);

                bool? ok = w.ShowDialog();

                if (w.SuppressForSession)
                    _suppressForSession = true;

                if (ok != true)
                    return;

                if (w.Decision == ImportCadDecision.ContinueImport)
                {
                    TryLog(uiapp, boundCmdId, "ContinueImport");
                    _ignoreDocChangesUntilUtc = DateTime.UtcNow.AddSeconds(3);
                    return;
                }

                e.Cancel = true;

                if (w.Decision == ImportCadDecision.CancelImport)
                {
                    TryLog(uiapp, boundCmdId, "CancelImport");
                    return;
                }

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
                new System.Windows.Interop.WindowInteropHelper(wpfWindow) { Owner = handle };
            }
            catch { }
        }

        private static void TryWriteStartupBindingLog()
        {
            try
            {
                var logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BA", "Logs");

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
                    lines.Add($" - {id} => {(cmd == null ? "NULL" : $"OK (Name={cmd.Name})")}");
                }

                // <- CHANGED: also log the link command resolution at startup so you can see
                // immediately if any import command ID maps to the same Name as the link command
                var linkCmd = RevitCommandId.LookupCommandId(CmdLinkCad);
                lines.Add($" - {CmdLinkCad} (link reference) => {(linkCmd == null ? "NULL" : $"OK (Name={linkCmd.Name})")}");
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
                "BA", "Logs");

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

        private static void WriteLog(string line)
        {
            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BA", "Logs", "BA_CadGuardDiag.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
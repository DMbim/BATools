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
    /// Intercepts Import command(s) and warns against importing CAD.
    /// Works in Revit variants where "Import CAD" is routed through ID_FILE_IMPORT. :contentReference[oaicite:1]{index=1}
    /// </summary>
    public static class ImportCadWarningGuard
    {
        // ---------- Settings ----------
        public static bool Enabled { get; set; } = true;

        /// <summary>
        /// Bind both CAD-specific and generic Import.
        /// Recommended ON because many Revit builds route CAD import via ID_FILE_IMPORT.
        /// </summary>
        public static bool BindGenericImport { get; set; } = true;

        // ---------- State ----------
        private static bool _suppressForSession;
        private static bool _isHandling;

        private static UIControlledApplication? _uiControlledApp;
        private static UIApplication? _cachedUiApp;
        private static bool _capturedUiAppOnce;

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
        private const string CmdImportGeneric = "ID_FILE_IMPORT";          // commonly used for Import CAD routing :contentReference[oaicite:2]{index=2}
        private const string CmdImportCad = "ID_FILE_CADFORMAT_IMPORT";    // exists in some builds
        private const string CmdLinkCad = "ID_FILE_CADFORMAT_LINK";

        // Extra fallbacks (safe: LookupCommandId may return null)
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

            // Capture UIApplication once via Idling (sender is UIApplication)
            app.Idling += OnIdlingCaptureUiApp;

            foreach (var cmdId in ImportCommandIds.Distinct())
            {
                if (!BindGenericImport && cmdId.Equals(CmdImportGeneric, StringComparison.OrdinalIgnoreCase))
                    continue;

                TryBind(app, cmdId);
            }

            // Optional: write a small startup log of which command IDs are available
            TryWriteStartupBindingLog();
        }

        public static void Unregister(UIControlledApplication app)
        {
            if (app == null) return;

            try { app.Idling -= OnIdlingCaptureUiApp; } catch { }

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
        }

        private static void OnIdlingCaptureUiApp(object sender, IdlingEventArgs e)
        {
            if (_capturedUiAppOnce) return;
            if (sender is UIApplication uiapp)
            {
                _cachedUiApp = uiapp;
                _capturedUiAppOnce = true;

                try { _uiControlledApp!.Idling -= OnIdlingCaptureUiApp; } catch { }
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

                // Continue import (do NOT cancel) => command proceeds after this handler returns
                if (w.Decision == ImportCadDecision.ContinueImport)
                {
                    TryLog(uiapp, boundCmdId, "ContinueImport");
                    return;
                }

                // Otherwise cancel the import command
                e.Cancel = true;

                if (w.Decision == ImportCadDecision.CancelImport)
                {
                    TryLog(uiapp, boundCmdId, "CancelImport");
                    return;
                }

                // Use Link CAD
                TryLog(uiapp, boundCmdId, "UseLinkCad");

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

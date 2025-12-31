using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using BA.UI.Views.Warnings;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace BA.App.Guards
{
    /// <summary>
    /// Shows a warning when user attempts to Import (CAD) and offers Link CAD instead.
    /// Register from your main IExternalApplication OnStartup.
    /// </summary>
    public static class ImportCadWarningGuard
    {
        private static bool _suppressForSession;
        private static AddInCommandBinding? _binding;

        // Revit built-in UI command ids (more reliable than custom strings).
        // Import command covers many formats, but this is what "Import CAD" routes through.
        private const string CmdImport = "ID_FILE_IMPORT";
        private const string CmdLinkCad = "ID_FILE_CADFORMAT_LINK";

        public static void Register(UIControlledApplication app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (_binding != null) return; // already registered

            var importCmd = RevitCommandId.LookupCommandId(CmdImport);
            if (importCmd == null) return;

            // Replace/ensure our binding
            try { app.RemoveAddInCommandBinding(importCmd); } catch { /* ok */ }

            _binding = app.CreateAddInCommandBinding(importCmd);

            // Important: cancel is supported in BeforeExecuted (not reliably in Executed).
            _binding.BeforeExecuted += OnBeforeImport;
        }

        public static void Unregister(UIControlledApplication app)
        {
            if (_binding == null) return;

            try { _binding.BeforeExecuted -= OnBeforeImport; } catch { /* ok */ }
            _binding = null;
        }

        private static void OnBeforeImport(object sender, BeforeExecutedEventArgs e)
        {
            if (_suppressForSession) return;
            if (!e.Cancellable) return;

            // NOTE: For command bindings, sender is often UIApplication. If not, we fall back gracefully.
            var uiapp = sender as UIApplication;

            var w = new ImportCadWarningWindow();
            SetOwnerToRevit(w);

            bool? dialog = w.ShowDialog();

            if (w.SuppressForSession)
                _suppressForSession = true;

            // Always cancel the import command
            e.Cancel = true;

            // Log attempt (even if user clicks "Use Link CAD" - still attempted import)
            try { LogImportAttempt(uiapp); } catch { /* don't break UX */ }

            // If they want to link, try to post the Link CAD command
            if (dialog == true && uiapp != null)
            {
                var linkCmd = RevitCommandId.LookupCommandId(CmdLinkCad);
                if (linkCmd != null)
                    uiapp.PostCommand(linkCmd);
            }
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

        private static void LogImportAttempt(UIApplication? uiapp)
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

            if (uiapp?.ActiveUIDocument?.Document != null)
            {
                var doc = uiapp.ActiveUIDocument.Document;
                docTitle = doc.Title ?? "(NoTitle)";
                docPath = string.IsNullOrWhiteSpace(doc.PathName) ? "(NotSaved)" : doc.PathName;
                revitVersion = uiapp.Application.VersionNumber ?? "(Unknown)";
            }

            using var sw = new StreamWriter(logPath, append: true);
            if (newFile)
            {
                sw.WriteLine("Timestamp,User,RevitVersion,DocumentTitle,DocumentPath");
            }

            // CSV-safe-ish (simple escaping)
            sw.WriteLine(string.Join(",",
                Csv(now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                Csv(user),
                Csv(revitVersion),
                Csv(docTitle),
                Csv(docPath)
            ));
        }

        private static string Csv(string s)
        {
            s ??= "";
            if (s.Contains(",") || s.Contains("\"") || s.Contains("\n") || s.Contains("\r"))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}

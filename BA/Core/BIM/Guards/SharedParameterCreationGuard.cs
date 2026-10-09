using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using BA.Core.Settings;
using BA.UI.Views.Warnings;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Interop;

namespace BA.App.Guards
{
    /// <summary>
    /// Blocks the Edit Shared Parameters dialog for anyone who is not a registered
    /// Revit Leader. Hooked through DialogBoxShowing (the dialog is reachable from
    /// Manage, Project Parameters, Family Types and schedule fields, so a single
    /// command binding would miss most paths). The dialog is dismissed with
    /// OverrideResult, and the explanation window is shown later from Idling.
    /// The guard is always on and fails closed.
    /// </summary>
    public static class SharedParameterCreationGuard
    {
        private const string BlockedDialogId = "Dialog_Revit_ExternalParamEdit";
        private const int DialogResultCancel = 2;

        private sealed class PendingNotice
        {
            public PendingNotice(SharedParameterBlockReason reason, string autodeskUsername)
            {
                Reason = reason;
                AutodeskUsername = autodeskUsername;
            }

            public SharedParameterBlockReason Reason { get; }
            public string AutodeskUsername { get; }
        }

        private sealed class Decision
        {
            private Decision(bool allowed, SharedParameterBlockReason reason)
            {
                Allowed = allowed;
                Reason = reason;
            }

            public bool Allowed { get; }
            public SharedParameterBlockReason Reason { get; }

            public static Decision Allow()
            {
                return new Decision(true, SharedParameterBlockReason.NotLeader);
            }

            public static Decision Block(SharedParameterBlockReason reason)
            {
                return new Decision(false, reason);
            }
        }

        private static readonly object _stateLock = new object();
        private static readonly object _logLock = new object();

        private static bool _registered;
        private static PendingNotice? _pending;
        private static bool _isShowingNotice;
        private static UIApplication? _cachedUiApp;
        private static bool _capturedUiAppOnce;

        public static void Register(UIControlledApplication app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (_registered) return;

            app.DialogBoxShowing += OnDialogBoxShowing;
            app.Idling += OnIdling;
            _registered = true;

            WriteRuntimeLog("SharedParameterCreationGuard registered.");
        }

        public static void Unregister(UIControlledApplication app)
        {
            if (app == null) return;

            try { app.DialogBoxShowing -= OnDialogBoxShowing; } catch { }
            try { app.Idling -= OnIdling; } catch { }

            lock (_stateLock)
            {
                _pending = null;
            }

            _cachedUiApp = null;
            _capturedUiAppOnce = false;
            _isShowingNotice = false;
            _registered = false;

            WriteRuntimeLog("SharedParameterCreationGuard unregistered.");
        }

        private static void OnDialogBoxShowing(object sender, DialogBoxShowingEventArgs e)
        {
            try
            {
                if (!string.Equals(e.DialogId, BlockedDialogId, StringComparison.Ordinal))
                    return;

                UIApplication? uiapp = (sender as UIApplication) ?? _cachedUiApp;
                string username = ReadAutodeskUsername(uiapp);

                Decision decision = Evaluate(username);

                if (decision.Allowed)
                {
                    LogAttempt(uiapp, username, "Allowed", "Leader");
                    return;
                }

                bool overridden = false;

                try
                {
                    e.OverrideResult(DialogResultCancel);
                    overridden = true;
                }
                catch (Exception ex)
                {
                    WriteRuntimeLog("OverrideResult failed: " + ex.GetType().Name + " " + ex.Message);
                }

                LogAttempt(uiapp, username, overridden ? "Blocked" : "BlockFailed", decision.Reason.ToString());

                if (overridden)
                {
                    lock (_stateLock)
                    {
                        _pending = new PendingNotice(decision.Reason, username);
                    }
                }
            }
            catch (Exception ex)
            {
                WriteRuntimeLog("OnDialogBoxShowing error: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        private static Decision Evaluate(string username)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(username))
                    return Decision.Block(SharedParameterBlockReason.NotSignedIn);

                LeaderRegistrySnapshot snapshot = RevitLeaderRegistry.Load();

                if (snapshot.Status == LeaderRegistryStatus.Unavailable)
                {
                    WriteRuntimeLog("Leader registry unavailable: " + snapshot.Error);
                    return Decision.Block(SharedParameterBlockReason.RegistryUnavailable);
                }

                if (snapshot.Status == LeaderRegistryStatus.Empty)
                    return Decision.Block(SharedParameterBlockReason.NoLeadersRegistered);

                if (RevitLeaderRegistry.IsLeader(snapshot, username))
                    return Decision.Allow();

                return Decision.Block(SharedParameterBlockReason.NotLeader);
            }
            catch (Exception ex)
            {
                WriteRuntimeLog("Evaluate error: " + ex.GetType().Name + " " + ex.Message);
                return Decision.Block(SharedParameterBlockReason.RegistryUnavailable);
            }
        }

        private static void OnIdling(object sender, IdlingEventArgs e)
        {
            if (!_capturedUiAppOnce && sender is UIApplication capturedApp)
            {
                _cachedUiApp = capturedApp;
                _capturedUiAppOnce = true;
            }

            PendingNotice? notice;

            lock (_stateLock)
            {
                notice = _pending;
                _pending = null;
            }

            if (notice == null) return;
            if (_isShowingNotice) return;

            _isShowingNotice = true;

            try
            {
                ShowNotice((sender as UIApplication) ?? _cachedUiApp, notice);
            }
            finally
            {
                _isShowingNotice = false;
            }
        }

        private static void ShowNotice(UIApplication? uiapp, PendingNotice notice)
        {
            try
            {
                var window = new SharedParameterBlockedWindow(notice.Reason, notice.AutodeskUsername);
                SetOwnerToRevit(window, uiapp);
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                WriteRuntimeLog("Block window failed: " + ex.GetType().Name + " " + ex.Message);

                try
                {
                    Autodesk.Revit.UI.TaskDialog.Show(
                        "BA | Shared parameters restricted",
                        "Creating and editing shared parameters is restricted to Revit Leaders. Contact BIM Manager or Revit Leader.");
                }
                catch
                {
                }
            }
        }

        private static void SetOwnerToRevit(System.Windows.Window window, UIApplication? uiapp)
        {
            try
            {
                IntPtr handle = IntPtr.Zero;

                try
                {
                    if (uiapp != null)
                        handle = uiapp.MainWindowHandle;
                }
                catch
                {
                }

                if (handle == IntPtr.Zero)
                    handle = Process.GetCurrentProcess().MainWindowHandle;

                if (handle == IntPtr.Zero) return;

                new WindowInteropHelper(window) { Owner = handle };
            }
            catch
            {
            }
        }

        private static string ReadAutodeskUsername(UIApplication? uiapp)
        {
            try
            {
                return (uiapp?.Application?.Username ?? "").Trim();
            }
            catch
            {
                return "";
            }
        }

        // ---------------- Logging ----------------

        private static string LogDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BA", "Logs");
        }

        private static void LogAttempt(UIApplication? uiapp, string autodeskUser, string outcome, string reason)
        {
            try
            {
                string dir = LogDirectory();
                Directory.CreateDirectory(dir);

                string path = Path.Combine(dir, "BA_SharedParamGuardAttempts.csv");

                string docTitle = "(NoDoc)";
                string revitVersion = "(Unknown)";

                try
                {
                    var doc = uiapp?.ActiveUIDocument?.Document;
                    if (doc != null)
                        docTitle = string.IsNullOrWhiteSpace(doc.Title) ? "(NoTitle)" : doc.Title;

                    revitVersion = uiapp?.Application?.VersionNumber ?? "(Unknown)";
                }
                catch
                {
                }

                lock (_logLock)
                {
                    bool newFile = !File.Exists(path);

                    using (var sw = new StreamWriter(path, append: true))
                    {
                        if (newFile)
                            sw.WriteLine("Timestamp,WindowsUser,AutodeskUser,RevitVersion,Outcome,Reason,DocumentTitle");

                        sw.WriteLine(string.Join(",",
                            Csv(DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture)),
                            Csv(Environment.UserName),
                            Csv(string.IsNullOrWhiteSpace(autodeskUser) ? "(none)" : autodeskUser),
                            Csv(revitVersion),
                            Csv(outcome),
                            Csv(reason),
                            Csv(docTitle)));
                    }
                }
            }
            catch
            {
            }
        }

        private static string Csv(string? s)
        {
            s ??= "";

            if (s.Contains(",") || s.Contains("\"") || s.Contains("\n") || s.Contains("\r"))
                return "\"" + s.Replace("\"", "\"\"") + "\"";

            return s;
        }

        private static void WriteRuntimeLog(string line)
        {
            try
            {
                string dir = LogDirectory();
                Directory.CreateDirectory(dir);

                string path = Path.Combine(dir, "BA_SharedParamGuardLog.txt");
                string stamp = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

                lock (_logLock)
                {
                    File.AppendAllText(path, "[" + stamp + "] " + line + Environment.NewLine);
                }
            }
            catch
            {
            }
        }
    }
}
// File: BA_Tools/UI/BimHub/Views/BimHubWindow.xaml.cs
using Autodesk.Revit.UI;
using BA.Classification;
using BA.Commands.Families;
using BA.Commands.Views.ScopeBoxes;
using BA.IssueReporter.Commands;
using BA.RoomClassification;
using BA.Subcategories.Commands;
using BA.UI.BimHub.Models;
using BA.UI.BimHub.Services;
using BA.UI.ExternalEvents;
using BA.UI.Helpers;
using BA.UI.LineStyleHub;
using BA.UI.Management;
using BA.UI.TextHub;
using BA.UI.Views;
using BA.UI.ViewTemplates;
using BA_Tools.ScheduleExporter.Commands;
using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Color = System.Windows.Media.Color;

namespace BA.UI.BimHub.Views
{
    public partial class BimHubWindow : Window, IDisposable
    {
        private readonly UIApplication _uiApp;
        private readonly RevitActionQueueHandler _handler;
        private readonly Autodesk.Revit.UI.ExternalEvent _externalEvent;
        private readonly RevitExternalInvoker _invoker;
        private bool _disposed;
        private LineStyleExternalInvoker? _lineStyleInvoker;

        private Document? ActiveDoc => _uiApp.ActiveUIDocument?.Document;

        public BimHubWindow(UIApplication uiApp)
        {
            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));

            _handler = new RevitActionQueueHandler(Dispatcher.CurrentDispatcher);
            _externalEvent = Autodesk.Revit.UI.ExternalEvent.Create(_handler);
            _invoker = new RevitExternalInvoker(_handler, _externalEvent);

            InitializeComponent();
            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RevitWindowHelper.SetOwnerToRevit(this, _uiApp);
            RefreshHealthCard();
        }

        private void OnClosed(object sender, EventArgs e)
        {
            Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }

        // ── Health card ──────────────────────────────────────────────────────
        private void RefreshHealthCard()
        {
            _invoker.Run<BimHubHealthSnapshot>(
                uiApp => BimHubHealthService.Collect(uiApp),
                onCompleted: ApplySnapshot,
                onError: ex => Dispatcher.Invoke(() =>
                {
                    TxtStatusSummary.Text = "Health check failed — see journal";
                    TxtCheckedAt.Text = string.Empty;
                }));
        }

        private void ApplySnapshot(BimHubHealthSnapshot s)
        {
            Dispatcher.Invoke(() =>
            {
                TxtParamsLoaded.Text = s.ParamsLoaded.ToString();
                TxtQaWarnings.Text = s.QaWarnings.ToString();
                TxtQaErrors.Text = s.QaErrors.ToString();
                TxtTemplateVersion.Text = s.TemplateVersion;
                TxtCheckedAt.Text = $"Last checked: {s.CheckedAtFormatted}";
                TxtStatusSummary.Text = s.StatusSummary + "  ·  ";

                StatusDot.Fill = s.HasErrors
                    ? new SolidColorBrush(Color.FromRgb(0xE0, 0x52, 0x52))
                    : s.HasWarnings
                        ? new SolidColorBrush(Color.FromRgb(0xE8, 0xA0, 0x30))
                        : new SolidColorBrush(Color.FromRgb(0x3D, 0xB8, 0x7A));

                TxtQaWarnings.Foreground = s.QaWarnings > 0
                    ? new SolidColorBrush(Color.FromRgb(0xE8, 0xA0, 0x30))
                    : new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x72));

                TxtQaErrors.Foreground = s.QaErrors > 0
                    ? new SolidColorBrush(Color.FromRgb(0xE0, 0x52, 0x52))
                    : new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x72));
            });
        }

        // ── Command runner ───────────────────────────────────────────────────
        private void RunCommand(
            string commandName,
            Action<UIApplication>? implementation = null,
            bool closeHub = true)
        {
            if (closeHub) Close();

            if (implementation != null)
            {
                _invoker.Run(
                    implementation,
                    onError: ex => ShowError(commandName, ex));
            }
            else
            {
                TaskDialog.Show(
                    "BA BIM Hub",
                    $"{commandName}\n\nNot yet extracted to a service.\n" +
                    "Wire it in BimHubWindow.xaml.cs when ready.");
            }
        }

        private static void ShowError(string commandName, Exception ex)
            => TaskDialog.Show("BA BIM Hub — Error", $"{commandName} failed:\n\n{ex.Message}");

        // ── STANDARDS ────────────────────────────────────────────────────────
        private void BtnSyncLineStyles_Click(object sender, RoutedEventArgs e)
        {
            RunCommand("Line Style Manager", uiApp =>
            {
                var doc = ActiveDoc;
                if (doc == null)
                {
                    TaskDialog.Show("BA - Project Tools", "No active document.");
                    return;
                }

                _lineStyleInvoker ??= new LineStyleExternalInvoker(uiApp);

                var wnd = LineStyleHubWindow.GetOrCreate(uiApp, _lineStyleInvoker);
                RevitWindowHelper.SetOwnerToRevit(wnd, uiApp);
                wnd.Show();
                wnd.Activate();
            });
        }

        private void BtnTransferViewTemplate_Click(object sender, RoutedEventArgs e)
        {
            RunCommand("Transfer View Template", uiApp =>
            {
                var doc = uiApp.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    TaskDialog.Show("BA - Project Tools", "No active document.");
                    return;
                }

                var wnd = TemplateTransferWindow.GetOrCreate(uiApp);
                RevitWindowHelper.SetOwnerToRevit(wnd, uiApp);
                wnd.Show();
                wnd.Activate();
            });
        }

        // ── FAMILIES ─────────────────────────────────────────────────────────
        private void BtnSaveFamilies_Click(object sender, RoutedEventArgs e)
            => RunCommand("Save Families");

        private void BtnSaveToolsHub_Click(object sender, RoutedEventArgs e)
            => RunCommand("Save Tools Hub");

        private void BtnContentBrowser_Click(object sender, RoutedEventArgs e)
            => RunCommand("Content Browser");

        private void BtnFamilyTools_Click(object sender, RoutedEventArgs e)
            => RunCommand("Family Tools");
        private void BtnReportFamilySubcategories_Click(object sender, RoutedEventArgs e)
        {
            RunCommand("Report Family Subcategories", uiApp =>
            {
                string msg = string.Empty;
                Cmd_ReportFamilySubcategories.Run(uiApp, ref msg);
                if (!string.IsNullOrWhiteSpace(msg))
                    ShowError("Report Family Subcategories", new Exception(msg));
            });
        }

        private void BtnSubcategoryManager_Click(object sender, RoutedEventArgs e)
        {
            RunCommand("Subcategory Manager", uiApp =>
            {
                string msg = string.Empty;
                SubcategoryManagerCommand.Run(uiApp, ref msg);
                if (!string.IsNullOrWhiteSpace(msg))
                    ShowError("Subcategory Manager", new Exception(msg));
            });
        }
        // ── ROOMS & CLASSIFICATION ────────────────────────────────────────────
        private void BtnAreasToRooms_Click(object sender, RoutedEventArgs e)
            => RunCommand("Areas → Rooms");

        private void BtnColourScheme_Click(object sender, RoutedEventArgs e)
        {
            RunCommand("Colour Scheme", uiApp =>
            {
                var doc = ActiveDoc;
                if (doc == null) { TaskDialog.Show("BA - Project Tools", "No active document."); return; }

                var wnd = BAViewFilterColorManager.GetOrCreate(_uiApp, _invoker);
                RevitWindowHelper.SetOwnerToRevit(wnd, _uiApp);
                wnd.Show();
                wnd.Activate();
            });
        }

        private void BtnTextHub_Click(object sender, RoutedEventArgs e)
        {
            RunCommand("Text Hub", uiApp =>
            {
                var doc = ActiveDoc;
                if (doc == null) { TaskDialog.Show("BA - Project Tools", "No active document."); return; }

                var invoker = new TextHub.ExternalEvents.TextHubExternalInvoker(_invoker, uiApp);
                var wnd = TextHubWindow.GetOrCreate(uiApp, invoker);
                wnd.Show();
                wnd.Activate();
            });
        }

        private void BtnClassifyElements_Click(object sender, RoutedEventArgs e)
        {
            RunCommand("Classify Elements", uiApp =>
            {
                string msg = string.Empty;
                Cmd_ClassifyElements.Run(uiApp, ref msg);
                if (!string.IsNullOrWhiteSpace(msg))
                    ShowError("Classify Elements", new Exception(msg));
            });
        }

        private void BtnRoomClassificationImport_Click(object sender, RoutedEventArgs e)
        {
            RunCommand("Import Room Classification", uiApp =>
            {
                string msg = string.Empty;
                RoomClassificationImportCommand.Run(uiApp, ref msg);
                if (!string.IsNullOrWhiteSpace(msg))
                    ShowError("Import Room Classification", new Exception(msg));
            });
        }

        // ── PROJECT ───────────────────────────────────────────────────────────
        private void BtnProjectMonitor_Click(object sender, RoutedEventArgs e)
            => RunCommand("Project Monitor");

        private void BtnQaCenter_Click(object sender, RoutedEventArgs e)
            => RunCommand("QA Center");

        private void BtnScopeBoxManager_Click(object sender, RoutedEventArgs e)
        {
            RunCommand("Scope Box Manager", uiApp =>
            {
                string msg = string.Empty;
                ScopeBoxManagerCommand.Run(uiApp, ref msg);
                if (!string.IsNullOrWhiteSpace(msg))
                    ShowError("Scope Box Manager", new Exception(msg));
            });
        }

        // BimHubWindow.xaml.cs — only this handler changes, one argument added
        private void BtnTemplateChecker_Click(object sender, RoutedEventArgs e)
        {
            RunCommand("Template Checker", uiApp =>
            {
                var doc = uiApp.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    TaskDialog.Show("BA - Project Tools", "No active document.");
                    return;
                }

                var wnd = new TemplateCheckerWindow(uiApp, doc, _invoker); // <- CHANGED: added _invoker
                RevitWindowHelper.SetOwnerToRevit(wnd, uiApp);
                wnd.Show();
                wnd.Activate();
            });
        }

        private void BtnCleanUp_Click(object sender, RoutedEventArgs e)
            => RunCommand("Clean up & Maintenance");

        // ── OUTCOME ───────────────────────────────────────────────────────────
        private void BtnExportSchedule_Click(object sender, RoutedEventArgs e)
        {
            RunCommand("Export Schedule", uiApp =>
            {
                string msg = string.Empty;
                ExportScheduleCommand.Run(uiApp, ref msg);
                if (!string.IsNullOrWhiteSpace(msg))
                    ShowError("Export Schedule", new Exception(msg));
            });
        }

        private void BtnImportSchedule_Click(object sender, RoutedEventArgs e)
        {
            RunCommand("Import Schedule", uiApp =>
            {
                string msg = string.Empty;
                ImportScheduleCommand.Run(uiApp, ref msg);
                if (!string.IsNullOrWhiteSpace(msg))
                    ShowError("Import Schedule", new Exception(msg));
            });
        }

        // ── ISSUE REPORTER ────────────────────────────────────────────────────
        private void BtnSubmitIssue_Click(object sender, RoutedEventArgs e)
        {
            RunCommand("Submit Issue", uiApp =>
            {
                string msg = string.Empty;
                SubmitIssueCommand.Run(uiApp, ref msg);
                if (!string.IsNullOrWhiteSpace(msg))
                    ShowError("Submit Issue", new Exception(msg));
            });
        }

        private void BtnManageIssues_Click(object sender, RoutedEventArgs e)
        {
            RunCommand("Manage Issues", uiApp =>
            {
                string msg = string.Empty;
                ManageIssuesCommand.Run(uiApp, ref msg);
                if (!string.IsNullOrWhiteSpace(msg))
                    ShowError("Manage Issues", new Exception(msg));
            });
        }

        private void BtnIssueReporterSettings_Click(object sender, RoutedEventArgs e)
        {
            RunCommand("Issue Reporter Settings", uiApp =>
            {
                string msg = string.Empty;
                IssueReporterSettingsCommand.Run(uiApp, ref msg);
                if (!string.IsNullOrWhiteSpace(msg))
                    ShowError("Issue Reporter Settings", new Exception(msg));
            });
        }

        // ── FOOTER ────────────────────────────────────────────────────────────
        private void BtnClose_Click(object sender, RoutedEventArgs e)
            => Close();
    }
}

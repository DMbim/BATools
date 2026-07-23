using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using BA.App.Guards;
using BA.App.Overhead;
using BA.App.Settings;
using BA.BAApplication.Ribbon;
using BA.Core.Overhead;
using BA.QA.FamilyVersioning.Hook;
using BA.Telemetry.Infrastructure;
using BA.Telemetry.Services;
using BATools.SelectionManager.Infrastructure;
using Nice3point.Revit.Toolkit.External;
using System;
using System.Collections.Generic;
using System.Text;
using ExternalEvent = Autodesk.Revit.UI.ExternalEvent;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace BA.BAApplication
{
    public sealed class BaApplication : ExternalApplication
    {
        private TelemetryService _telemetryService;
        private PostableCommandInterceptor _commandInterceptor;
        private FamilyVersioningDocumentHook _familyVersioningHook;
        private BA.Core.Export.Infrastructure.ExportScheduler _exportScheduler;

        public override void OnStartup()
        {
            const string tabName = "BA_Tools";


            try
            {
                BA.Updates.UpdateService.Register(Application);
                OverheadProxyUpdater.Register(Application);
                ImportCadWarningGuard.Register(Application);
                FamilyImportWarningGuardV2.Register(Application);
                Application.ControlledApplication.DocumentSynchronizingWithCentral += OnDocumentSynchronizingWithCentral;
                Application.ControlledApplication.DocumentSynchronizedWithCentral += OnDocumentSynchronizedWithCentral;
                PluginSettingsBootstrap.ApplySavedSettingsToRuntime();
                OverheadToggleController.Initialize(Application);
                SelectionManagerActivator.Instance.Initialize(Application);

                _exportScheduler = new BA.Core.Export.Infrastructure.ExportScheduler();
                Application.Idling += _exportScheduler.OnIdling;

                RibbonPanel panelAnnotation = Application.CreatePanel("Graphics\nAnnotation", tabName);
                RibbonPanel panelRooms = Application.CreatePanel("Rooms", tabName);
                RibbonPanel panelFamilies = Application.CreatePanel("Families & Content", tabName);
                RibbonPanel panelProject = Application.CreatePanel("Project", tabName);
                RibbonPanel panelUtilities = Application.CreatePanel("Utilities", tabName);

                // BA BIM tab — single hub button, deployed to BIM managers only     // <- NEW
                var bimTabName = "BA_BIM";                                            // <- NEW
                RibbonPanel panelBimHub = Application.CreatePanel("BIM Hub", bimTabName); // <- NEW
                BimHubPanelFactory.Build(panelBimHub);

                AnnotationPanelFactory.Build(panelAnnotation);
                RoomsPanelFactory.Build(panelRooms);
                FamiliesPanelFactory.Build(panelFamilies);
                ProjectPanelFactory.Build(panelProject);
                ;

                AppLogger.LogInfo("BATools startup completed successfully.");
            }
            catch (Exception ex)
            {
                AppLogger.LogError("OnStartup", ex);
                TaskDialog.Show("BA_Tools – Startup error", ex.ToString());
            }

            try
            {
                _telemetryService = new TelemetryService(Application); // <- FIXED: capital A
                _telemetryService.Start();
                Application.Idling += OnFirstIdling; // <- ADDED: Idling subscription
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BA.Telemetry] Failed to start TelemetryService: {ex.Message}");
            }
        }

        private void OnFirstIdling(object sender, Autodesk.Revit.UI.Events.IdlingEventArgs e)
        {
            try
            {
                UIApplication uiApp = sender as UIApplication;

                if (uiApp == null)
                    return;

                if (_telemetryService != null && _commandInterceptor == null)
                {
                    _commandInterceptor = new PostableCommandInterceptor(uiApp, _telemetryService);
                    _commandInterceptor.Register();
                    _familyVersioningHook = new FamilyVersioningDocumentHook(
                        Application.ControlledApplication, uiApp);
                    _familyVersioningHook.Register();
                    _familyVersioningHook.SeedOpenDocuments();
                }


            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BA.Telemetry] Interceptor registration failed: {ex.Message}");
            }
            finally
            {
                if (sender is UIApplication uiAppFinal)
                    uiAppFinal.Idling -= OnFirstIdling;
            }
        }
        public override void OnShutdown()
        {
            try
            {
                _commandInterceptor?.Dispose();
                _telemetryService?.Dispose();
                _familyVersioningHook?.Dispose();
                OverheadProxyUpdater.Unregister(Application);
                ImportCadWarningGuard.Unregister(Application);
                FamilyImportWarningGuardV2.Unregister(Application);
                BA.Updates.UpdateService.Unregister(Application);
                Application.ControlledApplication.DocumentSynchronizingWithCentral -= OnDocumentSynchronizingWithCentral;
                Application.ControlledApplication.DocumentSynchronizedWithCentral -= OnDocumentSynchronizedWithCentral;
                if (_exportScheduler != null)
                {
                    Application.Idling -= _exportScheduler.OnIdling;
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("OnShutdown", ex);
            }
        }
        private void OnDocumentSynchronizingWithCentral(object sender, DocumentSynchronizingWithCentralEventArgs e)
        {
            BA.Core.Export.Infrastructure.SynchronizeGuard.IsSynchronizing = true;

            try
            {
                bool shouldProceed = BA.Core.Ledger.LedgerSyncService.Run(
                    e.Document,
                    ResolveLedgerConflicts,
                    WarnBindingFailures,
                    out string cancelReason);

                if (!shouldProceed)
                {
                    TaskDialog.Show("Ledger Sync Conflict", cancelReason);
                    e.Cancel();
                    BA.Core.Export.Infrastructure.SynchronizeGuard.IsSynchronizing = false;
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("OnDocumentSynchronizingWithCentral: unhandled failure applying ledger", ex);
                BA.Core.Export.Infrastructure.SynchronizeGuard.IsSynchronizing = false;
            }
        }

        private void OnDocumentSynchronizedWithCentral(object sender, DocumentSynchronizedWithCentralEventArgs e)
        {
            BA.Core.Export.Infrastructure.SynchronizeGuard.IsSynchronizing = false;
        }

        private static BA.Core.Ledger.LedgerSyncService.LedgerConflictResolution ResolveLedgerConflicts(
            List<BA.Core.Ledger.LedgerSyncService.LedgerConflictItem> conflicts)
        {
            var sb = new StringBuilder();
            foreach (var c in conflicts)
            {
                sb.AppendLine($"{c.FamilyTypeKey} / {c.ParameterName}:");
                sb.AppendLine($"   Your value: '{c.LocalValue}'");
                sb.AppendLine($"   Server value: '{c.ServerValue}' (by {c.ServerEditedBy} at {c.ServerTimestampUtc:u})");
                sb.AppendLine();
            }

            var dialog = new TaskDialog("Ledger Sync Conflict")
            {
                MainInstruction = $"{conflicts.Count} field(s) were changed by someone else since your last sync",
                MainContent = sb.ToString(),
                CommonButtons = TaskDialogCommonButtons.None,
                AllowCancellation = true
            };

            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Keep MY value(s)",
                "Overwrite the server with what you have locally for every listed field.");
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Accept SERVER value(s)",
                "Discard your local changes for every listed field and pull the server's values instead.");
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Cancel sync",
                "Don't sync anything right now. Resolve manually and try again later.");

            TaskDialogResult result = dialog.Show();

            switch (result)
            {
                case TaskDialogResult.CommandLink1:
                    return BA.Core.Ledger.LedgerSyncService.LedgerConflictResolution.KeepMine;
                case TaskDialogResult.CommandLink2:
                    return BA.Core.Ledger.LedgerSyncService.LedgerConflictResolution.AcceptServer;
                default:
                    return BA.Core.Ledger.LedgerSyncService.LedgerConflictResolution.CancelSync;
            }
        }

        /// <summary>
        /// Non-blocking notice shown after a sync that otherwise completed successfully, for
        /// any field where a newly published Type Parameter could not be bound into this
        /// document (GUID not present in this session's shared parameter file, or Revit
        /// rejected the binding). Does NOT cancel or roll back anything; everything else in
        /// the sync already committed by the time this runs. Purely informational, so the user
        /// isn't left wondering why a parameter a colleague added isn't showing up here.
        /// </summary>
        private static void WarnBindingFailures(List<BA.Core.Ledger.LedgerSyncService.LedgerBindingFailure> failures)
        {
            if (failures == null || failures.Count == 0)
            {
                return;
            }

            var sb = new StringBuilder();
            foreach (var f in failures)
            {
                sb.AppendLine($"{f.FamilyTypeKey} / {f.ParameterName}:");
                sb.AppendLine($"   {f.Reason}");
                sb.AppendLine();
            }

            var dialog = new TaskDialog("Ledger Sync – Parameter Binding Warning")
            {
                MainInstruction = $"{failures.Count} Type Parameter(s) could not be applied in this document",
                MainContent = sb.ToString()
                    + "This usually means the shared parameter file loaded in this Revit session is out of date or points to a different file than the one used when the parameter was created."
                    + " Check File > Options > Shared Parameters, then try Synchronize with Central again.",
                CommonButtons = TaskDialogCommonButtons.Ok,
                AllowCancellation = true
            };

            dialog.Show();
        }
    }
}
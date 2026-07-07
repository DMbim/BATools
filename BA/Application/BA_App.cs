// FILE: BA_Tools/Application/BaApplication.cs
using Autodesk.Revit.UI;
using BA.App.Guards;
using BA.App.Overhead;
using BA.App.Settings;
using BA.BAApplication.Ribbon;
using BA.Core.Overhead;
using BATools.SelectionManager.Infrastructure;
using Nice3point.Revit.Toolkit.External;
using System;
using ExternalEvent = Autodesk.Revit.UI.ExternalEvent;
using BA.Telemetry.Infrastructure;
using BA.Telemetry.Services;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using BA.QA.FamilyVersioning.Hook;

namespace BA.BAApplication
{
    public sealed class BaApplication : ExternalApplication
    {
        private TelemetryService _telemetryService;
        private PostableCommandInterceptor _commandInterceptor;
        private FamilyVersioningDocumentHook _familyVersioningHook;
        public override void OnStartup()
        {
            const string tabName = "BA_Tools";


            try
            {
                BA.Updates.UpdateService.Register(Application);
                OverheadProxyUpdater.Register(Application);
                ImportCadWarningGuard.Register(Application);
                FamilyImportWarningGuardV2.Register(Application);

                PluginSettingsBootstrap.ApplySavedSettingsToRuntime();
                OverheadToggleController.Initialize(Application);
                SelectionManagerActivator.Instance.Initialize(Application);

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
                UtilitiesPanelFactory.Build(panelUtilities);

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
            }
            catch (Exception ex)
            {
                AppLogger.LogError("OnShutdown", ex);
            }
        }
    }
}

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
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace BA.BAApplication
{
    public sealed class BaApplication : ExternalApplication
    {
        // Stored as fields so they remain alive for the entire Revit session



        public override void OnStartup()
        {
            const string tabName = "BA_Tools";

            try
            {
                // ── Infrastructure registration ──────────────────────────────────
                BA.Updates.UpdateService.Register(Application);
                OverheadProxyUpdater.Register(Application);
                ImportCadWarningGuard.Register(Application);
                FamilyImportWarningGuardV2.Register(Application);
                PluginSettingsBootstrap.ApplySavedSettingsToRuntime();
                OverheadToggleController.Initialize(Application);
                SelectionManagerActivator.Instance.Initialize(Application);




                // ── Ribbon panels ────────────────────────────────────────────────
                RibbonPanel panelAnnotation = Application.CreatePanel("Annotation", tabName);
                RibbonPanel panelViews = Application.CreatePanel("Views", tabName);
                RibbonPanel panelRooms = Application.CreatePanel("Rooms", tabName);
                RibbonPanel panelFamilies = Application.CreatePanel("Families & Content", tabName);
                RibbonPanel panelProject = Application.CreatePanel("Project", tabName);
                RibbonPanel panelSelection = Application.CreatePanel("Selection", tabName);

                AnnotationPanelFactory.Build(panelAnnotation);
                ViewsPanelFactory.Build(panelViews);
                RoomsPanelFactory.Build(panelRooms);
                FamiliesPanelFactory.Build(panelFamilies);
                ProjectPanelFactory.Build(panelProject);
                SelectionPanelFactory.Build(panelSelection);

                AppLogger.LogInfo("BATools startup completed successfully.");
            }
            catch (Exception ex)
            {
                AppLogger.LogError("OnStartup", ex);
                TaskDialog.Show("BA_Tools – Startup error", ex.ToString());
            }
        }

        public override void OnShutdown()
        {
            try
            {

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
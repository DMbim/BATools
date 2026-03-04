using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using BA.App.Guards;
using BA.App.Overhead;
using BA.BIM.Commands.Anno;
using BA.Classification;
using BA.Commands;
using BA.Commands.Anno;
using BA.Commands.Finishes;
using BA.Commands.Management;
using BA.Commands.Rooms;
using BA.Commands.TextHub;
using BA.Core.Overhead;
using BA.Ribbon;
using Microsoft.VisualBasic;
using Nice3point.Revit.Extensions;
using Nice3point.Revit.Toolkit.External;
using System;
using System.Buffers.Text;

namespace BA.BAApplication
{
    /// <summary>
    /// Main Revit entry point for BA tools.
    /// </summary>
    public sealed class BaApplication : ExternalApplication
    {
        public override void OnStartup()
        {
            const string tabName = "BA_Tools";

            try
            {
                // 1) Register updaters (if you need them)

                BA.Updates.UpdateService.Register(Application);
                OverheadProxyUpdater.Register(Application);
                ImportCadWarningGuard.Register(Application);
                FamilyImportWarningGuardV2.Register(Application);
                BA.App.Settings.PluginSettingsBootstrap.ApplySavedSettingsToRuntime();
                OverheadToggleController.Initialize(Application);

                // 2) Create panels
                RibbonPanel panelAnnotation = Application.CreatePanel("Annotation", tabName);
                RibbonPanel panelManagement = Application.CreatePanel("Monitoring", tabName);
                RibbonPanel panelElements = Application.CreatePanel("Elements", tabName);
                RibbonPanel panelRooms = Application.CreatePanel("Rooms", tabName);

                // 3) Icon paths (keep as you had them)
                var cmIcon16 = "/BA;component/Resources/Icons16/ChangeMonitor16.png";
                var cmIcon32 = "/BA;component/Resources/Icons32/ChangeMonitor32.png";
                var cmStop16 = "/BA;component/Resources/Icons16/ChangeMonitorStop16.png";
                var cmStop32 = "/BA;component/Resources/Icons32/ChangeMonitorStop32.png";
                var cmLive16 = "/BA;component/Resources/Icons16/ChangeMonitorLive16.png";
                var cmLive32 = "/BA;component/Resources/Icons32/ChangeMonitorLive32.png";
                var cmClear16 = "/BA;component/Resources/Icons16/ChangeMonitorClear16.png";
                var cmClear32 = "/BA;component/Resources/Icons32/ChangeMonitorClear32.png";
                var cmRB16 = "/BA;component/Resources/Icons16/RayBounceCeiling16.png";
                var cmRB32 = "/BA;component/Resources/Icons32/RayBounceCeiling32.png";
                var cmHarmonize16 = "/BA;component/Resources/Icons16/FamilyParameters16.png";
                var cmHarmonize32 = "/BA;component/Resources/Icons32/FamilyParameters32.png";
                var cmClassify16 = "/BA;component/Resources/Icons16/Classif16.png";
                var cmClassify32 = "/BA;component/Resources/Icons32/Classif32.png";
                var cmSheetRenumber16 = "/BA;component/Resources/Icons16/SheetDateAndRevision16.png";
                var cmSheetRenumber32 = "/BA;component/Resources/Icons32/SheetDateAndRevision32.png";
                var cmDimOverride16 = "/BA;component/Resources/Icons16/DimOverride16.png";
                var cmDimOverride32 = "/BA;component/Resources/Icons32/DimOverride32.png";
                var cmOHD16 = "/BA;component/Resources/Icons16/OverheadCommand16.png";
                var cmOHD32 = "/BA;component/Resources/Icons32/OverheadCommand32.png";
                var cmAx2r16 = "/BA;component/Resources/Icons16/AxisToRoom16.png";
                var cmAx2r32 = "/BA;component/Resources/Icons32/AxisToRoom32.png";
                var cmA2RLink16 = "/BA;component/Resources/Icons16/AxisToRoom_Link16.png";
                var cmA2RLink32 = "/BA;component/Resources/Icons32/AxisToRoom_Link32.png";
                var cmA2RLocal16 = "/BA;component/Resources/Icons16/AxisToRoom_Local16.png";
                var cmA2RLocal32 = "/BA;component/Resources/Icons32/AxisToRoom_Local32.png";
                var cmE2RLink16 = "/BA;component/Resources/Icons16/ElementToRoom_Link16.png";
                var cmE2RLink32 = "/BA;component/Resources/Icons32/ElementToRoom_Link32.png";
                var cmE2RLocal16 = "/BA;component/Resources/Icons16/ElementToRoom_Local16.png";
                var cmE2RLocal32 = "/BA;component/Resources/Icons32/ElementToRoom_Local32.png";
                var E2RPath = "/BA;component/Resources/Icons16/ElementToRoom16.png";
                var E2RPath32 = "/BA;component/Resources/Icons32/ElementToRoom32.png";
                var use16 = "/BA;component/Resources/Icons16/ause16.png";
                var use32 = "/BA;component/Resources/Icons32/ause32.png";
                var ArAnno16 = "/BA;component/Resources/Icons16/ArAnno_16.png";
                var ArAnno32 = "/BA;component/Resources/Icons32/ArAnno_32.png";
                var mnu16 = "/BA;component/Resources/Icons16/Menu_16.png";
                var mnu32 = "/BA;component/Resources/Icons32/Menu_32.png";
                var fin16 = "/BA;component/Resources/Icons16/FIN16.png";
                var fin32 = "/BA;component/Resources/Icons32/FIN32.png";
                var txt16 = "/BA;component/Resources/Icons16/txt6.png";
                var txt32 = "/BA;component/Resources/Icons32/txt6.png";

                var cmdRoomFinishes = panelRooms.AddPushButton<ApplyFinishesByRoomsCommand>(
                    "Room Finishes",
                    "Room\nFinishes",
                    "Calculate finishes for rooms based on room parameters and element finishes.",
                    fin16,
                    fin32);

                var cmdTextTools = panelRooms.AddPushButton<CmdTextHub>(
                    "Modify Text",
                    "Modify\nText",
                    "Modify Text throughout the model.",
                    txt16,
                    txt32);


                var cmdUse = panelElements.AddPushButton<Cmd_FinishToRoom>(
                    "Finish to Room",
                    "Finish\nto\nRoom",
                    "Automatically finish elements to the level of the room they are in, or a selected room.",
                    use16,
                    use32);

                var arAnno = panelAnnotation.AddPushButton<ArrangeAnnotationsCommand>(
                    "Arrange Annotations",
                    "Anno\nArrange",
                    "Auto Arranges selected annotations",
                    ArAnno16,
                    ArAnno32);


                // ---------------------------
                #region ElementToRoom
                var pdE2R = panelRooms.AddPulldownButton<Cmd_ElementToRoom_Link>(
                    "ElementsToRoom",
                    "Room Nbr\n-> ELements",
                    "Writes RoomNumber into elements located in that room",
                    E2RPath,
                    E2RPath32);

                // Sub-buttons
                pdE2R.AddPushButton<Cmd_ElementToRoom_Link>(
                    "ElementToRoomLink",
                    "Link",
                    "Writes information into the elements based on their location(linked room)",
                    cmE2RLink16,
                    cmE2RLink32);

                pdE2R.AddPushButton<Cmd_ElementToRoom_Local>(
                    "ElementToRoomLocal",
                    "Local",
                    "Writes information into the elements based on their location(room)",
                    cmE2RLocal16,
                    cmE2RLocal32);
                #endregion
                // ---------------------------


                // ---------------------------
                #region DimensionEdit

                var pdDim = panelAnnotation.AddPulldownButton<Cmd_DimensionEdit>(
                    "DimEditPulldown",
                    "Dim\nEdit",
                    "Mass Dimension Edit – change value, add value under",
                    cmDimOverride16,
                    cmDimOverride32);

                pdDim.AddPushButton<Cmd_Dim_ValueOverride>(
                    "OverrideText",
                    "Override\nText",
                    "Override selected dimensions with extra text below the value.",
                    "/BA;component/Resources/Icons16/DimOveride_OverrideDimension16.png",
                    "/BA;component/Resources/Icons32/DimOveride_OverrideDimension32.png");

                pdDim.AddPushButton<Cmd_Dim_AddBelow>(
                    "AddBelow",
                    "Add\nBelow",
                    "Add an extra numeric value below the existing dimension value.",
                    "/BA;component/Resources/Icons16/DimOveride_OverrideDimensionSegment16.png",
                    "/BA;component/Resources/Icons32/DimOveride_OverrideDimensionSegment32.png");
                #endregion
                // ---------------------------

                // ---------------------------
                // Sheets
                // ---------------------------
                panelManagement.AddPushButton<Cmd_SheetDateAndRevision>(
                    "SheetDateRevision",
                    "Sheet\nDate+Rev",
                    "Update Issue Date / Revision on selected sheets.",
                    cmSheetRenumber16,
                    cmSheetRenumber32);


                // ---------------------------
                #region Management
                // ---------------------------

                // Family parameters
                panelManagement.AddPushButton<Cmd_FamilyParameters>(
                    "HarmonizeFamilyParameters",
                    "Harmonize\nFamily Params",
                    "Harmonize family parameters in the current family.",
                    cmHarmonize16, cmHarmonize32
                );


                // Classification
                panelManagement.AddPushButton<Cmd_ClassifyElements>(
                    "ClassifyByType",
                    "Classify\nTypes",
                    "Classify element types by rules.",
                    cmClassify16,
                    cmClassify32
                );


                // Project tools hub
                var pdPTH = panelRooms.AddPushButton<Cmd_ProjectToolsHub>(
                    "ManagementTools",
                    "Management Tools",
                    "Collection of Management tools",
                    mnu16,
                    mnu32
                );


                #endregion

                // ---------------------------
                #region Change Monitor
                // ---------------------------
                var pdMon = panelManagement.AddPulldownButton<Cmd_ChangeMonitorStart>(
                    "ChangeMonitor",
                    "Change\nMonitor",
                    "Track adds, deletes, moves and parameter edits; generate reports and highlights.",
                    cmIcon16,
                    cmIcon32);

                pdMon.AddPushButton<Cmd_ChangeMonitorStart>(
                    "ChangeMonitorStart",
                    "Start",
                    "Start monitoring the active document.",
                    cmIcon16,
                    cmIcon32);

                pdMon.AddPushButton<Cmd_ChangeMonitorStop>(
                    "ChangeMonitorStop",
                    "Stop",
                    "Stop monitoring and open the report dialog.",
                    cmStop16,
                    cmStop32);

                pdMon.AddPushButton<Cmd_ChangeMonitorLive>(
                    "ChangeMonitorLive",
                    "Live\nReview",
                    "Open the live review window.",
                    cmLive16,
                    cmLive32);

                pdMon.AddPushButton<Cmd_ChangeMonitorClearHighlights>(
                    "ChangeMonitorClear",
                    "Clear\nHighlights",
                    "Clear all overrides applied by Change Monitor.",
                    cmClear16,
                    cmClear32);

                #endregion
                // ---------------------------


                // ---------------------------
                #region AxisToRoom
                // ---------------------------
                var pdAxis = panelRooms.AddPulldownButton<Cmd_AxisToRoom_Link>(
                    "AxisToRoom",
                    "Axis\n→ Room",
                    "Place BA_Axis detail into rooms selected by room tags.",
                    cmAx2r16,
                    cmAx2r32);

                // Sub-buttons
                pdAxis.AddPushButton<Cmd_AxisToRoom_Link>(
                    "AxisToRoomLink",
                    "Link",
                    "Pick room tags hosted in the model, resolve rooms in a linked model, then place detail.",
                    cmA2RLink16,
                    cmA2RLink32);

                pdAxis.AddPushButton<Cmd_AxisToRoom_Local>(
                    "AxisToRoomLocal",
                    "Local",
                    "Pick room tags in the active model, resolve rooms locally, then place detail.",
                    cmA2RLocal16,
                    cmA2RLocal32);
                #endregion
                // ---------------------------

                // ---------------------------
                // RayBounce
                // ---------------------------
                panelElements.AddPushButton<Cmd_RayBounceCeiling>(
                    "RayBounceCeiling",
                    "Ray Bounce\nCeiling",
                    "Calculate the ceiling above the selected element.",
                    cmRB16,
                    cmRB32
                );


                panelAnnotation.AddPushButton<Cmd_OverheadAutoDash>(
                    "OverheadAutoDash",
                    "Overhead\nAuto Dash",
                    "Automatically generate overhead dash patterns in annotations.",
                    cmOHD16,
                    cmOHD32
                );
            }
            catch (Exception ex)
            {
                // Don’t crash Revit on startup
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
            catch { }
        }

    }
}

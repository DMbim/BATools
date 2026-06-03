// FILE: BA_Tools/Application/Ribbon/AnnotationPanelFactory.cs
using Autodesk.Revit.UI;
using BA.App.Overhead;
using BA.BIM.Commands.Anno;
using BA.Commands;
using BA.Commands.Anno;
using BA.Commands.TextHub;
using BA.KeyplanGrid;
using BA.Ribbon;
using BATools.ParamCopy.Commands;
using Nice3point.Revit.Extensions;

namespace BA.BAApplication.Ribbon
{
    internal static class AnnotationPanelFactory
    {
        internal static void Build(RibbonPanel panel)
        {
            panel.AddPushButton<ArrangeAnnotationsCommand>(
                "ArrangeAnnotations", "Arrange\nAnnotations",
                "Auto-arrange selected annotations to resolve overlaps.",
                IconResources.ArAnno16, IconResources.ArAnno32);

            panel.AddPushButton<ToggleOverheadProxyCommand>(
                "OverheadLines", "Overhead\nLines",
                "Automatically generate overhead dash patterns in the active plan view.",
                IconResources.Overhead16, IconResources.Overhead32);

            var pdDim = panel.AddPulldownButton<Cmd_Dim_ValueOverride>(
                "DimEditPulldown", "Dim\nEdit",
                "Dimension editing tools.",
                IconResources.DimOverride16, IconResources.DimOverride32);


            pdDim.AddPushButton<Cmd_Dim_ValueOverride>(
                "OverrideText", "Override\nText",
                "Override selected dimensions with custom text.",
                IconResources.DimValueOverride16, IconResources.DimValueOverride32);

            pdDim.AddPushButton<Cmd_Dim_AddBelow>(
                "AddBelow", "Add\nBelow",
                "Add an extra numeric value below the existing dimension value.",
                IconResources.DimAddBelow16, IconResources.DimAddBelow32);


            var pdKeyPlan = panel.AddPulldownButton<Cmd_KeyplanGridGenerator>(
                "KeyPlan", "Key\nPlan",
                "Keyplan generation tools.",
                IconResources.KeyPlan16, IconResources.KeyPlan32);

            pdKeyPlan.AddPushButton<Cmd_KeyplanGridGenerator>(
                "GenerateKeyplanGrids", "Generate\nGrids",
                "Generate grid cells in the keyplan drafting view based on the current model grids.",
                IconResources.KeyPlan16, IconResources.KeyPlan32);

            var pdCopy = panel.AddPulldownButton<Cmd_ScheduleSync>(
                "Copy", "\nCopy",
                "¨Coping tools",
                IconResources.ScheduleSync16, IconResources.ScheduleSync32);

            pdCopy.AddPushButton<Cmd_ScheduleSync>(
                "ScheduleCopyValue", "Schedule\nCopy Value",
                "Synchronize schedule data across multiple schedules based on matching parameters.",
                IconResources.ScheduleSync16, IconResources.ScheduleSync32);

            pdCopy.AddPushButton<ParamCopyCommand>(
                "CopyTypeParams", "Copy Type\nParameters",
                "Copy type parameters from a source element to one or more target elements.",
                IconResources.Copy16, IconResources.Copy32);

            panel.AddPushButton<SyncBaLineStylesCommand>(
    "SyncLineStyles", "Sync\nLine Styles",
    "Synchronize BA standard line patterns and line styles into the current document.",
    IconResources.LineStyles16, IconResources.LineStyles32);

            panel.AddPushButton<CmdTextHub>(
    "TextHub", "Text\nHub",
    "Find, replace and reformat text notes across the entire model.",
    IconResources.TextHub16, IconResources.TextHub32);
        }
    }



}
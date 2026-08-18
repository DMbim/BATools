// FILE: BA_Tools/BAApplication/Ribbon/LayoutPlanningPanelFactory.cs
using Autodesk.Revit.UI;
using BA.Commands.Diagnostics;
using BA.Commands.Ribbon;
using BA.KeyplanGrid;
using BA.Materials;
using BA.Ribbon;
using BATools.ParamCopy.Commands;
namespace BA.BAApplication.Ribbon
{
    internal static class LayoutPlanningPanelFactory
    {
        internal static void Build(RibbonPanel panel)
        {
            #region Keyplan Grid Generator (moved from AnnotationPanelFactory / Drawing
            // Production; was buried three levels deep inside the Overhead Auto Dash stack
            // purely because it shared stacking space. Standalone here, was a single-item
            // pulldown, now a plain push button since there's nothing left to group it with.)
            panel.AddPushButton<Cmd_KeyplanGridGenerator>(
                "GenerateKeyplanGrids", "Generate\nGrids",
                "Generate grid cells in the keyplan drafting view.",
                IconResources.KeyPlan16, IconResources.KeyPlan32);
            #endregion
            // <- NEW: standalone Markup Cleanup button. Deliberately NOT combined into a
            //    SplitButton with PlaceMarkupCommand. Revit's SplitButton remembers the
            //    last-clicked child on its top face, which would make an accidental
            //    Cleanup click silently hijack the default click behavior of the most
            //    frequently used command in this panel. Kept as an independent PushButton
            //    instead, no shared click state with Place Markup.
            panel.AddPushButton<BA.Markup.Commands.MarkupCleanupCommand>(
                "MarkupCleanup", "Markup\nCleanup",
                "Remove inactive users from the markup assignee registry and clear any markup " +
                "assignments pointing to users no longer active on this project.",
                IconResources.Markup16, IconResources.Markup16);
            var pdCopy = panel.AddPulldownButton<ParamCopyCommand>(
                "Copy", "\nCopy",
                "Copying tools",
                IconResources.Copy16, IconResources.Copy32);
            pdCopy.AddPushButton<ParamCopyCommand>(
                "CopyTypeParams", "Copy Type\nParameters",
                "Copy type parameters from a source element to one or more target elements.",
                IconResources.CopyP16, IconResources.CopyP32);
            panel.AddPushButton<DiagRoomNumberParamCommand>(
                "DiagRoomNumberParam", "Room Number\nDiagnostics",
                "Check for missing or duplicate room number parameters in the model.",
                IconResources.Markup16, IconResources.Markup32);
            panel.AddPushButton<Cmd_OpenMaterialLibrary>(
                "OpenMaterialLibrary", "Material Library\nDiagnostics",
                "Open the material library for inspection and management.",
                IconResources.Markup16, IconResources.Markup32);
            // <- NEW: jumps the active ribbon tab to BA_Tools. Lives here in BA_Admin so
            //    there's a fast way back to the daily-drafting tab without hunting for it
            //    manually. Goes through the internal Autodesk.Windows ComponentManager,
            //    see CmdActivateBaTab for the unsupported-API caveat.
            panel.AddPushButton<CmdActivateBaTab>(
                "ActivateBaToolsTab", "Go To\nBA Tools",
                "Switch the active ribbon tab to BA_Tools.",
                IconResources.Markup16, IconResources.Markup32);
        }
    }
}
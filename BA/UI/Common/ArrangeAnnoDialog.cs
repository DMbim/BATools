using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BIM.Core.Annotations;

namespace BA.UI.Common
{
    public static class ArrangeAnnotationsDialog
    {
        public static ArrangeConfig GetConfig()
        {
            // Step 1: choose group (max 4 links)
            TaskDialog td1 = new TaskDialog("BA - Arrange annotations");
            td1.MainInstruction = "Choose operation group";
            td1.MainContent = "TaskDialog supports only 4 command links. More options are shown on the next step.";
            td1.CommonButtons = TaskDialogCommonButtons.Cancel;

            td1.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Resolve collisions (de-overlap)");
            td1.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Distribute");
            td1.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Stack list");
            td1.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Snap to guide line");

            var r1 = td1.Show();
            if (r1 == TaskDialogResult.Cancel)
                return null;

            if (r1 == TaskDialogResult.CommandLink1)
            {
                var cfg = ArrangeConfig.DefaultResolve();
                cfg.Mode = ArrangeMode.ResolveCollisions;
                return cfg;
            }

            if (r1 == TaskDialogResult.CommandLink2)
            {
                return ChooseDistribute();
            }

            if (r1 == TaskDialogResult.CommandLink3)
            {
                return ChooseStack();
            }

            if (r1 == TaskDialogResult.CommandLink4)
            {
                var cfg = ArrangeConfig.DefaultResolve();
                cfg.Mode = ArrangeMode.SnapToGuideLine;
                cfg.Gap = UnitUtils.ConvertToInternalUnits(6, UnitTypeId.Millimeters);
                return cfg;
            }

            return null;
        }

        private static ArrangeConfig ChooseDistribute()
        {
            TaskDialog td = new TaskDialog("BA - Distribute");
            td.MainInstruction = "Distribute mode";
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Distribute horizontally");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Distribute vertically");

            var r = td.Show();
            if (r == TaskDialogResult.Cancel) return null;

            var cfg = ArrangeConfig.DefaultResolve();
            cfg.Mode = (r == TaskDialogResult.CommandLink1)
                ? ArrangeMode.DistributeHorizontal
                : ArrangeMode.DistributeVertical;

            return cfg;
        }

        private static ArrangeConfig ChooseStack()
        {
            TaskDialog td = new TaskDialog("BA - Stack list");
            td.MainInstruction = "Stack mode";
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Stack vertically");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Stack horizontally");

            var r = td.Show();
            if (r == TaskDialogResult.Cancel) return null;

            var cfg = ArrangeConfig.DefaultResolve();
            cfg.Mode = (r == TaskDialogResult.CommandLink1)
                ? ArrangeMode.StackListVertical
                : ArrangeMode.StackListHorizontal;

            cfg.Gap = UnitUtils.ConvertToInternalUnits(6, UnitTypeId.Millimeters);
            return cfg;
        }
    }
}
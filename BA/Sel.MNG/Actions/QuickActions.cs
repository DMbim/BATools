using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BATools.SelectionManager.ExternalEvents;
using BATools.SelectionManager.Infrastructure;

namespace BATools.SelectionManager.Actions
{
    public class IsolateElementsAction : IQuickAction
    {
        public string Id => "isolate_temporary";
        public string DefaultLabel => "Isolate";
        public string IconResourceKey => "IconIsolate";
        public bool RequiresSelection => true;

        public bool CanExecute(IList<ElementId> sel) => sel.Count > 0;

        public void Execute(IList<ElementId> sel)
        {
            SelectionManagerBridge.Instance.RequestViewOperation(
                ViewOperationType.IsolateTemporary, sel.ToList());
        }
    }

    public class HideElementsAction : IQuickAction
    {
        public string Id => "hide_elements";
        public string DefaultLabel => "Hide";
        public string IconResourceKey => "IconHide";
        public bool RequiresSelection => true;

        public bool CanExecute(IList<ElementId> sel) => sel.Count > 0;

        public void Execute(IList<ElementId> sel)
        {
            SelectionManagerBridge.Instance.RequestViewOperation(
                ViewOperationType.HideElements, sel.ToList());
        }
    }

    public class ResetIsolationAction : IQuickAction
    {
        public string Id => "reset_isolation";
        public string DefaultLabel => "Reset View";
        public string IconResourceKey => "IconResetView";
        public bool RequiresSelection => false;

        public bool CanExecute(IList<ElementId> sel) => true;

        public void Execute(IList<ElementId> sel)
        {
            SelectionManagerBridge.Instance.RequestViewOperation(
                ViewOperationType.ResetTemporaryHideIsolate, new List<ElementId>());
        }
    }

    public class OverrideRedAction : IQuickAction
    {
        public string Id => "override_red";
        public string DefaultLabel => "Red";
        public string IconResourceKey => "IconOverrideRed";
        public bool RequiresSelection => true;

        public bool CanExecute(IList<ElementId> sel) => sel.Count > 0;

        public void Execute(IList<ElementId> sel)
        {
            SelectionManagerBridge.Instance.RequestViewOperation(
                ViewOperationType.OverrideColor, sel.ToList(),
                colorArgb: unchecked((int)0xFFE53935));
        }
    }

    public class ResetOverridesAction : IQuickAction
    {
        public string Id => "reset_overrides";
        public string DefaultLabel => "Reset Color";
        public string IconResourceKey => "IconResetOverrides";
        public bool RequiresSelection => true;

        public bool CanExecute(IList<ElementId> sel) => sel.Count > 0;

        public void Execute(IList<ElementId> sel)
        {
            SelectionManagerBridge.Instance.RequestViewOperation(
                ViewOperationType.ResetOverrides, sel.ToList());
        }
    }

    public class SaveSelectionAction : IQuickAction
    {
        public string Id => "save_selection";
        public string DefaultLabel => "Save Set";
        public string IconResourceKey => "IconSaveSet";
        public bool RequiresSelection => true;

        private readonly Action _showSaveDialog;

        public SaveSelectionAction(Action showSaveDialog)
        {
            _showSaveDialog = showSaveDialog;
        }

        public bool CanExecute(IList<ElementId> sel) => sel.Count > 0;

        public void Execute(IList<ElementId> sel)
        {
            _showSaveDialog?.Invoke();
        }
    }

    public static class QuickActionRegistry
    {
        public static IReadOnlyList<IQuickAction> CreateDefault(Action showSaveDialog)
        {
            return new List<IQuickAction>
            {
                new IsolateElementsAction(),
                new HideElementsAction(),
                new ResetIsolationAction(),
                new OverrideRedAction(),
                new ResetOverridesAction(),
                new SaveSelectionAction(showSaveDialog)
            };
        }
    }
}
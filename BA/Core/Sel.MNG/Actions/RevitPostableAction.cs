using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BATools.SelectionManager.Infrastructure;

namespace BATools.SelectionManager.Actions
{
    public class RevitPostableAction : IQuickAction
    {
        private readonly PostableCommand _command;
        private readonly string _category;

        public string Id => $"revit_{(int)_command}";
        public string DefaultLabel { get; }
        public string IconResourceKey => "IconRevitCmd";
        public bool RequiresSelection => false;

        public RevitPostableAction(PostableCommand command, string displayName, string category)
        {
            _command = command;
            _category = category;
            DefaultLabel = displayName;
        }

        public bool CanExecute(IList<ElementId> selection) => true;

        public void Execute(IList<ElementId> selection)
            => SelectionManagerBridge.Instance.RequestPostCommand(_command);
    }
}
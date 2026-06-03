using System.Collections.Generic;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BATools.SelectionManager.Actions
{
    public interface IQuickAction
    {
        string Id { get; }
        string DefaultLabel { get; }
        string IconResourceKey { get; }
        bool RequiresSelection { get; }
        bool CanExecute(IList<ElementId> currentSelection);
        void Execute(IList<ElementId> currentSelection);
    }
}
using Autodesk.Revit.Attributes;
using BA.ViewModels;
using BA.Views;
using Nice3point.Revit.Toolkit.External;

namespace BA.Commands
{
    /// <summary>
    ///     External command entry point
    /// </summary>
    [UsedImplicitly]
    [Transaction(TransactionMode.Manual)]
    public class StartupCommand : ExternalCommand
    {
        public override void Execute()
        {
            var viewModel = new BAViewModel();
            var view = new BAView(viewModel);
            view.ShowDialog();
        }
    }
}
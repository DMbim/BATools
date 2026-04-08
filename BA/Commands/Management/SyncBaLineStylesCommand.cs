using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BA.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class SyncBaLineStylesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var doc = data.Application.ActiveUIDocument.Document;

            using (Transaction t = new Transaction(doc, "Sync BA Line Styles"))
            {
                t.Start();

                var sync = new BA.Core.Graphics.BaLineStyleSynchronizer(doc);
                sync.Execute();

                t.Commit();
            }

            TaskDialog.Show("BA", "Line styles synchronized.");

            return Result.Succeeded;
        }
    }
}

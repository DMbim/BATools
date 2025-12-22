using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using System;
using System.IO;

namespace BA.Classification
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_ClassifyElements : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // 1️⃣ Prompt user for Excel file
                var dlg = new OpenFileDialog
                {
                    Filter = "Excel Files (*.xlsx)|*.xlsx",
                    Title = "Select BA Classification Rules Excel File"
                };

                if (dlg.ShowDialog() != true)
                {
                    TaskDialog.Show("BA Classification", "Operation cancelled by user.");
                    return Result.Cancelled;
                }

                string excelPath = dlg.FileName;
                if (!File.Exists(excelPath))
                {
                    TaskDialog.Show("BA Classification", "Invalid Excel file path.");
                    return Result.Failed;
                }

                // 2️⃣ Load and classify
                TaskDialog.Show("BA Classification", $"Starting classification using:\n{excelPath}");

                var engine = new ClassificationEngine(excelPath);
                engine.ClassifyAll(doc);

                TaskDialog.Show("BA Classification", "✅ Classification complete.\nSee log file next to Excel for details.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"Error: {ex.Message}\n\n{ex.StackTrace}";
                TaskDialog.Show("BA Classification - Error", message);
                return Result.Failed;
            }
        }
    }
}

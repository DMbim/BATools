using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace BA.Core
{
    public class HarmonizerEventHandler : IExternalEventHandler
    {
        public HarmonizerEventHandler()
        {
            Log = new StringBuilder();
            Decisions = new List<ParameterPreview>();
        }
        public UIDocument UiDocument { get; set; }
        public UIApplication UiApplication { get; set; }
        public Document Document { get; set; }
        public FamilyManager FamilyManager { get; set; }
        public List<ParameterPreview> Decisions { get; set; }
        public StringBuilder Log { get; set; }

        /// <summary>  
        /// This method is called when the external event is raised.  
        /// It's a safe place to perform Revit API operations.  
        /// </summary>  
        public void Execute(UIApplication app)
        {
            try
            {
                // Call the main harmonization logic.  
                HarmonizeFamilyParameters.Execute(UiApplication, Document, Decisions, Log);

                // Save log to file. This can also be done on the UI thread as it's just file I/O.  
                string logPath = string.Empty;
                var docPath = Document.PathName;
                if (!string.IsNullOrWhiteSpace(docPath))
                {
                    logPath = Path.Combine(Path.GetDirectoryName(docPath), "harmonizer_log.txt");
                    File.WriteAllText(logPath, Log.ToString());
                }

                // Show a TaskDialog to the user.  
                var td = new TaskDialog("Harmonization Complete")
                {
                    MainInstruction = "Family parameters have been harmonized.",
                    MainContent = string.IsNullOrWhiteSpace(logPath)
                        ? "Log was generated in memory."
                        : $"Log saved to:\n{logPath}"
                };
                td.Show();
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", "An error occurred during harmonization:\n" + ex.Message);
            }
        }

        public string GetName() => "Family Harmonizer Handler";
    }
}

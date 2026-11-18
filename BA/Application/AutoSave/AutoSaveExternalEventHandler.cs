// File: BA/Application/AutoSave/AutoSaveExternalEventHandler.cs
using Autodesk.Revit.UI;
using System;

namespace BA.BAApplication.AutoSave
{
    /// <summary>
    /// Dedicated handler, separate from AppExternalInvoker's queue, so a long
    /// save never sits in the same queue as UI driven actions. Rooted by a
    /// static field in AutoSaveService for the whole Revit session.
    /// </summary>
    internal sealed class AutoSaveExternalEventHandler : IExternalEventHandler
    {
        public void Execute(UIApplication app)
        {
            try
            {
                AutoSaveService.RunSavePass(app);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("AutoSaveExternalEventHandler.Execute", ex);
            }
        }

        public string GetName()
        {
            return "BA Autosave";
        }
    }
}
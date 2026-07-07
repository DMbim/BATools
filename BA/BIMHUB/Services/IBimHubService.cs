// File: BA_Tools/UI/BimHub/Services/IBimHubService.cs
using Autodesk.Revit.UI;

namespace BA.UI.BimHub.Services
{
    /// <summary>
    /// Contract for all BIM hub command services.
    /// Each extracted command service implements this interface.
    /// The hub calls Run(uiApp) through RevitExternalInvoker — never directly.
    /// </summary>
    public interface IBimHubService
    {
        /// <summary>
        /// Execute the command logic. Called inside IExternalEventHandler.Execute()
        /// — Revit API access is valid. Must not open its own transaction unless
        /// the operation specifically requires isolated transaction scope.
        /// </summary>
        void Run(UIApplication uiApp);
    }
}
using System;
using Autodesk.Revit.UI;

namespace BATools.SelectionManager.ExternalEvents
{
    public class PostCommandHandler : IExternalEventHandler
    {
        public PostableCommand Command { get; set; }

        public void Execute(UIApplication uiApp)
        {
            try
            {
                var cmdId = RevitCommandId.LookupPostableCommandId(Command);
                if (uiApp.CanPostCommand(cmdId))
                    uiApp.PostCommand(cmdId);
                else
                    System.Diagnostics.Debug.WriteLine(
                        $"[PostCommandHandler] Cannot post: {Command}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[PostCommandHandler] {ex.Message}");
            }
        }

        public string GetName() => "PostRevitCommand";
    }
}
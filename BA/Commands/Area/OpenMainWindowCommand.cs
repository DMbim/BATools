// BA/Addin/Commands/OpenMainWindowCommand.cs
using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.Core.Areas.EEH;
using BA.UI.Views;

namespace BA.Addin.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class OpenMainWindowCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                if (AreaMainWindow.Instance is not null)
                {
                    AreaMainWindow.Instance.Activate();
                    AreaMainWindow.Instance.Focus();
                    return Result.Succeeded;
                }

                var bridge = BaApplication.CzaBridge
                    ?? throw new InvalidOperationException(
                        "CzaBridge není inicializován. " +
                        "Zkontroluj BaApplication.OnStartup().");

                var window = new AreaMainWindow(bridge, commandData.Application);
                window.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
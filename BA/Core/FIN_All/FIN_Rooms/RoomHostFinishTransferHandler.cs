using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;

namespace BA.Core.Rooms
{
    /// <summary>
    /// Executes requests coming from the WPF UI inside Revit API context.
    /// </summary>
    public class RoomHostFinishTransferHandler : IExternalEventHandler
    {
        private Action<UIApplication>? _pendingAction;
        private string _pendingName = "Room Host Finish Transfer";

        public void Raise(Action<UIApplication> action, string name = "Room Host Finish Transfer")
        {
            _pendingAction = action;
            _pendingName = name;
        }

        public void Execute(UIApplication app)
        {
            var a = _pendingAction;
            _pendingAction = null;

            if (a == null) return;

            try { a(app); }
            catch (Exception ex)
            {
                TaskDialog.Show("BA", ex.ToString());
            }
        }

        public string GetName() => _pendingName;
    }
}
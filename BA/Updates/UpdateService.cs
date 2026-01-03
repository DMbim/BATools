// File: BA/Updates/UpdateService.cs
using Autodesk.Revit.UI;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BA.Updates
{
    internal static class UpdateService
    {
        private static bool _registered;
        private static bool _ran;

        public static void Register(UIControlledApplication app)
        {
            if (_registered) return;
            _registered = true;
            app.Idling += OnIdling;
        }

        private static async void OnIdling(object? sender, Autodesk.Revit.UI.Events.IdlingEventArgs e)
        {
            if (_ran) return;
            _ran = true;

            if (sender is not UIApplication uiapp)
                return;

            try
            {
                var r = await UpdateCoordinator.CheckAsync(uiapp, CancellationToken.None);
                if (r != null && r.HasUpdate)
                    UpdateCoordinator.PromptAndHandle(uiapp, r);
            }
            catch (Exception)
            {
                // never break Revit startup
            }
        }
    }
}

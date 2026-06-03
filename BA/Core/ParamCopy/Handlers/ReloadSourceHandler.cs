using Autodesk.Revit.UI;
using BATools.ParamCopy.Models;
using BATools.ParamCopy.Services;
using System;
using System.Collections.Generic;

namespace BATools.ParamCopy.Handlers
{
    public class ReloadSourceHandler : IExternalEventHandler
    {
        private readonly object _lock = new();
        private ListSettings? _settings;
        public Action<List<ElementListItem>>? OnCompleted { get; set; }

        public void SetSettings(ListSettings s)
        {
            lock (_lock) _settings = s;
        }

        public void Execute(UIApplication app)
        {
            ListSettings? s;
            lock (_lock) { s = _settings; _settings = null; }
            if (s == null) return;

            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;

            try
            {
                var items = ElementFilterService.Collect(doc, s);
                System.Windows.Application.Current.Dispatcher.BeginInvoke(
                    () => OnCompleted?.Invoke(items));
            }
            catch
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(
                    () => OnCompleted?.Invoke(new List<ElementListItem>()));
            }
        }

        public string GetName() => "BA.ParamCopy.ReloadSource";
    }
}

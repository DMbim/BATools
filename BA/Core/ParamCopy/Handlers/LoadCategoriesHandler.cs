using Autodesk.Revit.UI;
using BATools.ParamCopy.Services;
using System;
using System.Collections.Generic;

namespace BATools.ParamCopy.Handlers
{
    /// <summary>
    /// Resolves the list of Model-type category names that have at least one
    /// instance in the active document. Single-slot request — acceptable here
    /// because this handler only ever serves one logical concern (there is one
    /// shared category list for both Source and Dest).
    /// </summary>
    public class LoadCategoriesHandler : IExternalEventHandler
    {
        private readonly object _lock = new();
        private Action<List<string>>? _onCompleted;
        private bool _pending;

        public void Request(Action<List<string>> onCompleted)
        {
            lock (_lock)
            {
                _onCompleted = onCompleted;
                _pending = true;
            }
        }

        public void Execute(UIApplication app)
        {
            Action<List<string>>? cb;
            lock (_lock)
            {
                if (!_pending) return;
                cb = _onCompleted;
                _pending = false;
            }
            if (cb == null) return;

            var doc = app.ActiveUIDocument?.Document;
            if (doc == null)
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(
                    () => cb(new List<string>()));
                return;
            }

            try
            {
                var names = ElementFilterService.CollectCategoryNamesInDocument(doc);
                System.Windows.Application.Current.Dispatcher.BeginInvoke(
                    () => cb(names));
            }
            catch
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(
                    () => cb(new List<string>()));
            }
        }

        public string GetName() => "BA.ParamCopy.LoadCategories";
    }
}
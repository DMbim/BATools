using Autodesk.Revit.UI;
using BATools.ParamCopy.Models;
using BATools.ParamCopy.Services;
using System;
using System.Collections.Generic;

namespace BATools.ParamCopy.Handlers
{
    /// <summary>
    /// Resolves the set of instance parameter names available for a category,
    /// optionally narrowed by FilterSets. Instantiated once per dropdown
    /// concern (source category-only, dest category-only, source matched,
    /// dest matched) — see ParamCopyExternalInvoker — so that rapid successive
    /// requests for different concerns never overwrite each other's pending
    /// single-slot request via ExternalEvent's raise coalescing.
    /// </summary>
    public class LoadParameterNamesHandler : IExternalEventHandler
    {
        private readonly object _lock = new();
        private readonly string _name;
        private LoadParameterNamesRequest? _request;

        public LoadParameterNamesHandler(string name)
        {
            _name = name;
        }

        public void SetRequest(LoadParameterNamesRequest req)
        {
            lock (_lock) _request = req;
        }

        public void Execute(UIApplication app)
        {
            LoadParameterNamesRequest? req;
            lock (_lock) { req = _request; _request = null; }
            if (req == null) return;

            var doc = app.ActiveUIDocument?.Document;
            if (doc == null)
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(
                    () => req.OnCompleted(new List<string>()));
                return;
            }

            try
            {
                var names = ElementFilterService.CollectParameterNames(
                    doc, req.CategoryName, req.FilterSets);

                System.Windows.Application.Current.Dispatcher.BeginInvoke(
                    () => req.OnCompleted(names));
            }
            catch
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(
                    () => req.OnCompleted(new List<string>()));
            }
        }

        public string GetName() => _name;
    }

    public class LoadParameterNamesRequest
    {
        public string CategoryName { get; }
        public IReadOnlyList<FilterSet>? FilterSets { get; }
        public Action<List<string>> OnCompleted { get; }

        public LoadParameterNamesRequest(
            string categoryName,
            IReadOnlyList<FilterSet>? filterSets,
            Action<List<string>> onCompleted)
        {
            CategoryName = categoryName;
            FilterSets = filterSets;
            OnCompleted = onCompleted;
        }
    }
}
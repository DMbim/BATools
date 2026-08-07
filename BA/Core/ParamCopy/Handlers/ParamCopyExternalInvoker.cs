using Autodesk.Revit.UI;
using BATools.ParamCopy.Models;
using System;
using System.Collections.Generic;

namespace BATools.ParamCopy.Handlers
{
    public sealed class ParamCopyExternalInvoker : IDisposable
    {
        private readonly ReloadSourceHandler _srcHandler;
        private readonly ReloadDestHandler _dstHandler;
        private readonly RunCopyHandler _copyHandler;
        private readonly LoadCategoriesHandler _categoriesHandler;
        private readonly LoadParameterNamesHandler _srcCategoryParamsHandler;
        private readonly LoadParameterNamesHandler _dstCategoryParamsHandler;
        private readonly LoadParameterNamesHandler _srcMatchedParamsHandler;
        private readonly LoadParameterNamesHandler _dstMatchedParamsHandler;

        private readonly ExternalEvent _srcEvent;
        private readonly ExternalEvent _dstEvent;
        private readonly ExternalEvent _copyEvent;
        private readonly ExternalEvent _categoriesEvent;
        private readonly ExternalEvent _srcCategoryParamsEvent;
        private readonly ExternalEvent _dstCategoryParamsEvent;
        private readonly ExternalEvent _srcMatchedParamsEvent;
        private readonly ExternalEvent _dstMatchedParamsEvent;

        public ParamCopyExternalInvoker(UIApplication uiApp)
        {
            _srcHandler = new ReloadSourceHandler();
            _dstHandler = new ReloadDestHandler();
            _copyHandler = new RunCopyHandler();
            _categoriesHandler = new LoadCategoriesHandler();
            _srcCategoryParamsHandler = new LoadParameterNamesHandler("BA.ParamCopy.LoadSourceCategoryParams");
            _dstCategoryParamsHandler = new LoadParameterNamesHandler("BA.ParamCopy.LoadDestCategoryParams");
            _srcMatchedParamsHandler = new LoadParameterNamesHandler("BA.ParamCopy.LoadSourceMatchedParams");
            _dstMatchedParamsHandler = new LoadParameterNamesHandler("BA.ParamCopy.LoadDestMatchedParams");

            _srcEvent = ExternalEvent.Create(_srcHandler);
            _dstEvent = ExternalEvent.Create(_dstHandler);
            _copyEvent = ExternalEvent.Create(_copyHandler);
            _categoriesEvent = ExternalEvent.Create(_categoriesHandler);
            _srcCategoryParamsEvent = ExternalEvent.Create(_srcCategoryParamsHandler);
            _dstCategoryParamsEvent = ExternalEvent.Create(_dstCategoryParamsHandler);
            _srcMatchedParamsEvent = ExternalEvent.Create(_srcMatchedParamsHandler);
            _dstMatchedParamsEvent = ExternalEvent.Create(_dstMatchedParamsHandler);
        }

        public void ReloadSource(ListSettings settings,
            Action<List<ElementListItem>> onCompleted)
        {
            _srcHandler.OnCompleted = onCompleted;
            _srcHandler.SetSettings(settings);
            _srcEvent.Raise();
        }

        public void ReloadDest(ListSettings settings,
            Action<List<ElementListItem>> onCompleted)
        {
            _dstHandler.OnCompleted = onCompleted;
            _dstHandler.SetSettings(settings);
            _dstEvent.Raise();
        }

        public void RunCopy(
            IReadOnlyList<ElementPair> pairs,
            IReadOnlyList<ParamMapping> mappings,
            Action<string> onDone)
        {
            _copyHandler.SetRequest(new RunCopyRequest(pairs, mappings, onDone));
            _copyEvent.Raise();
        }

        /// <summary>
        /// Loads Model-type category names present in the active document.
        /// Shared between Source and Dest category dropdowns.
        /// </summary>
        public void LoadCategories(Action<List<string>> onCompleted)
        {
            _categoriesHandler.Request(onCompleted);
            _categoriesEvent.Raise();
        }

        /// <summary>
        /// Loads instance parameter names for all elements of the given
        /// category, ignoring FilterSets. Used for the Source filter-rule
        /// ParameterName dropdowns.
        /// </summary>
        public void LoadSourceCategoryParameters(
            string categoryName, Action<List<string>> onCompleted)
        {
            _srcCategoryParamsHandler.SetRequest(
                new LoadParameterNamesRequest(categoryName, filterSets: null, onCompleted));
            _srcCategoryParamsEvent.Raise();
        }

        /// <summary>
        /// Loads instance parameter names for all elements of the given
        /// category, ignoring FilterSets. Used for the Dest filter-rule
        /// ParameterName dropdowns.
        /// </summary>
        public void LoadDestCategoryParameters(
            string categoryName, Action<List<string>> onCompleted)
        {
            _dstCategoryParamsHandler.SetRequest(
                new LoadParameterNamesRequest(categoryName, filterSets: null, onCompleted));
            _dstCategoryParamsEvent.Raise();
        }

        /// <summary>
        /// Loads instance parameter names for elements of the given category
        /// that also match the given FilterSets. Used for Source Display
        /// Params, Pairing Parameter candidates, and the Mapping grid's
        /// Source Parameter Name column.
        /// </summary>
        public void LoadSourceMatchedParameters(
            string categoryName,
            IReadOnlyList<FilterSet> filterSets,
            Action<List<string>> onCompleted)
        {
            _srcMatchedParamsHandler.SetRequest(
                new LoadParameterNamesRequest(categoryName, filterSets, onCompleted));
            _srcMatchedParamsEvent.Raise();
        }

        /// <summary>
        /// Loads instance parameter names for elements of the given category
        /// that also match the given FilterSets. Used for Dest Display
        /// Params, Pairing Parameter candidates, and the Mapping grid's
        /// Dest Parameter Name column.
        /// </summary>
        public void LoadDestMatchedParameters(
            string categoryName,
            IReadOnlyList<FilterSet> filterSets,
            Action<List<string>> onCompleted)
        {
            _dstMatchedParamsHandler.SetRequest(
                new LoadParameterNamesRequest(categoryName, filterSets, onCompleted));
            _dstMatchedParamsEvent.Raise();
        }

        public void Dispose() { }
    }
}
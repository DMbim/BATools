// BA/Markup/Commands/RefreshRevisionsHandler.cs
using System;
using System.Collections.Generic;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Markup.Models;
using BA.Markup.ViewModels;

namespace BA.Markup.Commands
{
    /// <summary>
    /// IExternalEventHandler that reads available Revit Revisions and pushes
    /// them back to the MarkupViewModel on the UI thread.
    /// </summary>
    public sealed class RefreshRevisionsHandler : IExternalEventHandler
    {
        private MarkupViewModel? _viewModel;
        private Dispatcher? _uiDispatcher;

        /// <summary>
        /// Must be called before raising the ExternalEvent.
        /// </summary>
        public void Prepare(MarkupViewModel viewModel, Dispatcher uiDispatcher)
        {
            _viewModel = viewModel;
            _uiDispatcher = uiDispatcher;
        }

        public void Execute(UIApplication app)
        {
            if (_viewModel == null || _uiDispatcher == null)
                return;

            var doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;

            var items = ReadRevisions(doc);

            // Push back to ViewModel on the WPF dispatcher thread.
            _uiDispatcher.Invoke(() => _viewModel.UpdateRevisions(items));
        }

        public string GetName() => "BA.Markup.RefreshRevisions";

        // ------------------------------------------------------------------ //

        private static List<RevisionItem> ReadRevisions(Document doc)
        {
            var result = new List<RevisionItem>();

            // Revision elements are not enumerable via FilteredElementCollector directly;
            // use Revision.GetAllRevisionIds() which is the correct Revit 2026 API.
            var ids = Revision.GetAllRevisionIds(doc);
            foreach (var id in ids)
            {
                if (doc.GetElement(id) is Revision rev)
                {
                    result.Add(new RevisionItem
                    {
                        ElementId = (int)id.Value,
                        DisplayName = $"{rev.SequenceNumber} — {rev.Description} ({rev.RevisionDate})"
                    });
                }
            }

            return result;
        }
    }
}
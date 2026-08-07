// BA/Markup/Commands/RevisionManagerHandler.cs
using System;
using System.Collections.Generic;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Markup.Models;

namespace BA.Markup.Commands
{
    public enum RevisionHandlerOperation
    {
        Load,
        Save,
        Create
    }

    public sealed class RevisionManagerHandler : IExternalEventHandler
    {
        private readonly object _lock = new();

        private volatile RevisionHandlerOperation _operation = RevisionHandlerOperation.Load;
        private RevisionEditModel? _payload;
        private Dispatcher? _dispatcher;
        private Action<IReadOnlyList<RevisionItem>>? _onLoaded;
        private Action<RevisionItem>? _onSaved;
        private Action<string>? _onError;

        // ------------------------------------------------------------------ //
        //  Prepare overloads (retained for future modeless window use)
        // ------------------------------------------------------------------ //

        public void PrepareLoad(
            Dispatcher dispatcher,
            Action<IReadOnlyList<RevisionItem>> onLoaded,
            Action<string> onError)
        {
            lock (_lock)
            {
                _operation = RevisionHandlerOperation.Load;
                _payload = null;
                _dispatcher = dispatcher;
                _onLoaded = onLoaded;
                _onSaved = null;
                _onError = onError;
            }
        }

        public void PrepareSave(
            RevisionEditModel payload,
            Dispatcher dispatcher,
            Action<RevisionItem> onSaved,
            Action<string> onError)
        {
            if (payload.IsNew)
                throw new ArgumentException(
                    "Cannot Save a model with ElementId < 0. Use PrepareCreate.");

            lock (_lock)
            {
                _operation = RevisionHandlerOperation.Save;
                _payload = payload;
                _dispatcher = dispatcher;
                _onLoaded = null;
                _onSaved = onSaved;
                _onError = onError;
            }
        }

        public void PrepareCreate(
            RevisionEditModel payload,
            Dispatcher dispatcher,
            Action<RevisionItem> onCreated,
            Action<string> onError)
        {
            if (!payload.IsNew)
                throw new ArgumentException(
                    "Cannot Create a model with an existing ElementId. Use PrepareSave.");

            lock (_lock)
            {
                _operation = RevisionHandlerOperation.Create;
                _payload = payload;
                _dispatcher = dispatcher;
                _onLoaded = null;
                _onSaved = onCreated;
                _onError = onError;
            }
        }

        // ------------------------------------------------------------------ //
        //  IExternalEventHandler (retained for future modeless use)
        // ------------------------------------------------------------------ //

        public void Execute(UIApplication app)
        {
            RevisionHandlerOperation operation;
            RevisionEditModel? payload;
            Dispatcher? dispatcher;
            Action<IReadOnlyList<RevisionItem>>? onLoaded;
            Action<RevisionItem>? onSaved;
            Action<string>? onError;

            lock (_lock)
            {
                operation = _operation;
                payload = _payload;
                dispatcher = _dispatcher;
                onLoaded = _onLoaded;
                onSaved = _onSaved;
                onError = _onError;
            }

            if (dispatcher == null) return;

            var doc = app.ActiveUIDocument?.Document;
            if (doc == null)
            {
                dispatcher.Invoke(() => onError?.Invoke("No active document."));
                return;
            }

            try
            {
                switch (operation)
                {
                    case RevisionHandlerOperation.Load:
                        var items = ReadAllRevisions(doc);
                        dispatcher.Invoke(() => onLoaded?.Invoke(items));
                        break;

                    case RevisionHandlerOperation.Save:
                        var saved = SaveRevisionSync(doc, payload!);
                        dispatcher.Invoke(() => onSaved?.Invoke(saved));
                        break;

                    case RevisionHandlerOperation.Create:
                        var created = CreateRevisionSync(doc, payload!);
                        dispatcher.Invoke(() => onSaved?.Invoke(created));
                        break;
                }
            }
            catch (Exception ex)
            {
                dispatcher.Invoke(() => onError?.Invoke(ex.Message));
            }
        }

        public string GetName() => "BA.Markup.RevisionManager";

        // ------------------------------------------------------------------ //
        //  Public static synchronous API
        //  Called directly from PlaceMarkupCommand on the Revit API thread.
        //  No transaction wrapping needed in the caller — each method owns
        //  its own transaction.
        // ------------------------------------------------------------------ //

        public static IReadOnlyList<RevisionItem> ReadAllRevisions(Document doc)
        {
            var result = new List<RevisionItem>();
            var ids = Revision.GetAllRevisionIds(doc);

            foreach (var id in ids)
            {
                if (doc.GetElement(id) is Revision rev)
                    result.Add(BuildRevisionItem(rev));
            }

            return result;
        }

        // <- NEW: synchronous save, called directly on the API thread.
        public static RevisionItem SaveRevisionSync(Document doc, RevisionEditModel payload)
        {
            var id = new ElementId(payload.ElementId);
            var rev = doc.GetElement(id) as Revision
                ?? throw new InvalidOperationException(
                    $"Revision with ElementId {payload.ElementId} no longer exists.");

            using var tx = new Transaction(doc, "BA — Edit Revision");
            tx.Start();

            rev.RevisionDate = payload.RevisionDate;
            rev.Description = payload.Description;
            rev.Issued = payload.Issued;
            rev.IssuedBy = payload.IssuedBy;
            rev.IssuedTo = payload.IssuedTo;

            tx.Commit();

            return BuildRevisionItem(rev);
        }

        // <- NEW: synchronous create, called directly on the API thread.
        public static RevisionItem CreateRevisionSync(Document doc, RevisionEditModel payload)
        {
            using var tx = new Transaction(doc, "BA — Create Revision");
            tx.Start();

            var rev = Revision.Create(doc);

            rev.RevisionDate = payload.RevisionDate;
            rev.Description = payload.Description;
            rev.Issued = payload.Issued;
            rev.IssuedBy = payload.IssuedBy;
            rev.IssuedTo = payload.IssuedTo;

            tx.Commit();

            return BuildRevisionItem(rev);
        }

        // ------------------------------------------------------------------ //
        //  Shared helpers
        // ------------------------------------------------------------------ //

        internal static RevisionItem BuildRevisionItem(Revision rev) => new()
        {
            ElementId = (int)rev.Id.Value,
            SequenceNumber = rev.SequenceNumber,
            RevisionDate = rev.RevisionDate ?? string.Empty,
            Description = rev.Description ?? string.Empty,
            Issued = rev.Issued,
            IssuedBy = rev.IssuedBy ?? string.Empty,
            IssuedTo = rev.IssuedTo ?? string.Empty
        };
    }
}
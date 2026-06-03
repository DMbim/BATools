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

        private readonly ExternalEvent _srcEvent;
        private readonly ExternalEvent _dstEvent;
        private readonly ExternalEvent _copyEvent;

        public ParamCopyExternalInvoker(UIApplication uiApp)
        {
            _srcHandler  = new ReloadSourceHandler();
            _dstHandler  = new ReloadDestHandler();
            _copyHandler = new RunCopyHandler();

            _srcEvent  = ExternalEvent.Create(_srcHandler);
            _dstEvent  = ExternalEvent.Create(_dstHandler);
            _copyEvent = ExternalEvent.Create(_copyHandler);
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

        public void Dispose() { }
    }
}

using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using View = Autodesk.Revit.DB.View;

namespace BA.UI
{
    public class ShowElementHandler : IExternalEventHandler
    {
        private UIApplication _uiApp;
        private ElementId _viewId;
        private ElementId _elementId;

        public void Request(UIApplication uiApp, ElementId viewId, ElementId elementId)
        {
            _uiApp = uiApp;
            _viewId = viewId;
            _elementId = elementId;
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var uiDoc = _uiApp.ActiveUIDocument;
                if (uiDoc == null) return;

                var doc = uiDoc.Document;

                if (_viewId != null && _viewId != ElementId.InvalidElementId)
                {
                    var v = doc.GetElement(_viewId) as View;
                    if (v != null && !v.IsTemplate)
                    {
                        uiDoc.RequestViewChange(v);
                    }
                }

                if (_elementId != null && _elementId != ElementId.InvalidElementId)
                {
                    var el = doc.GetElement(_elementId);
                    if (el != null)
                    {
                        var ids = new HashSet<ElementId> { _elementId };
                        uiDoc.Selection.SetElementIds(ids);
                        uiDoc.ShowElements(ids);
                    }
                }
            }
            catch
            {
                // swallow – this is UI convenience, not critical
            }
        }

        public string GetName() => "BA Show Element Handler";
    }
}

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Core.Content.Models;
using System;
using System.Linq;
using static BA.UI.ContentBrowser.ContentBrowserViewModel;

namespace BA.Core.Content.Revit
{
    public sealed class ContentLoadExternalHandler : IExternalEventHandler
    {
        private ContentLoadRequest? _request;
        private string _lastError = string.Empty;

        public void SetRequest(ContentLoadRequest request)
        {
            _request = request ?? throw new ArgumentNullException(nameof(request));
            _lastError = string.Empty;
        }

        public string ConsumeLastError()
        {
            string value = _lastError;
            _lastError = string.Empty;
            return value;
        }

        public void Execute(UIApplication app)
        {
            try
            {
                if (_request == null)
                    return;

                UIDocument uiDoc = app.ActiveUIDocument;
                Document doc = uiDoc?.Document ?? throw new InvalidOperationException("No active document.");

                if (doc.IsFamilyDocument)
                    throw new InvalidOperationException("Load target must be a project document, not a family document.");

                if (string.IsNullOrWhiteSpace(_request.FamilyPath))
                    throw new InvalidOperationException("Family path is empty.");

                if (!System.IO.File.Exists(_request.FamilyPath))
                    throw new InvalidOperationException("Family file not found.");

                Family loadedFamily;

                using (var tx = new Transaction(doc, "BA Load Content Family"))
                {
                    tx.Start();

                    var options = new AlwaysOverwriteLoadOptions();
                    bool ok = doc.LoadFamily(_request.FamilyPath, options, out loadedFamily!);
                    if (!ok || loadedFamily == null)
                        throw new InvalidOperationException("Revit failed to load the family.");

                    if (_request.ActivateFirstSymbol)
                    {
                        var symbolId = loadedFamily.GetFamilySymbolIds().FirstOrDefault();
                        if (symbolId != ElementId.InvalidElementId)
                        {
                            var symbol = doc.GetElement(symbolId) as FamilySymbol;
                            if (symbol != null && !symbol.IsActive)
                                symbol.Activate();
                        }
                    }

                    tx.Commit();
                }

                if (_request.PlaceAfterLoad)
                {
                    var symbolId = loadedFamily.GetFamilySymbolIds().FirstOrDefault();
                    if (symbolId == ElementId.InvalidElementId)
                        throw new InvalidOperationException("No symbol found in loaded family.");

                    var symbol = doc.GetElement(symbolId) as FamilySymbol;
                    if (symbol == null)
                        throw new InvalidOperationException("Failed to get family symbol.");

                    uiDoc.PostRequestForElementTypePlacement(symbol);
                }
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
            }
            finally
            {
                _request = null;
            }
        }

        public string GetName()
        {
            return "BA Content Load External Handler";
        }
    }
}
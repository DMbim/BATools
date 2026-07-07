using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BATools.SelectionManager.ExternalEvents
{
    public class PlaceFamilyHandler : IExternalEventHandler
    {
        /// <summary>UniqueId of the FamilySymbol to place.</summary>
        public string SymbolUniqueId { get; set; } = string.Empty;
        public string FamilyName { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;

        public void Execute(UIApplication uiApp)
        {
            var uidoc = uiApp.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null || uidoc == null) return;

            FamilySymbol? symbol = ResolveSymbol(doc);
            if (symbol == null)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[PlaceFamilyHandler] Could not resolve: {FamilyName} / {TypeName}");
                return;
            }

            // Activate the symbol if needed — requires its own transaction
            // because PromptForFamilyInstancePlacement cannot run inside a transaction
            if (!symbol.IsActive)
            {
                try
                {
                    using var tx = new Transaction(doc, "BATools: Activate Family Symbol");
                    tx.Start();
                    symbol.Activate();
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[PlaceFamilyHandler] Activation failed: {ex.Message}");
                    return;
                }
            }

            try
            {
                // This call is interactive and blocks until the user
                // finishes placing instances (clicks Escape or Finish)
                uidoc.PromptForFamilyInstancePlacement(symbol);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // User pressed Escape — expected, not an error
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[PlaceFamilyHandler] Placement failed: {ex.Message}");
            }
        }

        private FamilySymbol? ResolveSymbol(Document doc)
        {
            // 1 — Try UniqueId cache (fast path)
            if (!string.IsNullOrEmpty(SymbolUniqueId))
            {
                var byId = doc.GetElement(SymbolUniqueId) as FamilySymbol;
                if (byId != null) return byId;
            }

            // 2 — Fall back to name search (cross-project portability)
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(s =>
                    s.FamilyName == FamilyName &&
                    s.Name == TypeName);
        }

        public string GetName() => "PlaceFamily";
    }
}
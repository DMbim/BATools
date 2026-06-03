using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.Subcategories.Services;
using BA.Subcategories.ViewModels;
using BA.Subcategories.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace BA.Subcategories.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SubcategoryManagerCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document? doc = uiDoc?.Document;

            if (doc == null)
            {
                message = "No active document.";
                return Result.Failed;
            }

            if (!doc.IsFamilyDocument)
            {
                TaskDialog.Show("BA Subcategories",
                    "Open a Family document to run this command.");
                return Result.Cancelled;
            }

            Family? fam = doc.OwnerFamily;
            Category? famCat = fam?.FamilyCategory;

            if (famCat == null)
            {
                TaskDialog.Show("BA Subcategories", "Family category not found.");
                return Result.Failed;
            }

            Category? parentCategory = ResolveParentCategory(doc, famCat);
            if (parentCategory == null)
            {
                TaskDialog.Show("BA Subcategories",
                    "Unable to resolve parent category for this family.");
                return Result.Failed;
            }

            var vm = new SubcategoryManagerViewModel
            {
                Doc            = doc,
                ParentCategory = parentCategory,
                OwnerFamily    = fam!
            };
            vm.Initialise();

            var window = new SubcategoryManagerWindow(vm);
            new WindowInteropHelper(window).Owner = uiApp.MainWindowHandle;

            bool? result = window.ShowDialog();

            if (result != true)
                return Result.Cancelled;

            if (vm.ApplyLog.Count > 0)
            {
                // Already shown inside Apply — no second dialog needed.
            }

            return Result.Succeeded;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static Category? ResolveParentCategory(Document doc, Category famCat)
        {
            // Primary: resolve by BuiltInCategory value
            try
            {
                var bic = (BuiltInCategory)famCat.Id.Value;
                var cat = doc.Settings.Categories.get_Item(bic);
                if (cat != null) return cat;
            }
            catch { }

            // Fallback: match by name
            foreach (Category c in doc.Settings.Categories)
            {
                if (string.Equals(c.Name, famCat.Name,
                        StringComparison.OrdinalIgnoreCase))
                    return c;
            }

            return null;
        }
    }
}

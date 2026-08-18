// BA/Commands/Cmd_InstallGhostMarkupSetup.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.Core.GhostMarkup;

namespace BA.Commands
{
    /// <summary>
    /// One time, idempotent setup command. Creates the BA_NPLT Line Style
    /// subcategory under Lines and colors it magenta, and creates the
    /// BA_NPLT_Ghost view filter (Type Name begins with BA_NPLT, scoped to
    /// Text Notes and Detail Items) with a magenta halftone override,
    /// applying it to the active view only. Safe to run repeatedly, it
    /// checks for existing resources before creating new ones.
    ///
    /// Does not touch view templates, that has to be added to each template
    /// manually or by a separate script once the office confirms which
    /// templates should carry the ghost markup filter.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_InstallGhostMarkupSetup : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return Run(commandData.Application, ref message);
        }

        public static Result Run(UIApplication uiApp, ref string message)
        {
            var uiDoc = uiApp.ActiveUIDocument;
            if (uiDoc == null)
            {
                message = "No active document.";
                return Result.Failed;
            }

            var doc = uiDoc.Document;
            var activeView = doc.ActiveView;

            if (activeView == null || activeView.IsTemplate)
            {
                message = "Activate a graphical view before running setup.";
                return Result.Failed;
            }

            try
            {
                ElementId filterId;

                using (var tx = new Transaction(doc, "Install Ghost Markup Setup"))
                {
                    tx.Start();

                    EnsureLineStyle(doc);
                    filterId = EnsureFilter(doc);
                    ApplyFilterToView(doc, activeView, filterId);

                    tx.Commit();
                }

                AppLogger.LogInfo("Ghost markup setup installed or already present.");

                TaskDialog.Show(
                    "Ghost Markup Setup",
                    "Ghost markup line style and view filter are installed.\n\n" +
                    "The filter (" + GhostMarkupConstants.FilterName + ") has been added to the active view only. " +
                    "Add it to your view templates for it to appear everywhere ghost markup should show as magenta halftone.");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Ghost markup setup install failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static void EnsureLineStyle(Document doc)
        {
            var linesCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            if (linesCategory == null)
            {
                throw new InvalidOperationException("Lines category not found in document.");
            }

            Category ghostSub = null;

            foreach (Category sub in linesCategory.SubCategories)
            {
                if (string.Equals(sub.Name, GhostMarkupConstants.LineStyleName, StringComparison.OrdinalIgnoreCase))
                {
                    ghostSub = sub;
                    break;
                }
            }

            if (ghostSub == null)
            {
                ghostSub = doc.Settings.Categories.NewSubcategory(linesCategory, GhostMarkupConstants.LineStyleName);
            }

            ghostSub.LineColor = GhostMarkupConstants.GhostColor;
        }

        private static ElementId EnsureFilter(Document doc)
        {
            var existingFilters = new FilteredElementCollector(doc)
                .OfClass(typeof(ParameterFilterElement))
                .Cast<ParameterFilterElement>();

            foreach (var pfe in existingFilters)
            {
                if (string.Equals(pfe.Name, GhostMarkupConstants.FilterName, StringComparison.OrdinalIgnoreCase))
                {
                    return pfe.Id;
                }
            }

            var categories = new List<ElementId>
            {
                new ElementId(BuiltInCategory.OST_TextNotes),
                new ElementId(BuiltInCategory.OST_DetailComponents)
            };

            var typeNameParamId = new ElementId(BuiltInParameter.ALL_MODEL_TYPE_NAME);

            // Flagging this rather than presenting it as fact: verify this exact
            // overload of ParameterFilterRuleFactory.CreateBeginsWithRule against
            // the installed Revit 2026 SDK. The case sensitivity parameter on this
            // factory method has changed shape across recent API versions, and I
            // am not certain of the current signature without the SDK in front of me.
            var rule = ParameterFilterRuleFactory.CreateBeginsWithRule(
                typeNameParamId, GhostMarkupConstants.PrefixToken);

            var elementFilter = new ElementParameterFilter(rule);

            var newFilter = ParameterFilterElement.Create(
                doc, GhostMarkupConstants.FilterName, categories, elementFilter);

            return newFilter.Id;
        }

        private static void ApplyFilterToView(Document doc, View view, ElementId filterId)
        {
            var appliedFilterIds = view.GetFilters();

            if (!appliedFilterIds.Contains(filterId))
            {
                view.AddFilter(filterId);
            }

            var overrides = new OverrideGraphicSettings();
            overrides.SetProjectionLineColor(GhostMarkupConstants.GhostColor);
            overrides.SetHalftone(true);

            view.SetFilterOverrides(filterId, overrides);
        }
    }
}

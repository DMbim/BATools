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
    /// subcategory under Lines and colors it magenta, and creates one or
    /// two BA_NPLT_Ghost ParameterFilterElements (Type Name begins with
    /// BA_NPLT), scoped to Text Notes and Detail Items, with a magenta
    /// halftone override, applying them to the active view only.
    ///
    /// Type Name is not guaranteed to be a filterable parameter across
    /// both Text Notes and Detail Items combined, confirmed in production:
    /// ParameterFilterElement.Create throws "One of the given rules refers
    /// to a parameter that does not apply to this filter's categories"
    /// when the parameter ID is not in the intersection returned by
    /// ParameterFilterUtilities.GetFilterableParametersInCommon for the
    /// full category set. This resolves the parameter dynamically against
    /// that intersection first, and falls back to two separate single
    /// category filters, each resolved against its own filterable set,
    /// if no parameter is common to both.
    ///
    /// Safe to run repeatedly, it checks for existing resources before
    /// creating new ones.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_InstallGhostMarkupSetup : IExternalCommand
    {
        private static readonly BuiltInParameter[] TypeNameParameterCandidates =
        {
            BuiltInParameter.ALL_MODEL_TYPE_NAME,
            BuiltInParameter.SYMBOL_NAME_PARAM,
            BuiltInParameter.ELEM_TYPE_PARAM
        };

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
                List<ElementId> filterIds;

                using (var tx = new Transaction(doc, "Install Ghost Markup Setup"))
                {
                    tx.Start();

                    EnsureLineStyle(doc);
                    filterIds = EnsureFilters(doc);

                    foreach (var filterId in filterIds)
                    {
                        ApplyFilterToView(doc, activeView, filterId);
                    }

                    tx.Commit();
                }

                AppLogger.LogInfo($"Ghost markup setup installed or already present, {filterIds.Count} filter(s) applied to active view.");

                TaskDialog.Show(
                    "Ghost Markup Setup",
                    "Ghost markup line style and view filter(s) are installed.\n\n" +
                    filterIds.Count + " filter(s) applied to the active view. " +
                    "Add them to your view templates for ghost markup to appear as magenta halftone everywhere.");

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

        /// <summary>
        /// Resolves and creates the view filter(s) needed to cover Text
        /// Notes and Detail Items. Tries one combined filter first, falls
        /// back to one filter per category if no Type Name equivalent
        /// parameter is common to both. Either category individually
        /// failing to resolve a parameter is logged and skipped rather
        /// than aborting setup for the category that does work.
        /// </summary>
        private static List<ElementId> EnsureFilters(Document doc)
        {
            var textNoteCategory = new ElementId(BuiltInCategory.OST_TextNotes);
            var detailItemCategory = new ElementId(BuiltInCategory.OST_DetailComponents);

            var combinedCategories = new List<ElementId> { textNoteCategory, detailItemCategory };
            var combinedParam = ResolveTypeNameParameter(doc, combinedCategories);

            if (combinedParam != null)
            {
                var filterId = EnsureSingleFilter(
                    doc, GhostMarkupConstants.FilterName, combinedCategories, combinedParam);

                return new List<ElementId> { filterId };
            }

            AppLogger.LogInfo(
                "Ghost markup: no Type Name equivalent parameter common to Text Notes and Detail Items together, falling back to one filter per category.");

            var result = new List<ElementId>();

            var textNoteParam = ResolveTypeNameParameter(doc, new List<ElementId> { textNoteCategory });
            if (textNoteParam != null)
            {
                result.Add(EnsureSingleFilter(
                    doc,
                    GhostMarkupConstants.FilterName + "_TextNotes",
                    new List<ElementId> { textNoteCategory },
                    textNoteParam));
            }
            else
            {
                AppLogger.LogError(
                    "Ghost markup filter setup",
                    new InvalidOperationException("No Type Name equivalent filterable parameter found for Text Notes category."));
            }

            var detailItemParam = ResolveTypeNameParameter(doc, new List<ElementId> { detailItemCategory });
            if (detailItemParam != null)
            {
                result.Add(EnsureSingleFilter(
                    doc,
                    GhostMarkupConstants.FilterName + "_DetailItems",
                    new List<ElementId> { detailItemCategory },
                    detailItemParam));
            }
            else
            {
                AppLogger.LogError(
                    "Ghost markup filter setup",
                    new InvalidOperationException("No Type Name equivalent filterable parameter found for Detail Items category."));
            }

            if (result.Count == 0)
            {
                throw new InvalidOperationException(
                    "Could not resolve any Type Name equivalent filterable parameter for the ghost markup categories. Check the log for which candidates were tried.");
            }

            return result;
        }

        private static ElementId ResolveTypeNameParameter(Document doc, List<ElementId> categoryIds)
        {
            var filterable = new HashSet<ElementId>(
                ParameterFilterUtilities.GetFilterableParametersInCommon(doc, categoryIds));

            foreach (var candidate in TypeNameParameterCandidates)
            {
                var candidateId = new ElementId(candidate);
                if (filterable.Contains(candidateId))
                {
                    return candidateId;
                }
            }

            return null;
        }

        private static ElementId EnsureSingleFilter(
            Document doc, string name, List<ElementId> categories, ElementId paramId)
        {
            var existingFilters = new FilteredElementCollector(doc)
                .OfClass(typeof(ParameterFilterElement))
                .Cast<ParameterFilterElement>();

            foreach (var pfe in existingFilters)
            {
                if (string.Equals(pfe.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return pfe.Id;
                }
            }

            // Flagging this rather than presenting it as fact: the two
            // argument overload of CreateBeginsWithRule was deprecated in
            // recent API versions in favor of this three argument form with
            // an explicit caseSensitive flag. Verify this against the
            // installed Revit 2026 SDK before trusting it, this is the
            // second time this factory call has needed correction, do not
            // assume it compiles cleanly without checking.
            var rule = ParameterFilterRuleFactory.CreateBeginsWithRule(
                paramId, GhostMarkupConstants.PrefixToken);

            var elementFilter = new ElementParameterFilter(rule);

            var newFilter = ParameterFilterElement.Create(doc, name, categories, elementFilter);
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
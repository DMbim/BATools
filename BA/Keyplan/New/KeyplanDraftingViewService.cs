using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.Keyplan
{
    public static class KeyplanDraftingViewService
    {
        public static KeyplanGenerationResult Generate(Document doc, KeyplanGenerationOptions options)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (options == null) throw new ArgumentNullException(nameof(options));

            View sourceView = KeyplanViewUtils.FindViewByName(doc, options.SourceViewName);
            if (sourceView == null)
            {
                throw new InvalidOperationException(
                    $"Source view '{options.SourceViewName}' was not found.");
            }

            if (!KeyplanViewUtils.IsSupported2DSourceView(sourceView))
            {
                throw new InvalidOperationException(
                    $"Source view '{sourceView.Name}' is not a supported 2D graphics view. " +
                    $"Use a Floor Plan, Area Plan, Ceiling Plan, Section, Elevation, Detail, or Drafting view.");
            }

            KeyplanGenerationResult result = new KeyplanGenerationResult
            {
                SourceViewId = sourceView.Id,
                SourceViewName = sourceView.Name
            };

            using (TransactionGroup tg = new TransactionGroup(doc, "Generate Keyplan Drafting View"))
            {
                tg.Start();

                ViewDrafting targetView;

                using (Transaction tx = new Transaction(doc, "Create / Resolve target drafting view"))
                {
                    tx.Start();

                    targetView = KeyplanViewUtils.FindViewByName(doc, options.TargetDraftingViewName) as ViewDrafting;
                    if (targetView == null)
                    {
                        targetView = KeyplanViewUtils.CreateDraftingView(doc, options.TargetDraftingViewName);
                    }

                    FailureHandlingOptions fho = tx.GetFailureHandlingOptions();
                    fho.SetFailuresPreprocessor(new KeyplanFailuresPreprocessor());
                    tx.SetFailureHandlingOptions(fho);
                    tx.Commit();
                }

                result.GeneratedViewId = targetView.Id;
                result.TargetViewName = targetView.Name;

                if (options.DeleteExistingTargetContents)
                {
                    using (Transaction tx = new Transaction(doc, "Clear existing keyplan target contents"))
                    {
                        tx.Start();

                        IList<ElementId> existingIds = KeyplanElementCollector
                            .CollectAllOwnedViewElements(doc, targetView.Id);

                        if (existingIds.Count > 0)
                        {
                            doc.Delete(existingIds);
                        }

                        result.DeletedElementCount = existingIds.Count;
                        FailureHandlingOptions fho = tx.GetFailureHandlingOptions();
                        fho.SetFailuresPreprocessor(new KeyplanFailuresPreprocessor());
                        tx.SetFailureHandlingOptions(fho);

                        tx.Commit();
                    }
                }

                if (options.CopyViewSpecificElements)
                {
                    using (Transaction tx = new Transaction(doc, "Copy view-specific keyplan contents"))
                    {
                        tx.Start();

                        ICollection<ElementId> sourceSpecificIds =
                            KeyplanElementCollector.CollectCopyableViewSpecificElementIds(doc, sourceView);

                        if (sourceSpecificIds.Count > 0)
                        {
                            CopyPasteOptions copyOptions = new CopyPasteOptions();
                            copyOptions.SetDuplicateTypeNamesHandler(new 
                                UseDestinationTypesHandler());

                            ElementTransformUtils.CopyElements(
                                sourceView,
                                sourceSpecificIds,
                                targetView,
                                Transform.Identity,
                                copyOptions);

                            result.CopiedViewSpecificCount = sourceSpecificIds.Count;
                        }
                        FailureHandlingOptions fho = tx.GetFailureHandlingOptions();
                        fho.SetFailuresPreprocessor(new KeyplanFailuresPreprocessor());
                        tx.SetFailureHandlingOptions(fho);
                        tx.Commit();
                    }
                }

                if (options.RecreateVisibleNonViewSpecificCurves)
                {
                    using (Transaction tx = new Transaction(doc, "Recreate visible model / boundary curves as detail curves"))
                    {
                        tx.Start();

                        KeyplanCurveRebuildResult curveResult =
                            KeyplanCurveRebuilder.RecreateVisibleNonViewSpecificCurves(doc, sourceView, targetView);

                        result.RecreatedCurveCount = curveResult.CreatedCount;
                        result.SkippedCurveCount = curveResult.SkippedCount;
                        FailureHandlingOptions fho = tx.GetFailureHandlingOptions();
                        fho.SetFailuresPreprocessor(new KeyplanFailuresPreprocessor());
                        tx.SetFailureHandlingOptions(fho);
                        tx.Commit();
                    }
                }

                if (!string.IsNullOrWhiteSpace(options.TargetDraftingViewTemplateName))
                {
                    using (Transaction tx = new Transaction(doc, "Apply keyplan drafting template"))
                    {
                        tx.Start();

                        View template = KeyplanViewUtils.FindTemplateByName(
                            doc,
                            options.TargetDraftingViewTemplateName,
                            ViewType.DraftingView);

                        if (template != null && targetView.IsValidViewTemplate(template.Id))
                        {
                            targetView.ViewTemplateId = template.Id;
                            result.AppliedTemplateName = template.Name;
                        }
                        FailureHandlingOptions fho = tx.GetFailureHandlingOptions();
                        fho.SetFailuresPreprocessor(new KeyplanFailuresPreprocessor());
                        tx.SetFailureHandlingOptions(fho);
                        tx.Commit();
                    }
                }

                tg.Assimilate();
            }

            return result;
        }
    }
}
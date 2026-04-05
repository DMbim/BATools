using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.Keyplan
{
    public static class KeyplanSheetUpdateService
    {
        public static KeyplanSheetUpdateResult UpdateSheets(
            UIApplication uiApp,
            Document doc,
            KeyplanSheetUpdateOptions options)
        {
            if (uiApp == null) throw new ArgumentNullException(nameof(uiApp));
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (options == null) throw new ArgumentNullException(nameof(options));

            KeyplanSheetUpdateResult result = new KeyplanSheetUpdateResult();

            using (TransactionGroup tg = new TransactionGroup(doc, "Update Keyplans On Sheets"))
            {
                tg.Start();

                if (options.CreateSharedParameterIfMissing)
                {
                    using (Transaction tx = new Transaction(doc, "Ensure BA_KeyplanZone parameter"))
                    {
                        tx.Start();
                        KeyplanSharedParameterService.EnsureSheetTextSharedParameter(
                            uiApp,
                            doc,
                            options.SharedParameterFilePath,
                            options.SheetZoneParameterName);
                        tx.Commit();
                    }
                }

                if (options.RegenerateBaseViewFirst)
                {
                    using (Transaction tx = new Transaction(doc, "Generate base keyplan drafting view"))
                    {
                        tx.Start();

                        KeyplanGenerationOptions genOptions = KeyplanGenerationOptions.CreateDefault();
                        genOptions.SourceViewName = options.SourceViewName;
                        genOptions.TargetDraftingViewName = options.BaseDraftingViewName;
                        genOptions.TargetDraftingViewTemplateName = options.DraftingTemplateName;
                        genOptions.DeleteExistingTargetContents = true;
                        genOptions.CopyViewSpecificElements = true;
                        genOptions.RecreateVisibleNonViewSpecificCurves = true;

                        // call phase 1 generator from the previous implementation
                        KeyplanDraftingViewService.Generate(doc, genOptions);

                        tx.Commit();
                    }
                }

                ViewDrafting baseView = KeyplanViewUtils.FindViewByName(doc, options.BaseDraftingViewName) as ViewDrafting;
                if (baseView == null)
                    throw new InvalidOperationException($"Base drafting view '{options.BaseDraftingViewName}' was not found.");

                ElementId activeTypeId = KeyplanFilledRegionUtils.FindFilledRegionTypeIdByName(doc, options.ActiveFilledRegionTypeName);
                ElementId inactiveTypeId = KeyplanFilledRegionUtils.FindFilledRegionTypeIdByName(doc, options.InactiveFilledRegionTypeName);

                if (activeTypeId == ElementId.InvalidElementId)
                    throw new InvalidOperationException($"Filled region type '{options.ActiveFilledRegionTypeName}' was not found.");

                if (inactiveTypeId == ElementId.InvalidElementId)
                    throw new InvalidOperationException($"Filled region type '{options.InactiveFilledRegionTypeName}' was not found.");

                List<ViewSheet> sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(s => s != null && !s.IsPlaceholder)
                    .OrderBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                result.TotalSheets = sheets.Count;

                foreach (ViewSheet sheet in sheets)
                {
                    try
                    {
                        string zone = KeyplanParameterUtils.GetSheetZoneValue(sheet, options.SheetZoneParameterName);

                        if (string.IsNullOrWhiteSpace(zone))
                        {
                            result.SkippedSheets++;
                            continue;
                        }

                        result.SheetsWithZone++;

                        string targetViewName = KeyplanNamingUtils.BuildSheetSpecificViewName(
                            options.GeneratedViewPrefix,
                            sheet.SheetNumber,
                            zone);

                        ViewDrafting targetView;

                        using (Transaction tx = new Transaction(doc, $"Create or update keyplan view for sheet {sheet.SheetNumber}"))
                        {
                            tx.Start();

                            targetView = KeyplanZoneViewService.CreateOrUpdateSheetSpecificKeyplanView(
                                doc,
                                baseView,
                                targetViewName,
                                zone,
                                activeTypeId,
                                inactiveTypeId,
                                options.ReuseExistingSheetSpecificView,
                                out bool createdNew);

                            if (createdNew) result.CreatedViews++;
                            else result.UpdatedViews++;

                            tx.Commit();
                        }

                        using (Transaction tx = new Transaction(doc, $"Place keyplan on sheet {sheet.SheetNumber}"))
                        {
                            tx.Start();

                            Viewport vp = KeyplanSheetPlacementService.PlaceOrReplaceKeyplanViewport(
                                doc,
                                sheet,
                                targetView,
                                options.GeneratedViewPrefix,
                                options.DeleteOldKeyplanViewportOnSheet);

                            if (vp != null)
                            {
                                result.PlacedViewports++;
                                bool moved = KeyplanSheetPlacementService.MoveViewportToTitleBlockAnchor(
                                    doc,
                                    sheet,
                                    vp,
                                    options.OffsetFromTitleBlockRightFeet,
                                    options.OffsetFromTitleBlockTopFeet);

                                if (moved) result.MovedViewports++;
                            }

                            tx.Commit();
                        }
                    }
                    catch
                    {
                        result.Errors++;
                    }
                }

                tg.Assimilate();
            }

            return result;
        }
    }
}
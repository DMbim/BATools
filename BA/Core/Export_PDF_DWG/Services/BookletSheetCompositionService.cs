using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace BA.Core.Export.Services
{
    /// <summary>
    /// Creates the sheet, places the viewports, and populates the title
    /// block's own instance parameters directly, real title block fields
    /// instead of a drawn TextNote table. Must be called inside an open
    /// transaction, this modifies the document.
    ///
    /// Sheet coordinates are approximate, positioned for a generically
    /// sized sheet, not measured against any specific title block. If a
    /// specific office title block places its border differently, these
    /// positions may need adjusting, this has not been verified against
    /// a real title block yet.
    /// </summary>
    public static class BookletSheetCompositionService
    {
        /// <summary>
        /// Real Views branch: three viewports, floor plan, section, and
        /// isometric.
        /// </summary>
        public static (bool Success, string SheetNumber, string ErrorMessage) ComposeSheet(
            Document doc,
            ElementId titleBlockTypeId,
            string sheetNumber,
            string sheetName,
            ViewPlan floorPlanView,
            ViewSection sectionView,
            View3D isometricView,
            Dictionary<string, string> fieldValues,
            string itemMarkParameterName,
            string itemMarkValue)
        {
            ViewSheet sheet;

            try
            {
                sheet = ViewSheet.Create(doc, titleBlockTypeId);
                sheet.SheetNumber = sheetNumber;
                sheet.Name = sheetName;
            }
            catch (System.Exception ex)
            {
                return (false, string.Empty, $"Failed to create sheet: {ex.Message}");
            }

            var floorPlanPoint = new XYZ(0.6, 2.0, 0);
            var isometricPoint = new XYZ(2.0, 2.0, 0);
            var sectionPoint = new XYZ(1.3, 0.8, 0);

            if (!PlaceViewport(doc, sheet.Id, floorPlanView.Id, floorPlanPoint, out var floorPlanError))
            {
                return (false, sheet.SheetNumber, floorPlanError);
            }

            if (!PlaceViewport(doc, sheet.Id, isometricView.Id, isometricPoint, out var isometricError))
            {
                return (false, sheet.SheetNumber, isometricError);
            }

            if (!PlaceViewport(doc, sheet.Id, sectionView.Id, sectionPoint, out var sectionError))
            {
                return (false, sheet.SheetNumber, sectionError);
            }

            ApplyTitleBlockFields(doc, sheet.Id, fieldValues, itemMarkParameterName, itemMarkValue);

            return (true, sheet.SheetNumber, string.Empty);
        }

        /// <summary>
        /// Legend Components branch: a single viewport, a Legend Component
        /// has no equivalent to floor plan/section/isometric distinctions,
        /// it shows whatever directions were seeded in the source view.
        /// </summary>
        public static (bool Success, string SheetNumber, string ErrorMessage) ComposeLegendSheet(
            Document doc,
            ElementId titleBlockTypeId,
            string sheetNumber,
            string sheetName,
            View legendView,
            Dictionary<string, string> fieldValues,
            string itemMarkParameterName,
            string itemMarkValue)
        {
            ViewSheet sheet;

            try
            {
                sheet = ViewSheet.Create(doc, titleBlockTypeId);
                sheet.SheetNumber = sheetNumber;
                sheet.Name = sheetName;
            }
            catch (System.Exception ex)
            {
                return (false, string.Empty, $"Failed to create sheet: {ex.Message}");
            }

            var legendPoint = new XYZ(1.3, 1.5, 0);

            if (!PlaceViewport(doc, sheet.Id, legendView.Id, legendPoint, out var legendError))
            {
                return (false, sheet.SheetNumber, legendError);
            }

            ApplyTitleBlockFields(doc, sheet.Id, fieldValues, itemMarkParameterName, itemMarkValue);

            return (true, sheet.SheetNumber, string.Empty);
        }

        private static bool PlaceViewport(Document doc, ElementId sheetId, ElementId viewId, XYZ point, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (!Viewport.CanAddViewToSheet(doc, sheetId, viewId))
            {
                errorMessage = "This view could not be placed on the sheet (it may already be placed elsewhere, or is not a placeable view type).";
                return false;
            }

            try
            {
                Viewport.Create(doc, sheetId, viewId, point);
                return true;
            }
            catch (System.Exception ex)
            {
                errorMessage = $"Failed to place viewport: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Finds the title block instance Revit auto-places on a new
        /// sheet and sets its named instance parameters directly. A
        /// missing title block instance, or a missing/read-only named
        /// parameter, is silently skipped per field rather than failing
        /// the whole sheet, since these are cosmetic, not structural.
        /// </summary>
        private static void ApplyTitleBlockFields(
            Document doc,
            ElementId sheetId,
            Dictionary<string, string> fieldValues,
            string itemMarkParameterName,
            string itemMarkValue)
        {
            var titleBlockInstance = new FilteredElementCollector(doc, sheetId)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .FirstOrDefault();

            if (titleBlockInstance == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(itemMarkParameterName) && !string.IsNullOrWhiteSpace(itemMarkValue))
            {
                TrySetParameter(titleBlockInstance, itemMarkParameterName, itemMarkValue);
            }

            if (fieldValues == null)
            {
                return;
            }

            foreach (var field in fieldValues)
            {
                TrySetParameter(titleBlockInstance, field.Key, field.Value);
            }
        }

        private static void TrySetParameter(Element element, string parameterName, string value)
        {
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                return;
            }

            try
            {
                var parameter = element.LookupParameter(parameterName);

                if (parameter == null || parameter.IsReadOnly)
                {
                    return;
                }

                switch (parameter.StorageType)
                {
                    case StorageType.String:
                        parameter.Set(value ?? string.Empty);
                        break;
                    case StorageType.Double:
                        if (double.TryParse(value, out var doubleValue))
                        {
                            parameter.Set(doubleValue);
                        }
                        break;
                    case StorageType.Integer:
                        if (int.TryParse(value, out var intValue))
                        {
                            parameter.Set(intValue);
                        }
                        break;
                }
            }
            catch
            {
                // A single field failing to set (wrong type, locked
                // parameter, and so on) should not fail the whole sheet.
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BA.BAApplication;
using BA.Core.Export.Models;

namespace BA.Core.Export.Services
{
    /// <summary>
    /// Runs booklet generation for every selected type, branching per
    /// BookletSettings.Mode. RealViews needs a placed instance to cut a
    /// floor plan, section, and isometric view through. LegendComponents
    /// needs no placed instance at all, it references the type
    /// symbolically through the duplicated Legend Component. One type
    /// failing does not stop the rest, each type's sheet and its
    /// views/components are created inside their own Transaction, so a
    /// failure partway through one type rolls back only that type's
    /// changes. Must be called from a valid Revit API thread context.
    /// </summary>
    public static class BookletRunner
    {
        public static List<BookletOutcome> Run(Document doc, BookletSettings settings)
        {
            var outcomes = new List<BookletOutcome>();
            var sheetCounter = 1;

            foreach (var typeUniqueId in settings.SelectedTypeUniqueIds)
            {
                outcomes.Add(GenerateOneBooklet(doc, typeUniqueId, settings, ref sheetCounter));
            }

            return outcomes;
        }

        private static BookletOutcome GenerateOneBooklet(Document doc, string typeUniqueId, BookletSettings settings, ref int sheetCounter)
        {
            var element = doc.GetElement(typeUniqueId);

            if (!(element is ElementType elementType))
            {
                return new BookletOutcome
                {
                    TypeName = typeUniqueId,
                    Skipped = true,
                    SkippedReason = "Type could not be resolved, it may have been deleted since the picker was opened."
                };
            }

            var outcome = new BookletOutcome { TypeName = elementType.Name ?? string.Empty };

            var titleBlockTypeId = ResolveTitleBlockTypeId(doc, settings.TitleBlockUniqueId);

            if (titleBlockTypeId == ElementId.InvalidElementId)
            {
                outcome.Success = false;
                outcome.ErrorMessage = "No title block type could be resolved (none configured and no title blocks exist in the document).";
                return outcome;
            }

            return settings.Mode == BookletGenerationMode.LegendComponents
                ? GenerateLegendBooklet(doc, elementType, titleBlockTypeId, settings, outcome, ref sheetCounter)
                : GenerateRealViewBooklet(doc, elementType, titleBlockTypeId, settings, outcome, ref sheetCounter);
        }

        private static BookletOutcome GenerateRealViewBooklet(
            Document doc,
            ElementType elementType,
            ElementId titleBlockTypeId,
            BookletSettings settings,
            BookletOutcome outcome,
            ref int sheetCounter)
        {
            var instance = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.GetTypeId() == elementType.Id)
                .OfType<FamilyInstance>()
                .FirstOrDefault();

            if (instance == null)
            {
                outcome.Skipped = true;
                outcome.SkippedReason = "No placed instance of this type exists in the model, a floor plan, section, and isometric view need a real instance to base the views on.";
                return outcome;
            }

            using (var transaction = new Transaction(doc, $"BA Tools - Generate Booklet: {outcome.TypeName}"))
            {
                transaction.Start();

                try
                {
                    var (floorPlanView, sectionView, isometricView, viewError) = BookletViewGenerationService.CreateViews(
                        doc, instance, settings.CropMarginMm, settings.ViewScale, settings.DetailLevel);

                    if (floorPlanView == null || sectionView == null || isometricView == null)
                    {
                        transaction.RollBack();
                        outcome.Success = false;
                        outcome.ErrorMessage = viewError;
                        return outcome;
                    }

                    var fieldValues = ResolveFieldMappingValues(elementType, settings.TitleBlockFieldMappings);
                    var itemMarkValue = BuildItemMarkValue(settings, sheetCounter);
                    var sheetNumber = $"{settings.OutputSheetNumberPrefix}{sheetCounter:000}";

                    var (success, actualSheetNumber, sheetError) = BookletSheetCompositionService.ComposeSheet(
                        doc, titleBlockTypeId, sheetNumber, outcome.TypeName,
                        floorPlanView, sectionView, isometricView,
                        fieldValues, settings.ItemMarkTitleBlockParameterName, itemMarkValue);

                    if (!success)
                    {
                        transaction.RollBack();
                        outcome.Success = false;
                        outcome.ErrorMessage = sheetError;
                        return outcome;
                    }

                    transaction.Commit();
                    outcome.Success = true;
                    outcome.SheetNumber = actualSheetNumber;
                    sheetCounter++;
                }
                catch (Exception ex)
                {
                    if (transaction.GetStatus() == TransactionStatus.Started)
                    {
                        transaction.RollBack();
                    }

                    outcome.Success = false;
                    outcome.ErrorMessage = ex.Message;
                    AppLogger.LogError($"Booklet generation failed for type '{outcome.TypeName}'", ex);
                }
            }

            return outcome;
        }

        private static BookletOutcome GenerateLegendBooklet(
            Document doc,
            ElementType elementType,
            ElementId titleBlockTypeId,
            BookletSettings settings,
            BookletOutcome outcome,
            ref int sheetCounter)
        {
            if (string.IsNullOrWhiteSpace(settings.SeedLegendViewUniqueId))
            {
                outcome.Success = false;
                outcome.ErrorMessage = "No seed Legend view is configured. Pick the existing Legend view containing your placeholder component(s) in the settings panel.";
                return outcome;
            }

            using (var transaction = new Transaction(doc, $"BA Tools - Generate Legend Booklet: {outcome.TypeName}"))
            {
                transaction.Start();

                try
                {
                    var (legendView, viewError) = LegendBookletService.CreateLegendView(
                        doc, settings.SeedLegendViewUniqueId, elementType.Id);

                    if (legendView == null)
                    {
                        transaction.RollBack();
                        outcome.Success = false;
                        outcome.ErrorMessage = viewError;
                        return outcome;
                    }

                    var fieldValues = ResolveFieldMappingValues(elementType, settings.TitleBlockFieldMappings);
                    var itemMarkValue = BuildItemMarkValue(settings, sheetCounter);
                    var sheetNumber = $"{settings.OutputSheetNumberPrefix}{sheetCounter:000}";

                    var (success, actualSheetNumber, sheetError) = BookletSheetCompositionService.ComposeLegendSheet(
                        doc, titleBlockTypeId, sheetNumber, outcome.TypeName, legendView,
                        fieldValues, settings.ItemMarkTitleBlockParameterName, itemMarkValue);

                    if (!success)
                    {
                        transaction.RollBack();
                        outcome.Success = false;
                        outcome.ErrorMessage = sheetError;
                        return outcome;
                    }

                    transaction.Commit();
                    outcome.Success = true;
                    outcome.SheetNumber = actualSheetNumber;
                    sheetCounter++;
                }
                catch (Exception ex)
                {
                    if (transaction.GetStatus() == TransactionStatus.Started)
                    {
                        transaction.RollBack();
                    }

                    outcome.Success = false;
                    outcome.ErrorMessage = ex.Message;
                    AppLogger.LogError($"Legend booklet generation failed for type '{outcome.TypeName}'", ex);
                }
            }

            return outcome;
        }

        /// <summary>
        /// "Z 07" style, prefix plus a two digit running number, matching
        /// the reference shop drawing layout this was scoped against.
        /// Uses the same counter as the sheet number, so item mark and
        /// sheet number stay in step with each other.
        /// </summary>
        private static string BuildItemMarkValue(BookletSettings settings, int sheetCounter)
        {
            if (string.IsNullOrWhiteSpace(settings.ItemMarkTitleBlockParameterName))
            {
                return string.Empty;
            }

            return $"{settings.ItemMarkPrefix}{sheetCounter:00}";
        }

        private static ElementId ResolveTitleBlockTypeId(Document doc, string titleBlockUniqueId)
        {
            if (!string.IsNullOrWhiteSpace(titleBlockUniqueId))
            {
                var element = doc.GetElement(titleBlockUniqueId);

                if (element is FamilySymbol)
                {
                    return element.Id;
                }
            }

            var fallback = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .FirstOrDefault();

            return fallback?.Id ?? ElementId.InvalidElementId;
        }

        private static Dictionary<string, string> ResolveFieldMappingValues(
            ElementType elementType,
            List<BookletTitleBlockFieldMapping> mappings)
        {
            var result = new Dictionary<string, string>();

            if (mappings == null)
            {
                return result;
            }

            foreach (var mapping in mappings)
            {
                if (string.IsNullOrWhiteSpace(mapping.TitleBlockParameterName) || mapping.SourceField == null)
                {
                    continue;
                }

                var field = mapping.SourceField;
                Parameter parameter = null;

                switch (field.Source)
                {
                    case ParameterColumnSource.BuiltIn:
                        if (field.BuiltInParameterId.HasValue)
                        {
                            parameter = elementType.get_Parameter(field.BuiltInParameterId.Value);
                        }
                        break;

                    case ParameterColumnSource.Shared:
                        if (field.SharedParamGuid.HasValue)
                        {
                            parameter = elementType.get_Parameter(field.SharedParamGuid.Value);
                        }
                        break;

                    case ParameterColumnSource.Project:
                        parameter = elementType.LookupParameter(field.ProjectParameterName);
                        break;
                }

                var value = parameter != null && parameter.HasValue ? FormatValue(parameter) : string.Empty;
                result[mapping.TitleBlockParameterName] = value;
            }

            return result;
        }

        private static string FormatValue(Parameter parameter)
        {
            switch (parameter.StorageType)
            {
                case StorageType.String:
                    return parameter.AsString() ?? string.Empty;
                case StorageType.Integer:
                    return parameter.AsValueString() ?? parameter.AsInteger().ToString();
                case StorageType.Double:
                    return parameter.AsValueString() ?? parameter.AsDouble().ToString("0.##");
                case StorageType.ElementId:
                    return parameter.AsValueString() ?? string.Empty;
                default:
                    return string.Empty;
            }
        }
    }
}

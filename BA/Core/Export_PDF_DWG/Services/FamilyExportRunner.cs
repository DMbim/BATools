using Autodesk.Revit.DB;
using BA.Core.Export.Models;

namespace BA.Core.Export.Services
{
    /// <summary>
    /// Loops the selected families and aggregates a FamilyExportResult.
    /// One family failing does not stop the rest. Must be called from a
    /// valid Revit API thread context.
    /// </summary>
    public static class FamilyExportRunner
    {
        public static FamilyExportResult Run(Document doc, FamilyExportSettings settings)
        {
            var result = new FamilyExportResult();

            foreach (var uniqueId in settings.SelectedFamilyUniqueIds)
            {
                result.Outcomes.Add(FamilyExportService.ExportFamily(doc, uniqueId, settings));
            }

            return result;
        }
    }
}

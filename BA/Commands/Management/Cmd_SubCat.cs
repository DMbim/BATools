using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BA.Commands.Families
{
    [Transaction(TransactionMode.Manual)]
    public class Cmd_ReportFamilySubcategories : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document projectDoc = uiDoc.Document;

            try
            {
                if (projectDoc.IsFamilyDocument)
                {
                    TaskDialog.Show("Family Subcategories",
                        "Run this command in a project document, not inside a family document.");
                    return Result.Cancelled;
                }

                FamilySubcategoryScanOptions options = new FamilySubcategoryScanOptions
                {
                    Prefix = "BA_",
                    OnlyRowsWithoutPrefix = false,
                    IncludeRootFamilyCategoryRow = false
                };

                FamilySubcategoryScanResult result =
                    FamilySubcategoryScanner.ScanProjectFamilies(projectDoc, options);

                string reportPath = FamilySubcategoryReportWriter.WriteCsvReport(
                    result,
                    projectDoc,
                    "FamilySubcategoryReport");

                string summary = BuildSummary(result, reportPath);

                TaskDialog.Show("Family Subcategories", summary);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                return Result.Failed;
            }
        }

        private static string BuildSummary(FamilySubcategoryScanResult result, string reportPath)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("Family subcategory scan finished.");
            sb.AppendLine();
            sb.AppendLine($"Families scanned: {result.FamiliesScanned}");
            sb.AppendLine($"Families skipped: {result.FamiliesSkipped}");
            sb.AppendLine($"Rows collected: {result.Rows.Count}");
            sb.AppendLine($"Rows without BA_ prefix: {result.Rows.Count(x => !x.StartsWithPrefix)}");
            sb.AppendLine();

            if (result.Errors.Count > 0)
            {
                sb.AppendLine("Errors:");
                foreach (string err in result.Errors.Take(10))
                    sb.AppendLine(" - " + err);

                if (result.Errors.Count > 10)
                    sb.AppendLine($" - ... and {result.Errors.Count - 10} more");
                sb.AppendLine();
            }

            sb.AppendLine("CSV report:");
            sb.AppendLine(reportPath);

            return sb.ToString();
        }
    }

    public sealed class FamilySubcategoryScanOptions
    {
        public string Prefix { get; set; } = "BA_";
        public bool OnlyRowsWithoutPrefix { get; set; }
        public bool IncludeRootFamilyCategoryRow { get; set; }
    }

    public sealed class FamilySubcategoryRow
    {
        public string ProjectTitle { get; set; } = string.Empty;

        public string FamilyName { get; set; } = string.Empty;
        public int FamilyId { get; set; }

        public string FamilyCategoryName { get; set; } = string.Empty;
        public int FamilyCategoryId { get; set; }

        public string ParentCategoryName { get; set; } = string.Empty;
        public int ParentCategoryId { get; set; }

        public string SubcategoryName { get; set; } = string.Empty;
        public int SubcategoryId { get; set; }

        public bool StartsWithPrefix { get; set; }
        public bool IsRootCategoryRow { get; set; }
        public bool IsEditableFamily { get; set; }
        public string SourceFamilyPathOrInfo { get; set; } = string.Empty;
    }

    public sealed class FamilySubcategoryScanResult
    {
        public List<FamilySubcategoryRow> Rows { get; } = new List<FamilySubcategoryRow>();
        public List<string> Errors { get; } = new List<string>();

        public int FamiliesScanned { get; set; }
        public int FamiliesSkipped { get; set; }
    }

    public static class FamilySubcategoryScanner
    {
        public static FamilySubcategoryScanResult ScanProjectFamilies(
            Document projectDoc,
            FamilySubcategoryScanOptions options)
        {
            if (projectDoc == null) throw new ArgumentNullException(nameof(projectDoc));
            if (options == null) throw new ArgumentNullException(nameof(options));

            FamilySubcategoryScanResult result = new FamilySubcategoryScanResult();

            List<Family> families = new FilteredElementCollector(projectDoc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .OrderBy(x => SafeElementName(x))
                .ToList();

            foreach (Family family in families)
            {
                if (family == null)
                {
                    result.FamiliesSkipped++;
                    continue;
                }

                try
                {
                    if (!IsSupportedEditableFamily(projectDoc, family))
                    {
                        result.FamiliesSkipped++;
                        continue;
                    }

                    using (Document familyDoc = projectDoc.EditFamily(family))
                    {
                        CollectRowsFromFamilyDocument(projectDoc, family, familyDoc, options, result);
                        result.FamiliesScanned++;
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add(
                        $"Family '{SafeElementName(family)}' (Id {ToInt(family.Id)}) failed: {ex.Message}");
                    result.FamiliesSkipped++;
                }
            }

            return result;
        }

        private static void CollectRowsFromFamilyDocument(
            Document projectDoc,
            Family family,
            Document familyDoc,
            FamilySubcategoryScanOptions options,
            FamilySubcategoryScanResult result)
        {
            if (familyDoc == null || !familyDoc.IsFamilyDocument)
                return;

            Family ownerFamily = familyDoc.OwnerFamily;
            if (ownerFamily == null)
                return;

            Category familyCategory = ownerFamily.FamilyCategory;
            if (familyCategory == null)
                return;

            string familyName = SafeElementName(family);
            string familyCategoryName = SafeCategoryName(familyCategory);

            if (options.IncludeRootFamilyCategoryRow)
            {
                FamilySubcategoryRow rootRow = new FamilySubcategoryRow
                {
                    ProjectTitle = SafeProjectTitle(projectDoc),
                    FamilyName = familyName,
                    FamilyId = ToInt(family.Id),
                    FamilyCategoryName = familyCategoryName,
                    FamilyCategoryId = ToInt(familyCategory.Id),
                    ParentCategoryName = familyCategoryName,
                    ParentCategoryId = ToInt(familyCategory.Id),
                    SubcategoryName = familyCategoryName,
                    SubcategoryId = ToInt(familyCategory.Id),
                    StartsWithPrefix = StartsWithPrefix(familyCategoryName, options.Prefix),
                    IsRootCategoryRow = true,
                    IsEditableFamily = true,
                    SourceFamilyPathOrInfo = SafeFamilySourceInfo(family)
                };

                if (!options.OnlyRowsWithoutPrefix || !rootRow.StartsWithPrefix)
                    result.Rows.Add(rootRow);
            }

            CategoryNameMap subcats = familyCategory.SubCategories;
            if (subcats == null || subcats.IsEmpty)
                return;

            foreach (Category subcat in subcats)
            {
                if (subcat == null)
                    continue;

                FamilySubcategoryRow row = new FamilySubcategoryRow
                {
                    ProjectTitle = SafeProjectTitle(projectDoc),
                    FamilyName = familyName,
                    FamilyId = ToInt(family.Id),
                    FamilyCategoryName = familyCategoryName,
                    FamilyCategoryId = ToInt(familyCategory.Id),
                    ParentCategoryName = familyCategoryName,
                    ParentCategoryId = ToInt(familyCategory.Id),
                    SubcategoryName = SafeCategoryName(subcat),
                    SubcategoryId = ToInt(subcat.Id),
                    StartsWithPrefix = StartsWithPrefix(SafeCategoryName(subcat), options.Prefix),
                    IsRootCategoryRow = false,
                    IsEditableFamily = true,
                    SourceFamilyPathOrInfo = SafeFamilySourceInfo(family)
                };

                if (!options.OnlyRowsWithoutPrefix || !row.StartsWithPrefix)
                    result.Rows.Add(row);
            }
        }

        public static bool IsSupportedEditableFamily(Document projectDoc, Family family)
        {
            if (projectDoc == null) throw new ArgumentNullException(nameof(projectDoc));
            if (family == null) return false;

            try
            {
                if (!family.IsEditable)
                    return false;
            }
            catch
            {
                return false;
            }

            return true;
        }

        public static bool StartsWithPrefix(string name, string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix))
                return true;

            return (name ?? string.Empty)
                .StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        public static string SafeElementName(Element e)
        {
            if (e == null) return string.Empty;

            try
            {
                return e.Name ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public static string SafeCategoryName(Category c)
        {
            if (c == null) return string.Empty;

            try
            {
                return c.Name ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public static string SafeProjectTitle(Document doc)
        {
            if (doc == null) return string.Empty;

            try
            {
                return doc.Title ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public static string SafeFamilySourceInfo(Family family)
        {
            if (family == null) return string.Empty;

            try
            {
                return family.IsInPlace ? "InPlace" : "Loadable";
            }
            catch
            {
                return string.Empty;
            }
        }

        public static int ToInt(ElementId id)
        {
            if (id == null) return -1;

            try
            {
                return unchecked((int)id.Value);
            }
            catch
            {
                return -1;
            }
        }
    }

    public static class FamilySubcategoryReportWriter
    {
        public static string WriteCsvReport(
            FamilySubcategoryScanResult result,
            Document projectDoc,
            string baseFileName)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (projectDoc == null) throw new ArgumentNullException(nameof(projectDoc));

            string folder = GetReportFolder(projectDoc);
            EnsureFolderExists(folder);

            string safeProject = MakeSafeFileName(projectDoc.Title);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"{baseFileName}_{safeProject}_{timestamp}.csv";
            string fullPath = Path.Combine(folder, fileName);

            StringBuilder sb = new StringBuilder();

            sb.AppendLine(string.Join(",",
                Csv("ProjectTitle"),
                Csv("FamilyName"),
                Csv("FamilyId"),
                Csv("FamilyCategoryName"),
                Csv("FamilyCategoryId"),
                Csv("ParentCategoryName"),
                Csv("ParentCategoryId"),
                Csv("SubcategoryName"),
                Csv("SubcategoryId"),
                Csv("StartsWithPrefix"),
                Csv("IsRootCategoryRow"),
                Csv("IsEditableFamily"),
                Csv("SourceFamilyPathOrInfo")));

            foreach (FamilySubcategoryRow row in result.Rows
                         .OrderBy(x => x.FamilyName)
                         .ThenBy(x => x.SubcategoryName))
            {
                sb.AppendLine(string.Join(",",
                    Csv(row.ProjectTitle),
                    Csv(row.FamilyName),
                    Csv(row.FamilyId.ToString()),
                    Csv(row.FamilyCategoryName),
                    Csv(row.FamilyCategoryId.ToString()),
                    Csv(row.ParentCategoryName),
                    Csv(row.ParentCategoryId.ToString()),
                    Csv(row.SubcategoryName),
                    Csv(row.SubcategoryId.ToString()),
                    Csv(row.StartsWithPrefix.ToString()),
                    Csv(row.IsRootCategoryRow.ToString()),
                    Csv(row.IsEditableFamily.ToString()),
                    Csv(row.SourceFamilyPathOrInfo)));
            }

            File.WriteAllText(fullPath, sb.ToString(), Encoding.UTF8);

            return fullPath;
        }

        private static string GetReportFolder(Document projectDoc)
        {
            string docPath = string.Empty;

            try
            {
                docPath = projectDoc.PathName ?? string.Empty;
            }
            catch
            {
            }

            if (!string.IsNullOrWhiteSpace(docPath))
            {
                string dir = Path.GetDirectoryName(docPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    return Path.Combine(dir, "_BA_Reports");
            }

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            return Path.Combine(desktop, "_BA_Reports");
        }

        private static void EnsureFolderExists(string folder)
        {
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);
        }

        private static string Csv(string value)
        {
            string s = value ?? string.Empty;
            s = s.Replace("\"", "\"\"");
            return "\"" + s + "\"";
        }

        private static string MakeSafeFileName(string value)
        {
            string s = string.IsNullOrWhiteSpace(value) ? "Untitled" : value;

            foreach (char c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');

            return s;
        }
    }
}
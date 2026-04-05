using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.Core.Standards
{
    public sealed class SubcategoryAuditService
    {
        public IList<SubcategoryAuditRow> AuditProjectFamilies(
            Document projectDoc,
            SubcategoryAuditOptions options)
        {
            if (projectDoc == null)
                throw new ArgumentNullException(nameof(projectDoc));

            options ??= new SubcategoryAuditOptions();

            if (projectDoc.IsModifiable)
                throw new InvalidOperationException(
                    "The project document is currently modifiable. Close any open transaction before running the family auditor.");

            if (projectDoc.IsReadOnly)
                throw new InvalidOperationException(
                    "The project document is read-only. The auditor cannot open family edit documents from a read-only project state.");

            List<SubcategoryAuditRow> rows = new List<SubcategoryAuditRow>();

            List<Family> families = new FilteredElementCollector(projectDoc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .OrderBy(f => GetFamilyCategoryNameSafe(f))
                .ThenBy(f => f.Name)
                .ToList();

            foreach (Family family in families)
            {
                SubcategoryAuditRow row = AuditSingleFamily(projectDoc, family, options);
                rows.Add(row);
            }

            return rows;
        }

        private SubcategoryAuditRow AuditSingleFamily(
            Document projectDoc,
            Family family,
            SubcategoryAuditOptions options)
        {
            SubcategoryAuditRow row = new SubcategoryAuditRow
            {
                FamilyId = family?.Id?.Value ?? -1,
                FamilyName = family?.Name ?? "<null>",
                CategoryName = GetFamilyCategoryNameSafe(family),
                ExistingSubcategories = "",
                ValidBaNames = "",
                MissingRequired = "",
                AllowedNonBaNames = "",
                NonCompliantNames = "",
                Notes = ""
            };

            if (family == null)
            {
                row.Status = AuditRowStatus.Error;
                row.Notes = "Family reference was null.";
                return row;
            }

            if (family.IsInPlace)
            {
                row.Status = AuditRowStatus.Skipped;
                row.Notes = "Skipped: in-place family.";
                return row;
            }

            if (!family.IsEditable)
            {
                row.Status = AuditRowStatus.Skipped;
                row.Notes = "Skipped: family is not editable.";
                return row;
            }

            Document familyDoc = null;

            try
            {
                familyDoc = projectDoc.EditFamily(family);

                if (familyDoc == null)
                {
                    row.Status = AuditRowStatus.Error;
                    row.Notes = "EditFamily returned null.";
                    return row;
                }

                Family ownerFamily = familyDoc.OwnerFamily;
                if (ownerFamily == null)
                {
                    row.Status = AuditRowStatus.Error;
                    row.Notes = "Family document has no OwnerFamily.";
                    return row;
                }

                Category familyCategory = ownerFamily.FamilyCategory;
                row.CategoryName = familyCategory?.Name ?? "<No Category>";

                List<string> subcategoryNames = GetDirectSubcategoryNames(familyCategory);
                List<string> validBa = BaSubcategoryRules.GetValidBaNamesFound(subcategoryNames);
                List<string> required = BaSubcategoryRules.GetRequiredSubcategories(familyCategory).ToList();
                List<string> missing = BaSubcategoryRules.GetMissingRequired(subcategoryNames, required);
                List<string> allowedNonBa = BaSubcategoryRules.GetAllowedNonBaNamesFound(
                    familyCategory,
                    subcategoryNames,
                    options.StrictMode);
                List<string> nonCompliant = BaSubcategoryRules.GetNonCompliantCustomNames(
                    familyCategory,
                    subcategoryNames,
                    options.StrictMode);

                row.ExistingSubcategories = JoinList(subcategoryNames);
                row.ValidBaNames = JoinList(validBa);
                row.MissingRequired = JoinList(missing);
                row.AllowedNonBaNames = JoinList(allowedNonBa);
                row.NonCompliantNames = JoinList(nonCompliant);

                if (subcategoryNames.Count == 0)
                {
                    row.Status = AuditRowStatus.Warning;
                    row.Notes = "No custom subcategories found under the family category.";
                    return row;
                }

                bool noBaNamesIssue = options.WarnIfNoBaNames && validBa.Count == 0;
                bool hasWarnings =
                    missing.Count > 0 ||
                    nonCompliant.Count > 0 ||
                    noBaNamesIssue;

                if (!hasWarnings)
                {
                    row.Status = AuditRowStatus.Clean;

                    List<string> okNotes = new List<string>();
                    okNotes.Add("OK");

                    if (allowedNonBa.Count > 0)
                    {
                        if (options.StrictMode)
                            okNotes.Add("Contains always-allowed built-in non-BA subcategories.");
                        else
                            okNotes.Add("Contains allowed non-BA subcategories.");
                    }

                    row.Notes = string.Join(" ", okNotes);
                    return row;
                }

                row.Status = AuditRowStatus.Warning;

                List<string> notes = new List<string>();

                if (missing.Count > 0)
                    notes.Add("Missing required BA semantic roles.");

                if (nonCompliant.Count > 0)
                    notes.Add("Contains suspicious non-BA custom subcategory names.");

                if (noBaNamesIssue)
                    notes.Add("Contains no BA_* subcategories.");

                if (allowedNonBa.Count > 0)
                {
                    if (options.StrictMode)
                        notes.Add("Also contains always-allowed built-in non-BA names.");
                    else
                        notes.Add("Also contains allowed non-BA names.");
                }

                row.Notes = string.Join(" ", notes);
                return row;
            }
            catch (Exception ex)
            {
                row.Status = AuditRowStatus.Error;
                row.Notes = ex.Message;
                return row;
            }
            finally
            {
                if (familyDoc != null && familyDoc.IsValidObject)
                {
                    try
                    {
                        familyDoc.Close(false);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static string GetFamilyCategoryNameSafe(Family family)
        {
            try
            {
                return family?.FamilyCategory?.Name ?? "<No Category>";
            }
            catch
            {
                return "<Unknown Category>";
            }
        }

        private static List<string> GetDirectSubcategoryNames(Category familyCategory)
        {
            List<string> names = new List<string>();

            if (familyCategory == null)
                return names;

            CategoryNameMap map = familyCategory.SubCategories;
            if (map == null)
                return names;

            foreach (Category subCat in map)
            {
                if (subCat == null)
                    continue;

                string name = subCat.Name;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                names.Add(name.Trim());
            }

            return names
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string JoinList(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0)
                return "";

            return string.Join(", ", values);
        }

        public SubcategoryAuditSummary BuildSummary(IEnumerable<SubcategoryAuditRow> rows)
        {
            List<SubcategoryAuditRow> list = rows?.ToList() ?? new List<SubcategoryAuditRow>();

            return new SubcategoryAuditSummary
            {
                TotalRows = list.Count,
                CleanCount = list.Count(x => x.Status == AuditRowStatus.Clean),
                WarningCount = list.Count(x => x.Status == AuditRowStatus.Warning),
                ErrorCount = list.Count(x => x.Status == AuditRowStatus.Error),
                SkippedCount = list.Count(x => x.Status == AuditRowStatus.Skipped),
                MissingRequiredCount = list.Count(x => !string.IsNullOrWhiteSpace(x.MissingRequired)),
                NonCompliantNameCount = list.Count(x => !string.IsNullOrWhiteSpace(x.NonCompliantNames)),
                AllowedNonBaCount = list.Count(x => !string.IsNullOrWhiteSpace(x.AllowedNonBaNames)),
                ValidBaCount = list.Count(x => !string.IsNullOrWhiteSpace(x.ValidBaNames)),
                NoBaNamesCount = list.Count(x => string.IsNullOrWhiteSpace(x.ValidBaNames))
            };
        }
    }
}
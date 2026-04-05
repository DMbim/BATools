using Autodesk.Revit.DB;

namespace BA.Keyplan
{
    public sealed class KeyplanSheetUpdateOptions
    {
        public string SharedParameterFilePath { get; set; }

        public string SheetZoneParameterName { get; set; }

        public string SourceViewName { get; set; }
        public string BaseDraftingViewName { get; set; }
        public string DraftingTemplateName { get; set; }

        public string ActiveFilledRegionTypeName { get; set; }
        public string InactiveFilledRegionTypeName { get; set; }

        public string GeneratedViewPrefix { get; set; }

        public double OffsetFromTitleBlockRightFeet { get; set; }
        public double OffsetFromTitleBlockTopFeet { get; set; }

        public bool CreateSharedParameterIfMissing { get; set; }
        public bool RegenerateBaseViewFirst { get; set; }
        public bool DeleteOldKeyplanViewportOnSheet { get; set; }
        public bool ReuseExistingSheetSpecificView { get; set; }

        public static KeyplanSheetUpdateOptions CreateDefault()
        {
            return new KeyplanSheetUpdateOptions
            {
                SharedParameterFilePath = string.Empty,
                SheetZoneParameterName = "BA_KeyplanZone",

                SourceViewName = "X.NP_Keyplan",
                BaseDraftingViewName = "X.NP_Keyplan_Drafting_BASE",
                DraftingTemplateName = "X.NP_Keyplan_TEMPLATE",

                ActiveFilledRegionTypeName = "BA_Keyplan_Active",
                InactiveFilledRegionTypeName = "BA_Keyplan_Inactive",

                GeneratedViewPrefix = "X.NP_Keyplan_",

                // sheet coordinates are internal Revit feet
                // example values: ~20 mm from right/top edge of titleblock inner frame
                OffsetFromTitleBlockRightFeet = UnitUtils.ConvertToInternalUnits(20.0, UnitTypeId.Millimeters),
                OffsetFromTitleBlockTopFeet = UnitUtils.ConvertToInternalUnits(20.0, UnitTypeId.Millimeters),

                CreateSharedParameterIfMissing = true,
                RegenerateBaseViewFirst = true,
                DeleteOldKeyplanViewportOnSheet = true,
                ReuseExistingSheetSpecificView = true
            };
        }
    }

    public sealed class KeyplanSheetUpdateResult
    {
        public int TotalSheets { get; set; }
        public int SheetsWithZone { get; set; }
        public int CreatedViews { get; set; }
        public int UpdatedViews { get; set; }
        public int PlacedViewports { get; set; }
        public int MovedViewports { get; set; }
        public int SkippedSheets { get; set; }
        public int Errors { get; set; }
    }
}
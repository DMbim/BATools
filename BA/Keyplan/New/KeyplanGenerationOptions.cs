using Autodesk.Revit.DB;

namespace BA.Keyplan
{
    public sealed class KeyplanGenerationOptions
    {
        public string SourceViewName { get; set; }
        public string TargetDraftingViewName { get; set; }
        public string TargetDraftingViewTemplateName { get; set; }

        public bool DeleteExistingTargetContents { get; set; }
        public bool CopyViewSpecificElements { get; set; }
        public bool RecreateVisibleNonViewSpecificCurves { get; set; }
        public bool OpenTargetAfterGeneration { get; set; }

        public static KeyplanGenerationOptions CreateDefault()
        {
            return new KeyplanGenerationOptions
            {
                SourceViewName = "X.NP_Keyplan",
                TargetDraftingViewName = "X.NP_Keyplan_Drafting",
                TargetDraftingViewTemplateName = "X.NP_Keyplan_TEMPLATE",
                DeleteExistingTargetContents = true,
                CopyViewSpecificElements = true,
                RecreateVisibleNonViewSpecificCurves = true,
                OpenTargetAfterGeneration = true
            };
        }
    }

    public sealed class KeyplanGenerationResult
    {
        public ElementId SourceViewId { get; set; } = ElementId.InvalidElementId;
        public string SourceViewName { get; set; }

        public ElementId GeneratedViewId { get; set; } = ElementId.InvalidElementId;
        public string TargetViewName { get; set; }

        public int DeletedElementCount { get; set; }
        public int CopiedViewSpecificCount { get; set; }
        public int RecreatedCurveCount { get; set; }
        public int SkippedCurveCount { get; set; }

        public string AppliedTemplateName { get; set; }
    }
}
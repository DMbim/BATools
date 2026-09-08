using System.Collections.Generic;

namespace BA.Classification
{
    /// <summary>
    /// Stores classification summary statistics and examples for reporting.
    /// Compatible with ClassificationUiUtils.ShowReport().
    /// </summary>
    public class ClassificationReport
    {
        public int TotalTypes { get; set; }
        public int ConsideredTypes { get; set; }
        public int Classified { get; set; }

        public int SkippedNoCategory { get; set; }
        public int SkippedNoRulesForCategory { get; set; }
        public int SkippedMissingParameters { get; set; }
        public int SkippedAlreadyClassified { get; set; }
        public int SkippedReadOnlyOrTypeMismatch { get; set; }

        public int NoMatch { get; set; }

        // NEW: a type WAS classified (Domain/Group/Subcode/Code written from the winning rule),
        // but the winning rule's TargetLevelCode had no matching row in the BAClass catalog sheet,
        // so LabelEn/LabelCz were written blank. This used to happen silently - ClassificationEngine
        // called BaClassCatalog.TryGet and discarded the bool result, so a typo'd or renamed
        // TargetLevelCode in Rules_vNext quietly produced classified types with empty display labels
        // and nothing in the report or trace CSV said so. This counter and the example list make
        // that visible instead.
        public int ClassifiedMissingCatalogLabels { get; set; }

        /// <summary>
        /// Examples of types or instances that did not match any rule.
        /// </summary>
        public List<string> ExamplesNoMatch { get; } = new();

        /// <summary>
        /// Examples of elements missing classification parameters.
        /// </summary>
        public List<string> ExamplesMissingParams { get; } = new();

        // NEW: examples of types classified with a TargetLevelCode not found in the BAClass catalog.
        public List<string> ExamplesMissingCatalogLabels { get; } = new();

        public ClassificationReport()
        {
            TotalTypes = 0;
            ConsideredTypes = 0;
            Classified = 0;
            SkippedNoCategory = 0;
            SkippedNoRulesForCategory = 0;
            SkippedMissingParameters = 0;
            SkippedAlreadyClassified = 0;
            SkippedReadOnlyOrTypeMismatch = 0;
            NoMatch = 0;
            ClassifiedMissingCatalogLabels = 0; // <- NEW
        }

        public void AddExampleNoMatch(string example)
        {
            if (ExamplesNoMatch.Count < 10 && !string.IsNullOrWhiteSpace(example))
                ExamplesNoMatch.Add(example);
        }

        public void AddExampleMissingParams(string example)
        {
            if (ExamplesMissingParams.Count < 10 && !string.IsNullOrWhiteSpace(example))
                ExamplesMissingParams.Add(example);
        }

        // NEW
        public void AddExampleMissingCatalogLabels(string example)
        {
            if (ExamplesMissingCatalogLabels.Count < 10 && !string.IsNullOrWhiteSpace(example))
                ExamplesMissingCatalogLabels.Add(example);
        }
    }
}

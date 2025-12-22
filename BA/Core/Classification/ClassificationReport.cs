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

        /// <summary>
        /// Examples of types or instances that did not match any rule.
        /// </summary>
        public List<string> ExamplesNoMatch { get; } = new();

        /// <summary>
        /// Examples of elements missing classification parameters.
        /// </summary>
        public List<string> ExamplesMissingParams { get; } = new();

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
    }
}

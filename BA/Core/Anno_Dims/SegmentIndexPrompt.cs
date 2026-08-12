using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.UI;

namespace BA.UI
{
    /// <summary>
    /// Shared segment-index selection prompt used by dimension commands
    /// that can target specific DimensionSegment indices within a selection.
    /// </summary>
    public static class SegmentIndexPrompt
    {
        /// <summary>
        /// Asks user for segment indices using the pattern:
        /// - "0,1,2" (comma-separated)
        /// - "0..5..1" (start..end..step)
        /// Empty input, or dialog cancel, => "all segments".
        /// </summary>
        /// <returns>
        /// null  -> all segments
        /// list  -> explicit indices
        /// empty -> invalid input (caller should abort the command)
        /// </returns>
        public static List<int> AskSegmentIndices(int maxSegmentCount)
        {
            var prompt = new InputTextDialog(
                $"Dimension(s) may have up to {maxSegmentCount} segments.\n" +
                "Enter segment indices (e.g. '0, 1, 2'),.\n" +
                $"or range '0..{maxSegmentCount - 1}..1' (start..end..step).\n" +
                "Leave empty to apply to ALL segments.",
                "Select Segments");
            bool? res = prompt.ShowDialog();

            if (res != true)
            {
                // Cancel dialog => treat as "all segments"
                return null;
            }

            string input = prompt.InputText;
            if (string.IsNullOrWhiteSpace(input))
            {
                // Empty => "all segments"
                return null;
            }

            input = input.Trim();

            // Range syntax: "start..end..step"
            if (input.Contains(".."))
            {
                var parts = input.Split(new[] { ".." }, StringSplitOptions.None);
                if (parts.Length == 3 &&
                    int.TryParse(parts[0].Trim(), out int start) &&
                    int.TryParse(parts[1].Trim(), out int end) &&
                    int.TryParse(parts[2].Trim(), out int step) &&
                    start >= 0 &&
                    end >= 0 &&
                    step > 0)
                {
                    // Clamp end to maxSegmentCount-1 to avoid overshoot
                    end = Math.Min(end, maxSegmentCount - 1);

                    if (start > end)
                    {
                        TaskDialog.Show("BA", "Invalid range: start is greater than end.");
                        return new List<int>(); // signal invalid
                    }

                    var list = new List<int>();
                    for (int i = start; i <= end; i += step)
                    {
                        if (i >= 0 && i < maxSegmentCount)
                            list.Add(i);
                    }

                    if (list.Count == 0)
                    {
                        TaskDialog.Show("BA", "No valid indices found in the specified range.");
                        return new List<int>();
                    }

                    return list;
                }

                TaskDialog.Show("BA", "Invalid range format. Use 'start..end..step', e.g., '0..5..1'.");
                return new List<int>();
            }

            // Comma-separated indices
            var tokens = input.Split(',');
            var indices = new List<int>();

            foreach (var token in tokens.Select(t => t.Trim()))
            {
                if (!int.TryParse(token, out int idx))
                {
                    TaskDialog.Show("BA", $"Invalid index: '{token}'. Use integers like '0,1,2'.");
                    return new List<int>();
                }

                if (idx < 0 || idx >= maxSegmentCount)
                {
                    TaskDialog.Show("BA", $"Index {idx} is out of range. Valid range is 0 to {maxSegmentCount - 1}.");
                    return new List<int>();
                }

                indices.Add(idx);
            }

            if (indices.Count == 0)
            {
                TaskDialog.Show("BA", "No valid segment indices provided.");
                return new List<int>();
            }

            return indices;
        }
    }
}
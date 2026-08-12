using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Nice3point.Revit.Toolkit.External;
using BA.Filters;
using BA.UI;

namespace BA.Commands
{
    /// <summary>
    /// Overrides selected dimensions by appending a line of text
    /// below the current value (dimension or segment).
    /// User can specify which segments (by index) to affect.
    /// If dimension has no segments, the value is applied to the whole dimension.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class Cmd_Dim_ValueOverride : ExternalCommand
    {
        public override void Execute()
        {
            UIDocument uiDoc = UiDocument;
            Document doc = Document;

            try
            {
                // 1) Pick dimensions
                var refs = uiDoc.Selection.PickObjects(
                    ObjectType.Element,
                    new DimensionSelectionFilter(),
                    "Select dimensions to override");

                if (refs == null || !refs.Any())
                {
                    TaskDialog.Show("BA", "No dimensions selected.");
                    return;
                }

                // Resolve all dimensions once
                var dims = refs
                    .Select(r => doc.GetElement(r) as Dimension)
                    .Where(d => d != null)
                    .ToList();

                if (!dims.Any())
                {
                    TaskDialog.Show("BA", "No valid dimensions were selected.");
                    return;
                }

                // 2) Ask for text
                var dialog = new InputTextDialog(
                    "Enter text to override the dimension value:",
                    "Dimension Text Override");
                bool? res = dialog.ShowDialog();
                if (res != true || string.IsNullOrWhiteSpace(dialog.InputText))
                {
                    TaskDialog.Show("BA", "No text provided.");
                    return;
                }

                string additionalText = dialog.InputText.Trim();

                // 3) Determine max segment count across all dimensions
                int maxSegments = dims
                    .Select(d => d.Segments?.Size ?? 0)
                    .DefaultIfEmpty(0)
                    .Max();

                // If there are any segmented dimensions, ask for indices.
                // If user leaves it empty, we treat it as "all segments".
                List<int> segmentIndices = null;
                if (maxSegments > 0)
                {
                    segmentIndices = SegmentIndexPrompt.AskSegmentIndices(maxSegments);
                    if (segmentIndices != null && segmentIndices.Count == 0)
                    {
                        // User input was invalid and we already showed a message
                        return;
                    }
                    // segmentIndices == null => "all segments"
                }

                // 4) Apply override
                using (Transaction t = new Transaction(doc, "Dimension text override"))
                {
                    t.Start();

                    foreach (var dim in dims)
                    {
                        int segCount = dim.Segments?.Size ?? 0;

                        if (segCount > 0)
                        {
                            // Decide which indices to use for this dimension
                            IEnumerable<int> indicesToUse;
                            if (segmentIndices == null)
                            {
                                // All segments
                                indicesToUse = Enumerable.Range(0, segCount);
                            }
                            else
                            {
                                // Filter out indices that are out of range for this dimension
                                indicesToUse = segmentIndices.Where(i => i >= 0 && i < segCount);
                            }

                            foreach (int i in indicesToUse)
                            {
                                DimensionSegment seg = dim.Segments.get_Item(i);
                                if (seg == null) continue;

                                string current = seg.ValueOverride ?? string.Empty;
                                seg.ValueOverride = string.IsNullOrEmpty(current)
                                    ? additionalText
                                    : current + "\n" + additionalText;
                            }
                            // Segments not in indicesToUse are left completely untouched.
                        }
                        else
                        {
                            // No segments – apply to the whole dimension
                            string current = dim.ValueOverride ?? string.Empty;
                            dim.ValueOverride = string.IsNullOrEmpty(current)
                                ? additionalText
                                : current + "\n" + additionalText;
                        }
                    }

                    t.Commit();
                }

                TaskDialog.Show("BA", "Dimensions overridden successfully.");
            }
            catch (OperationCanceledException)
            {
                // user cancelled selection – no dialog needed
            }
            catch (Exception ex)
            {
                TaskDialog.Show("BA - Error", ex.Message);
            }
        }
    }
}
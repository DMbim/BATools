using System;
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

                // 3) Apply override
                using (Transaction t = new Transaction(doc, "Dimension text override"))
                {
                    t.Start();

                    foreach (var r in refs)
                    {
                        Dimension dim = doc.GetElement(r) as Dimension;
                        if (dim == null) continue;

                        if (dim.Segments != null && dim.Segments.Size > 0)
                        {
                            foreach (DimensionSegment seg in dim.Segments)
                            {
                                string current = seg.ValueOverride ?? string.Empty;
                                seg.ValueOverride = string.IsNullOrEmpty(current)
                                    ? additionalText
                                    : current + "\n" + additionalText;
                            }
                        }
                        else
                        {
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

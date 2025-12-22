using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace bimBA.Core.Views.Palettes
{
    /// <summary>
    /// Applies predefined color palettes (filter overrides) to view templates.
    /// </summary>
    public static class ColorPaletteManager
    {
        #region Models

        public sealed class ColorRule
        {
            public string FilterName { get; set; } = string.Empty;

            public Color ProjectionLineColor { get; set; } = new Color(0, 0, 0);
            public int ProjectionLineWeight { get; set; } = 1;

            public bool SurfaceForegroundVisible { get; set; } = true;
            public Color SurfaceForegroundColor { get; set; } = new Color(255, 255, 255);
            public ElementId? SurfaceForegroundPatternId { get; set; } // null => use default (solid fill)

            public int SurfaceTransparency { get; set; } = 0; // 0..100

            public bool CutForegroundVisible { get; set; } = true;
            public Color CutLineColor { get; set; } = new Color(0, 0, 0);
            public int CutLineWeight { get; set; } = 1;
            public Color CutForegroundColor { get; set; } = new Color(255, 255, 255);
            public ElementId? CutForegroundPatternId { get; set; } // null => use default (solid fill)
        }

        public sealed class ColorPalette
        {
            public string Name { get; set; } = string.Empty;
            public List<ColorRule> Rules { get; set; } = new();
        }

        public sealed class ApplyReport
        {
            public int Updated { get; set; }
            public int Missing { get; set; }
            public int Skipped { get; set; }
        }

        #endregion

        #region Public API: data

        public static IReadOnlyList<View> GetAllViewTemplates(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v != null && v.IsTemplate)
                .ToList();
        }

        public static List<ColorPalette> GetPredefinedPalettes()
        {
            // NOTE: keep palettes small-ish if you want TaskDialog command links.
            // If you grow beyond ~12, consider a WPF picker.
            return new List<ColorPalette>
            {
                new ColorPalette
                {
                    Name = "Fire Safety",
                    Rules = new List<ColorRule>
                    {
                        new ColorRule
                        {
                            FilterName = "Fire-rated doors",
                            ProjectionLineColor = new Color(255, 0, 0),
                            ProjectionLineWeight = 3,
                            SurfaceForegroundColor = new Color(255, 160, 160),
                            SurfaceTransparency = 0,
                            CutLineColor = new Color(255, 0, 0),
                            CutLineWeight = 3,
                            CutForegroundColor = new Color(255, 160, 160),
                        },
                        new ColorRule
                        {
                            FilterName = "Non-fire doors",
                            ProjectionLineColor = new Color(160, 160, 160),
                            ProjectionLineWeight = 2,
                            SurfaceForegroundColor = new Color(230, 230, 230),
                            SurfaceTransparency = 20,
                            CutLineColor = new Color(160, 160, 160),
                            CutLineWeight = 2,
                            CutForegroundColor = new Color(230, 230, 230),
                        }
                    }
                },

                new ColorPalette
                {
                    Name = "Discipline",
                    Rules = new List<ColorRule>
                    {
                        new ColorRule
                        {
                            FilterName = "Architecture",
                            ProjectionLineColor = new Color(0, 0, 0),
                            ProjectionLineWeight = 2,
                            SurfaceForegroundColor = new Color(200, 200, 200),
                            SurfaceTransparency = 0
                        },
                        new ColorRule
                        {
                            FilterName = "Structure",
                            ProjectionLineColor = new Color(0, 80, 255),
                            ProjectionLineWeight = 2,
                            SurfaceForegroundColor = new Color(180, 200, 255),
                            SurfaceTransparency = 10
                        },
                        new ColorRule
                        {
                            FilterName = "MEP",
                            ProjectionLineColor = new Color(0, 150, 0),
                            ProjectionLineWeight = 2,
                            SurfaceForegroundColor = new Color(200, 255, 200),
                            SurfaceTransparency = 10
                        }
                    }
                },

                new ColorPalette
                {
                    Name = "Material Type",
                    Rules = new List<ColorRule>
                    {
                        new ColorRule
                        {
                            FilterName = "Concrete",
                            ProjectionLineColor = new Color(100, 100, 100),
                            ProjectionLineWeight = 2,
                            SurfaceForegroundColor = new Color(170, 170, 170),
                            SurfaceTransparency = 0
                        },
                        new ColorRule
                        {
                            FilterName = "Steel",
                            ProjectionLineColor = new Color(0, 100, 180),
                            ProjectionLineWeight = 2,
                            SurfaceForegroundColor = new Color(150, 200, 255),
                            SurfaceTransparency = 0
                        },
                        new ColorRule
                        {
                            FilterName = "Timber",
                            ProjectionLineColor = new Color(160, 90, 0),
                            ProjectionLineWeight = 2,
                            SurfaceForegroundColor = new Color(210, 170, 120),
                            SurfaceTransparency = 0
                        }
                    }
                }
            };
        }

        #endregion

        #region Public API: selection (TaskDialog)

        /// <summary>
        /// Pick a View Template using TaskDialog. Supports paging if templates > 4.
        /// </summary>
        public static View? PickViewTemplateWithTaskDialog(IReadOnlyList<View> templates)
        {
            if (templates == null || templates.Count == 0) return null;
            return TaskDialogPicker.PickPaged("Select View Template", "Choose a view template:", templates, v => v.Name);
        }

        /// <summary>
        /// Pick a palette using TaskDialog. Supports paging if palettes > 4.
        /// </summary>
        public static ColorPalette? PickPaletteWithTaskDialog(IReadOnlyList<ColorPalette> palettes)
        {
            if (palettes == null || palettes.Count == 0) return null;
            return TaskDialogPicker.PickPaged("Select Color Palette", "Choose a predefined palette:", palettes, p => p.Name);
        }

        #endregion

        #region Apply

        /// <summary>
        /// Apply palette to a view template by id. Returns counters for updated/missing/skipped.
        /// </summary>
        public static ApplyReport ApplyPaletteToViewTemplate(Document doc, ElementId viewTemplateId, ColorPalette palette)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (viewTemplateId == null || viewTemplateId == ElementId.InvalidElementId) throw new ArgumentException("Invalid view template id.");
            if (palette == null) throw new ArgumentNullException(nameof(palette));

            var view = doc.GetElement(viewTemplateId) as View;
            if (view == null || !view.IsTemplate) throw new ArgumentException("Element is not a view template.");

            // Build filter dictionary by name (case-insensitive), from filters applied to the template
            var templateFilterIds = view.GetFilters() ?? new List<ElementId>();
            var filterByName = new Dictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);

            foreach (var id in templateFilterIds)
            {
                var pfe = doc.GetElement(id) as ParameterFilterElement;
                if (pfe != null && !string.IsNullOrWhiteSpace(pfe.Name))
                    filterByName[pfe.Name.Trim()] = id;
            }

            // Default patterns: solid fill (foreground)
            var solidFillId = GetSolidFillPatternId(doc); // may be InvalidElementId if not found

            var report = new ApplyReport();

            using (var t = new Transaction(doc, $"bimBA – Apply Palette: {palette.Name}"))
            {
                t.Start();

                foreach (var rule in palette.Rules)
                {
                    if (string.IsNullOrWhiteSpace(rule.FilterName))
                    {
                        report.Skipped++;
                        continue;
                    }

                    if (!filterByName.TryGetValue(rule.FilterName.Trim(), out var filterId) || filterId == ElementId.InvalidElementId)
                    {
                        report.Missing++;
                        continue;
                    }

                    // Build overrides
                    var ogs = BuildOverride(rule, solidFillId);

                    try
                    {
                        view.SetFilterOverrides(filterId, ogs);
                        report.Updated++;
                    }
                    catch
                    {
                        // can fail if view is not modifiable, filter not applicable, etc.
                        report.Skipped++;
                    }
                }

                t.Commit();
            }

            return report;
        }

        private static OverrideGraphicSettings BuildOverride(ColorRule rule, ElementId solidFillPatternId)
        {
            var ogs = new OverrideGraphicSettings();

            // Projection
            ogs = ogs
                .SetProjectionLineColor(rule.ProjectionLineColor)
                .SetProjectionLineWeight(ClampInt(rule.ProjectionLineWeight, 1, 16));

            // Surface foreground
            if (rule.SurfaceForegroundVisible)
            {
                ogs = ogs.SetSurfaceForegroundPatternVisible(true);

                var patt = rule.SurfaceForegroundPatternId ?? solidFillPatternId;
                if (patt != null && patt != ElementId.InvalidElementId)
                    ogs = ogs.SetSurfaceForegroundPatternId(patt);

                ogs = ogs.SetSurfaceForegroundPatternColor(rule.SurfaceForegroundColor);
            }
            else
            {
                ogs = ogs.SetSurfaceForegroundPatternVisible(false);
            }

            ogs = ogs.SetSurfaceTransparency(ClampInt(rule.SurfaceTransparency, 0, 100));

            // Cut
            ogs = ogs
                .SetCutLineColor(rule.CutLineColor)
                .SetCutLineWeight(ClampInt(rule.CutLineWeight, 1, 16));

            if (rule.CutForegroundVisible)
            {
                // There is no "CutForegroundPatternVisible" call in all versions,
                // but setting color + pattern id typically implies usage. If your Revit version
                // supports explicit visibility, you can add it similarly.
                var patt = rule.CutForegroundPatternId ?? solidFillPatternId;
                if (patt != null && patt != ElementId.InvalidElementId)
                    ogs = ogs.SetCutForegroundPatternId(patt);

                ogs = ogs.SetCutForegroundPatternColor(rule.CutForegroundColor);
            }

            return ogs;
        }

        private static ElementId GetSolidFillPatternId(Document doc)
        {
            // Solid fill is a FillPatternElement whose FillPattern.IsSolidFill == true
            var solids = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .Where(fpe =>
                {
                    var fp = fpe.GetFillPattern();
                    return fp != null && fp.IsSolidFill;
                })
                .ToList();

            return solids.Count > 0 ? solids[0].Id : ElementId.InvalidElementId;
        }

        private static int ClampInt(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        #endregion
    }

    /// <summary>
    /// Helper to pick items with TaskDialog command links (4 per page).
    /// </summary>
    /// <summary>
    /// Helper to pick items with TaskDialog command links (4 max). Supports paging safely.
    /// </summary>
    internal static class TaskDialogPicker
    {
        /// <summary>
        /// Picks an item using TaskDialog CommandLinks. Uses paging if list is longer than allowed.
        /// </summary>
        public static T? PickPaged<T>(
            string title,
            string instruction,
            IReadOnlyList<T> items,
            Func<T, string> label)
            where T : class
        {
            return PickPagedSafe(title, instruction, items, label);
        }

        /// <summary>
        /// Implementation: reserves CommandLink4 for navigation when paging exists, avoiding collisions.
        /// </summary>
        private static T? PickPagedSafe<T>(
            string title,
            string instruction,
            IReadOnlyList<T> items,
            Func<T, string> label)
            where T : class
        {
            if (items == null || items.Count == 0) return null;

            int page = 0;
            const int pageSize = 4;

            while (true)
            {
                bool hasPrev = page > 0;
                bool hasNext = (page + 1) * pageSize < items.Count;

                // If we need paging, reserve CommandLink4 for navigation => show only 3 items
                int itemSlots = (hasPrev || hasNext) ? 3 : 4;

                var chunk = items.Skip(page * pageSize).Take(pageSize).ToList();
                if (chunk.Count == 0) return null;

                var displayChunk = chunk.Take(itemSlots).ToList();

                var td = new TaskDialog(title)
                {
                    MainInstruction = instruction,
                    CommonButtons = TaskDialogCommonButtons.Cancel
                };

                var map = new Dictionary<TaskDialogResult, T>();

                for (int i = 0; i < displayChunk.Count; i++)
                {
                    var id = (TaskDialogCommandLinkId)Enum.Parse(typeof(TaskDialogCommandLinkId), $"CommandLink{i + 1}");
                    td.AddCommandLink(id, label(displayChunk[i]));
                    map[(TaskDialogResult)((int)TaskDialogResult.CommandLink1 + i)] = displayChunk[i];
                }

                if (hasPrev || hasNext)
                {
                    // We only have ONE nav slot (CommandLink4). We'll make it "Next" if possible, else "Prev".
                    string navText = hasNext ? "Next page →" : "← Previous page";
                    td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, navText);
                }

                var res = td.Show();
                if (res == TaskDialogResult.Cancel) return null;

                if (map.TryGetValue(res, out var picked))
                    return picked;

                // Navigation pressed (CommandLink4) -> move page
                if (res == TaskDialogResult.CommandLink4 && (hasPrev || hasNext))
                {
                    if (hasNext) page++;
                    else page--;
                    continue;
                }

                return null;
            }
        }
    }
}

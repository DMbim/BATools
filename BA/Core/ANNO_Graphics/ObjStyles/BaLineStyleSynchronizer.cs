using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.Core.Graphics
{
    public class BaLineStyleSynchronizer
    {
        private readonly Document _doc;

        public BaLineStyleSynchronizer(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        public void Execute()
        {
            EnsurePatterns();
            EnsureLineStyles();
        }

        private void EnsurePatterns()
        {
            var existing = new FilteredElementCollector(_doc)
                .OfClass(typeof(LinePatternElement))
                .Cast<LinePatternElement>()
                .ToDictionary(x => x.Name, x => x);

            foreach (var def in BaLineStyleRegistry.GetPatterns())
            {
                if (!existing.TryGetValue(def.Name, out var elem))
                {
                    LinePattern pattern = new LinePattern(def.Name);
                    pattern.SetSegments(def.Segments.ToList());
                    LinePatternElement.Create(_doc, pattern);
                }
                else
                {
                    LinePattern pattern = new LinePattern(def.Name);
                    pattern.SetSegments(def.Segments.ToList());
                    elem.SetLinePattern(pattern);
                }
            }
        }

        private void EnsureLineStyles()
        {
            Category linesCat = _doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);

            var subCats = linesCat.SubCategories.Cast<Category>()
                .ToDictionary(c => c.Name, c => c);

            foreach (var def in BaLineStyleRegistry.GetStyles())
            {
                Category sub;

                if (!subCats.TryGetValue(def.Name, out sub))
                {
                    sub = _doc.Settings.Categories.NewSubcategory(linesCat, def.Name);
                }
                

                ApplyStyle(sub, def);
            }
        }

        private void ApplyStyle(Category cat, BaLineStyleDefinition def)
        {
            cat.LineColor = def.Color;
            cat.SetLineWeight(def.LineWeight, GraphicsStyleType.Cut);

            var pattern = GetPattern(def.PatternName);
            if (pattern != null)
            {
                cat.SetLinePatternId(pattern.Id, GraphicsStyleType.Projection);
            }
        }
        public static class BaLineStyleClassifier
        {
            public static bool IsSwing(string name)
                => name.StartsWith("BA_Swing");

            public static bool IsClearance(string name)
                => name.StartsWith("BA_Clearance");

            public static bool IsOverhead(string name)
                => name.StartsWith("BA_Overhead");
        }
        private LinePatternElement GetPattern(string name)
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(LinePatternElement))
                .Cast<LinePatternElement>()
                .FirstOrDefault(x => x.Name == name);
        }
    }
}
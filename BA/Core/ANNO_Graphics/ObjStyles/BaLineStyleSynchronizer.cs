// FILE: BA_Tools/Core/Graphics/BaLineStyleSynchronizer.cs
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.Core.Graphics
{
    public class BaLineStyleSynchronizer
    {
        private readonly Document _doc;

        // Sentinel name used in registry to mean "use the built-in Revit solid pattern"
        public const string SolidSentinel = "BA_Solid";

        public BaLineStyleSynchronizer(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        /// <summary>
        /// Creates or updates all BA line patterns and line styles in the document.
        /// Must be called from within a valid Revit API context (IExternalCommand or IExternalEventHandler).
        /// </summary>
        public void Execute()
        {
            using Transaction tx = new Transaction(_doc, "BA: Synchronize Line Styles");
            tx.Start();

            try
            {
                EnsurePatterns();
                EnsureLineStyles();
                tx.Commit();
            }
            catch
            {
                if (tx.HasStarted())
                    tx.RollBack();
                throw;
            }
        }

        private void EnsurePatterns()
        {
            // Build a current snapshot BEFORE any mutations so we are not
            // iterating a collector that changes underneath us.
            Dictionary<string, LinePatternElement> existing = new FilteredElementCollector(_doc)
                .OfClass(typeof(LinePatternElement))
                .Cast<LinePatternElement>()
                .ToDictionary(x => x.Name, x => x);

            foreach (BaLinePatternDefinition def in BaLineStyleRegistry.GetPatterns())
            {
                // BA_Solid is never stored as a LinePatternElement.
                // Revit exposes the solid pattern via LinePatternElement.GetSolidPatternId().
                // Creating a LinePattern with zero segments throws ArgumentException.
                if (def.Name == SolidSentinel)
                    continue;

                if (existing.TryGetValue(def.Name, out LinePatternElement existingElem))
                {
                    // Delete and recreate. SetLinePattern on an in-use element is
                    // unreliable and can leave referencing styles in an inconsistent state.
                    _doc.Delete(existingElem.Id);
                }

                LinePattern pattern = new LinePattern(def.Name);
                pattern.SetSegments(def.Segments.ToList());
                LinePatternElement.Create(_doc, pattern);
            }
        }

        private void EnsureLineStyles()
        {
            Category linesCat = _doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);

            Dictionary<string, Category> subCats = linesCat.SubCategories
                .Cast<Category>()
                .ToDictionary(c => c.Name, c => c);

            foreach (BaLineStyleDefinition def in BaLineStyleRegistry.GetStyles())
            {
                if (!subCats.TryGetValue(def.Name, out Category sub))
                {
                    sub = _doc.Settings.Categories.NewSubcategory(linesCat, def.Name);
                }

                ApplyStyle(sub, def);
            }
        }

        private void ApplyStyle(Category cat, BaLineStyleDefinition def)
        {
            cat.LineColor = def.Color;
            cat.SetLineWeight(def.LineWeight, GraphicsStyleType.Projection);
            cat.SetLineWeight(def.LineWeight, GraphicsStyleType.Cut);

            ElementId patternId = ResolvePatternId(def.PatternName);
            if (patternId != ElementId.InvalidElementId)
            {
                cat.SetLinePatternId(patternId, GraphicsStyleType.Projection);
                cat.SetLinePatternId(patternId, GraphicsStyleType.Cut);
            }
        }

        private ElementId ResolvePatternId(string patternName)
        {
            // BA_Solid maps to the built-in Revit solid pattern, never a custom element.
            if (patternName == SolidSentinel)
                return LinePatternElement.GetSolidPatternId();

            LinePatternElement elem = new FilteredElementCollector(_doc)
                .OfClass(typeof(LinePatternElement))
                .Cast<LinePatternElement>()
                .FirstOrDefault(x => x.Name == patternName);

            return elem?.Id ?? ElementId.InvalidElementId;
        }

        public static class BaLineStyleClassifier
        {
            public static bool IsSwing(string name) => name.StartsWith("BA_Swing");
            public static bool IsClearance(string name) => name.StartsWith("BA_Clearance");
            public static bool IsOverhead(string name) => name.StartsWith("BA_Overhead");
        }
    }
}
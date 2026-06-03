// FILE: BA_Tools/Core/Graphics/BaLineStyleRegistry.cs
using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace BA.Core.Graphics
{
    public static class BaLineStyleRegistry
    {
        /// <summary>
        /// Returns all custom line pattern definitions.
        /// BA_Solid is included as a registry entry so callers know it exists,
        /// but the synchronizer skips creating it as a LinePatternElement because
        /// Revit exposes solid via LinePatternElement.GetSolidPatternId().
        ///
        /// IMPORTANT: Dot segments are represented as very short Dash segments.
        /// Revit's LinePattern validator rejects Dot segments with length 0.0 and
        /// throws ArgumentException("The Line Pattern is not valid").
        /// A Dash of 0.2mm is visually indistinguishable from a dot at drawing scale.
        /// </summary>
        public static List<BaLinePatternDefinition> GetPatterns()
        {
            // All lengths in decimal feet. Source values are in mm, divided by 304.8.
            const double mm = 1.0 / 304.8;

            return new List<BaLinePatternDefinition>
            {
                // Zero segments: synchronizer skips LinePatternElement creation for this.
                // Resolved via LinePatternElement.GetSolidPatternId() at runtime.
                new BaLinePatternDefinition(
                    BaLineStyleSynchronizer.SolidSentinel,
                    new List<LinePatternSegment>()),

                new BaLinePatternDefinition("BA_Dash", new List<LinePatternSegment>
                {
                    new LinePatternSegment(LinePatternSegmentType.Dash,  3.0 * mm),
                    new LinePatternSegment(LinePatternSegmentType.Space, 1.5 * mm),
                }),

                new BaLinePatternDefinition("BA_DashDot", new List<LinePatternSegment>
                {
                    new LinePatternSegment(LinePatternSegmentType.Dash,  3.0 * mm),
                    new LinePatternSegment(LinePatternSegmentType.Space, 1.5 * mm),
                    // Dot represented as a very short dash (0.2mm). Revit rejects Dot
                    // segments with length 0.0 with ArgumentException at pattern creation.
                    new LinePatternSegment(LinePatternSegmentType.Dash,  0.2 * mm),
                    new LinePatternSegment(LinePatternSegmentType.Space, 1.5 * mm),
                }),

                new BaLinePatternDefinition("BA_Hidden", new List<LinePatternSegment>
                {
                    new LinePatternSegment(LinePatternSegmentType.Dash,  2.0 * mm),
                    new LinePatternSegment(LinePatternSegmentType.Space, 1.0 * mm),
                }),

                new BaLinePatternDefinition("BA_Center", new List<LinePatternSegment>
                {
                    new LinePatternSegment(LinePatternSegmentType.Dash,  4.0 * mm),
                    new LinePatternSegment(LinePatternSegmentType.Space, 1.0 * mm),
                    // Dot represented as a very short dash.
                    new LinePatternSegment(LinePatternSegmentType.Dash,  0.2 * mm),
                    new LinePatternSegment(LinePatternSegmentType.Space, 1.0 * mm),
                }),
            };
        }

        public static List<BaLineStyleDefinition> GetStyles()
        {
            return new List<BaLineStyleDefinition>
            {
                new BaLineStyleDefinition("BA_Main__Solid_Medium",               BaLineStyleSynchronizer.SolidSentinel, 3, new Color(0,   0,   0)),
                new BaLineStyleDefinition("BA_Swing__Dash_Thin",                 "BA_Dash",                             1, new Color(0,   0,   0)),
                new BaLineStyleDefinition("BA_Overhead__DashDot_Thin",           "BA_DashDot",                          1, new Color(0,   0,   0)),
                new BaLineStyleDefinition("BA_Hidden__Hidden_Thin",              "BA_Hidden",                           1, new Color(0,   0,   0)),
                new BaLineStyleDefinition("BA_Centerline__Center_Thin",          "BA_Center",                           1, new Color(0,   0,   0)),
                new BaLineStyleDefinition("BA_Reference__Solid_Thin_Gray",       BaLineStyleSynchronizer.SolidSentinel, 1, new Color(150, 150, 150)),
                new BaLineStyleDefinition("BA_Clearance__Solid_Thin_LightBlue",  BaLineStyleSynchronizer.SolidSentinel, 1, new Color(120, 180, 255)),
                new BaLineStyleDefinition("BA_Boundary__Solid_Thick",            BaLineStyleSynchronizer.SolidSentinel, 5, new Color(0,   0,   0)),
                new BaLineStyleDefinition("BA_Path__DashDot_Medium",             "BA_DashDot",                          3, new Color(0,   0,   0)),
                new BaLineStyleDefinition("BA_Grid__Solid_Thin_Gray",            BaLineStyleSynchronizer.SolidSentinel, 1, new Color(180, 180, 180)),
                new BaLineStyleDefinition("BA_Screen__Solid_Thin_VeryLightGray", BaLineStyleSynchronizer.SolidSentinel, 1, new Color(220, 220, 220)),
            };
        }
    }
}
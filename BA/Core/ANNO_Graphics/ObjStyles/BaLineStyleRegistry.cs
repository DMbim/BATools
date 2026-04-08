using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace BA.Core.Graphics
{
    public static class BaLineStyleRegistry
    {
        public static List<BaLinePatternDefinition> GetPatterns()
        {
            return new List<BaLinePatternDefinition>
            {
                new BaLinePatternDefinition("BA_Solid", new List<LinePatternSegment>()),

                new BaLinePatternDefinition("BA_Dash", new List<LinePatternSegment>
                {
                    new LinePatternSegment(LinePatternSegmentType.Dash, 3.0/304.8),
                    new LinePatternSegment(LinePatternSegmentType.Space, 1.5/304.8),
                }),

                new BaLinePatternDefinition("BA_DashDot", new List<LinePatternSegment>
                {
                    new LinePatternSegment(LinePatternSegmentType.Dash, 3.0/304.8),
                    new LinePatternSegment(LinePatternSegmentType.Space, 1.5/304.8),
                    new LinePatternSegment(LinePatternSegmentType.Dot, 0.0),
                    new LinePatternSegment(LinePatternSegmentType.Space, 1.5/304.8),
                }),

                new BaLinePatternDefinition("BA_Hidden", new List<LinePatternSegment>
                {
                    new LinePatternSegment(LinePatternSegmentType.Dash, 2.0/304.8),
                    new LinePatternSegment(LinePatternSegmentType.Space, 1.0/304.8),
                }),

                new BaLinePatternDefinition("BA_Center", new List<LinePatternSegment>
                {
                    new LinePatternSegment(LinePatternSegmentType.Dash, 4.0/304.8),
                    new LinePatternSegment(LinePatternSegmentType.Space, 1.0/304.8),
                    new LinePatternSegment(LinePatternSegmentType.Dot, 0.0),
                    new LinePatternSegment(LinePatternSegmentType.Space, 1.0/304.8),
                }),
            };
        }

        public static List<BaLineStyleDefinition> GetStyles()
        {
            return new List<BaLineStyleDefinition>
            {
                new BaLineStyleDefinition("BA_Main__Solid_Medium", "BA_Solid", 3, new Color(0,0,0)),
                new BaLineStyleDefinition("BA_Swing__Dash_Thin", "BA_Dash", 1, new Color(0,0,0)),
                new BaLineStyleDefinition("BA_Overhead__DashDot_Thin", "BA_DashDot", 1, new Color(0,0,0)),
                new BaLineStyleDefinition("BA_Hidden__Hidden_Thin", "BA_Hidden", 1, new Color(0,0,0)),
                new BaLineStyleDefinition("BA_Centerline__Center_Thin", "BA_Center", 1, new Color(0,0,0)),
                new BaLineStyleDefinition("BA_Reference__Solid_Thin_Gray", "BA_Solid", 1, new Color(150,150,150)),
                new BaLineStyleDefinition("BA_Clearance__Solid_Thin_LightBlue", "BA_Solid", 1, new Color(120,180,255)),
                new BaLineStyleDefinition("BA_Boundary__Solid_Thick", "BA_Solid", 5, new Color(0,0,0)),

                new BaLineStyleDefinition("BA_Path__DashDot_Medium", "BA_DashDot", 3, new Color(0,0,0)),
                new BaLineStyleDefinition("BA_Grid__Solid_Thin_Gray", "BA_Solid", 1, new Color(180,180,180)),
                new BaLineStyleDefinition("BA_Screen__Solid_Thin_VeryLightGray", "BA_Solid", 1, new Color(220,220,220)),
            };
        }
    }
}

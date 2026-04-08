using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.UI.KeyplanGrid
{
    public static class KeyplanSplitConversionService
    {
        public static List<KeyplanSplitLineItem> CloneEnabledOrdered(
            IEnumerable<KeyplanSplitLineItem> splits,
            AxisOrientation orientation)
        {
            return (splits ?? Enumerable.Empty<KeyplanSplitLineItem>())
                .Where(x => x != null && x.IsEnabled && x.Orientation == orientation)
                .Select(x => new KeyplanSplitLineItem
                {
                    Id = x.Id,
                    Orientation = x.Orientation,
                    Normalized = Clamp01(x.Normalized),
                    IsEnabled = x.IsEnabled,
                    IsSelected = x.IsSelected,
                    Name = x.Name
                })
                .OrderBy(x => x.Normalized)
                .ThenBy(x => x.Id, StringComparer.Ordinal)
                .ToList();
        }

        public static double[] ToBreakArray(IEnumerable<KeyplanSplitLineItem> splits, AxisOrientation orientation)
        {
            List<double> values = CloneEnabledOrdered(splits, orientation)
                .Select(x => Clamp01(x.Normalized))
                .ToList();

            List<double> result = new List<double> { 0.0 };
            result.AddRange(values);
            result.Add(1.0);

            return result.ToArray();
        }

        internal static List<KeyplanSplitLineItem> ToBreakArray(double[] xBreaks, AxisOrientation vertical)
        {
            throw new NotImplementedException();
        }

        private static double Clamp01(double value)
        {
            if (value < 0.0) return 0.0;
            if (value > 1.0) return 1.0;
            return value;
        }
    }
}
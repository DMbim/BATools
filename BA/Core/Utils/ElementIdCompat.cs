using Autodesk.Revit.DB;
using System;

using Autodesk.Revit.DB;
using System;
using System.Reflection;

namespace BA.Core.Utils
{
    /// <summary>
    /// ElementId compatibility helpers across Revit versions.
    /// - Revit 2024+: ElementId.Value (long) and ctor(long)
    /// - Older: ElementId.IntegerValue (int) and ctor(int)
    /// </summary>
    public static class ElementIdCompat
    {
        private static readonly Func<ElementId, long> _getValue = BuildGetter();
        private static readonly Func<long, ElementId> _create = BuildCtor();

        public static long GetValue(ElementId id) => id == null ? -1 : _getValue(id);

        public static ElementId Create(long value) => _create(value);

        private static Func<ElementId, long> BuildGetter()
        {
            // Prefer Value (long)
            var pValue = typeof(ElementId).GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
            if (pValue?.PropertyType == typeof(long))
                return (ElementId id) => (long)pValue.GetValue(id);

            // Fallback: IntegerValue (int) older versions
            var pInt = typeof(ElementId).GetProperty("IntegerValue", BindingFlags.Instance | BindingFlags.Public);
            if (pInt?.PropertyType == typeof(int))
                return (ElementId id) => Convert.ToInt64((int)pInt.GetValue(id));

            // Extremely defensive fallback
            return _ => -1;
        }

        private static Func<long, ElementId> BuildCtor()
        {
            // Prefer ctor(long)
            var ctorLong = typeof(ElementId).GetConstructor(new[] { typeof(long) });
            if (ctorLong != null)
                return (long v) => (ElementId)ctorLong.Invoke(new object[] { v });

            // Fallback: ctor(int)
            var ctorInt = typeof(ElementId).GetConstructor(new[] { typeof(int) });
            if (ctorInt != null)
                return (long v) => (ElementId)ctorInt.Invoke(new object[] { unchecked((int)v) });

            throw new NotSupportedException("No compatible ElementId constructor found.");
        }
    }
}


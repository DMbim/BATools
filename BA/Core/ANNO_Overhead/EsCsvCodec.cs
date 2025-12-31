using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BA.Core.Overhead
{
    internal static class EsCsvCodec
    {
        public static string EncodeLongs(IEnumerable<long> values)
            => string.Join(",", (values ?? Array.Empty<long>()));

        public static List<long> DecodeLongs(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) return new List<long>();

            return csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                      .Select(s =>
                      {
                          if (long.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                              return (long?)v;
                          return null;
                      })
                      .Where(v => v.HasValue)
                      .Select(v => v!.Value)
                      .ToList();
        }

        public static string EncodeInts(IEnumerable<int> values)
            => string.Join(",", (values ?? Array.Empty<int>()));

        public static List<int> DecodeInts(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) return new List<int>();

            return csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                      .Select(s =>
                      {
                          if (int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                              return (int?)v;
                          return null;
                      })
                      .Where(v => v.HasValue)
                      .Select(v => v!.Value)
                      .ToList();
        }
    }
}

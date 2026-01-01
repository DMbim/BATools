// File: BA.Classification/CsvRuleTraceSink.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BA.Classification
{
    public interface IRuleTraceSink : IDisposable
    {
        void WriteHeader();
        void WriteTypeTrace(TypeTraceRow row);
    }

    public sealed class TypeTraceRow
    {
        public int TypeId { get; init; }
        public int RepresentativeInstanceId { get; init; }
        public string Category { get; init; } = "";
        public string FamilyName { get; init; } = "";
        public string TypeName { get; init; } = "";

        public string WinnerRuleId { get; init; } = "";
        public string WinnerTargetCode { get; init; } = "";

        public string WinnerWhy { get; init; } = "";
        public string MatchedRuleIds { get; init; } = "";
        public string MatchedDetails { get; init; } = "";
    }

    public sealed class CsvRuleTraceSink : IRuleTraceSink
    {
        private readonly StreamWriter _w;

        public CsvRuleTraceSink(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            _w = new StreamWriter(path, false);
        }

        public void WriteHeader()
        {
            _w.WriteLine(string.Join(",",
                "TypeId",
                "RepInstanceId",
                "Category",
                "FamilyName",
                "TypeName",
                "WinnerRuleId",
                "WinnerTargetCode",
                "WinnerWhy",
                "MatchedRuleIds",
                "MatchedDetails"
            ));
            _w.Flush();
        }

        public void WriteTypeTrace(TypeTraceRow row)
        {
            _w.WriteLine(string.Join(",",
                Csv(row.TypeId.ToString()),
                Csv(row.RepresentativeInstanceId.ToString()),
                Csv(row.Category),
                Csv(row.FamilyName),
                Csv(row.TypeName),
                Csv(row.WinnerRuleId),
                Csv(row.WinnerTargetCode),
                Csv(row.WinnerWhy),
                Csv(row.MatchedRuleIds),
                Csv(row.MatchedDetails)
            ));
        }

        private static string Csv(string s)
        {
            s ??= "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            if (s.Contains(',') || s.Contains('"'))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        public void Dispose()
        {
            _w.Flush();
            _w.Dispose();
        }
    }
}

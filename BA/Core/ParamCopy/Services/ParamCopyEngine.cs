using Autodesk.Revit.DB;
using BATools.ParamCopy.Models;
using System;
using System.Collections.Generic;

namespace BATools.ParamCopy.Services
{
    public static class ParamCopyEngine
    {
        public struct CopyResult
        {
            public int Written;
            public int Skipped;
            public int Errors;
            public List<string> ErrorMessages;
        }

        /// <summary>
        /// Executes parameter copy for all pairs using all mappings.
        /// Must be called inside an active Transaction on the Revit main thread.
        /// </summary>
        public static CopyResult Execute(
            Document doc,
            IReadOnlyList<ElementPair> pairs,
            IReadOnlyList<ParamMapping> mappings)
        {
            var result = new CopyResult { ErrorMessages = new List<string>() };

            if (pairs == null || pairs.Count == 0 ||
                mappings == null || mappings.Count == 0)
                return result;

            foreach (var pair in pairs)
            {
                Element? src = doc.GetElement(pair.SourceId);
                Element? dst = doc.GetElement(pair.DestId);

                if (src == null || dst == null)
                {
                    result.Errors++;
                    result.ErrorMessages.Add(
                        $"Pair ({pair.SourceId}/{pair.DestId}): element not found.");
                    continue;
                }

                foreach (var mapping in mappings)
                {
                    if (string.IsNullOrWhiteSpace(mapping.SourceParameterName) ||
                        string.IsNullOrWhiteSpace(mapping.DestParameterName))
                        continue;

                    try
                    {
                        Parameter? srcParam = src.LookupParameter(mapping.SourceParameterName);
                        Parameter? dstParam = dst.LookupParameter(mapping.DestParameterName);

                        if (srcParam == null || dstParam == null || dstParam.IsReadOnly)
                        {
                            result.Skipped++;
                            continue;
                        }

                        if (mapping.WriteOnlyIfEmpty)
                        {
                            string existing = ElementFilterService.GetParamString(
                                dst, mapping.DestParameterName);
                            if (!string.IsNullOrEmpty(existing))
                            {
                                result.Skipped++;
                                continue;
                            }
                        }

                        bool ok = CopyParameter(srcParam, dstParam);
                        if (ok) result.Written++;
                        else result.Skipped++;
                    }
                    catch (Exception ex)
                    {
                        result.Errors++;
                        result.ErrorMessages.Add(
                            $"El {src.Id}->{dst.Id} [{mapping.SourceParameterName}]: {ex.Message}");
                    }
                }
            }

            return result;
        }

        private static bool CopyParameter(Parameter src, Parameter dest)
        {
            if (src.StorageType == dest.StorageType)
            {
                switch (src.StorageType)
                {
                    case StorageType.String:
                        dest.Set(src.AsString() ?? string.Empty);
                        return true;
                    case StorageType.Double:
                        dest.Set(src.AsDouble());
                        return true;
                    case StorageType.Integer:
                        dest.Set(src.AsInteger());
                        return true;
                    case StorageType.ElementId:
                        dest.Set(src.AsElementId());
                        return true;
                    default:
                        return false;
                }
            }

            if (dest.StorageType == StorageType.String)
            {
                dest.Set(src.AsValueString() ?? string.Empty);
                return true;
            }

            if (src.StorageType == StorageType.String)
            {
                string raw = src.AsString() ?? string.Empty;
                if (dest.StorageType == StorageType.Double &&
                    double.TryParse(raw,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double d))
                {
                    dest.Set(d);
                    return true;
                }
                if (dest.StorageType == StorageType.Integer &&
                    int.TryParse(raw, out int i))
                {
                    dest.Set(i);
                    return true;
                }
            }

            return false;
        }
    }
}

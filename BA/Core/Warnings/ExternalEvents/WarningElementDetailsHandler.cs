// FILE: BA_Tools/Warnings/ExternalEvents/WarningElementDetailsHandler.cs
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.UI.ExternalEvents;
using BA.Warnings.Helpers;
using BA.Warnings.Models;

namespace BA.Warnings.ExternalEvents
{
    public static class WarningElementDetailsHandler
    {
        public static void RequestDetails(WarningItem target, Action<List<WarningElementDetail>> onCompleted)
        {
            AppExternalInvoker.Instance.Run(
                app => ExecuteDetails(app, target),
                onCompleted,
                ex =>
                {
                    AppLogger.LogError("WarningElementDetailsHandler.RequestDetails", ex);
                    onCompleted?.Invoke(new List<WarningElementDetail>());
                });
        }

        private static List<WarningElementDetail> ExecuteDetails(UIApplication app, WarningItem target)
        {
            var result = new List<WarningElementDetail>();

            UIDocument uiDoc = app.ActiveUIDocument;
            if (uiDoc == null || target == null) return result;

            Document doc = uiDoc.Document;
            string paramName = ExtractQuotedParameterName(target.Description);

            foreach (ElementId id in target.AllElementIds)
            {
                Element el = doc.GetElement(id);
                if (el == null) continue;

                string category = el.Category?.Name ?? "(no category)";
                string familyAndType = ElementDisplayHelper.GetFamilyAndTypeName(doc, el);
                string errorValue = paramName != null ? GetParameterValueDisplay(el, paramName) : null;

                string displayText = string.IsNullOrEmpty(errorValue)
                    ? $"{category} : {familyAndType} - id {id.Value}"
                    : $"{category} : {familyAndType} - id {id.Value} - Value: {errorValue}";

                result.Add(new WarningElementDetail
                {
                    ElementId = id,
                    Category = category,
                    FamilyAndType = familyAndType,
                    ErrorValue = errorValue,
                    DisplayText = displayText
                });
            }

            return result;
        }

        private static string ExtractQuotedParameterName(string description)
        {
            if (string.IsNullOrEmpty(description)) return null;
            Match m = Regex.Match(description, "\"([^\"]+)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string GetParameterValueDisplay(Element el, string paramName)
        {
            Parameter p = el.LookupParameter(paramName);
            if (p == null || !p.HasValue) return null;
            return p.AsValueString() ?? p.AsString();
        }
    }
}
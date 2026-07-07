using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BATools.SelectionManager.ExternalEvents
{
    public class FamilyTypeInfo
    {
        public string UniqueId { get; init; } = string.Empty;
        public string FamilyName { get; init; } = string.Empty;
        public string TypeName { get; init; } = string.Empty;
        public string CategoryName { get; init; } = string.Empty;
    }

    public class ReadFamilyTypesHandler : IExternalEventHandler
    {
        public Action<List<FamilyTypeInfo>>? OnComplete { get; set; }

        public void Execute(UIApplication uiApp)
        {
            var doc = uiApp.ActiveUIDocument?.Document;
            if (doc == null)
            {
                DispatchResult(new List<FamilyTypeInfo>());
                return;
            }

            var symbols = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(s => s.IsValidObject && s.Family != null)
                .OrderBy(s => s.FamilyName)
                .ThenBy(s => s.Name)
                .Select(s => new FamilyTypeInfo
                {
                    UniqueId = s.UniqueId,
                    FamilyName = s.FamilyName,
                    TypeName = s.Name,
                    CategoryName = s.Category?.Name ?? string.Empty
                })
                .ToList();

            DispatchResult(symbols);
        }

        private void DispatchResult(List<FamilyTypeInfo> result)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                new Action(() => OnComplete?.Invoke(result)));
        }

        public string GetName() => "ReadFamilyTypes";
    }
}
using Autodesk.Revit.UI;
using BA.Core.Standards;
using System;
using System.Collections.Generic;

namespace BA.UI.Standards
{
    public sealed class SubcategoryAuditExternalHandler : IExternalEventHandler
    {
        public Func<UIApplication, IList<SubcategoryAuditRow>> ExecuteFunc { get; set; }
        public Action<IList<SubcategoryAuditRow>> SuccessAction { get; set; }
        public Action<Exception> ErrorAction { get; set; }

        public void Execute(UIApplication app)
        {
            try
            {
                IList<SubcategoryAuditRow> result = ExecuteFunc?.Invoke(app) ?? new List<SubcategoryAuditRow>();
                SuccessAction?.Invoke(result);
            }
            catch (Exception ex)
            {
                ErrorAction?.Invoke(ex);
            }
        }

        public string GetName()
        {
            return "BA Subcategory Audit External Handler";
        }
    }
}
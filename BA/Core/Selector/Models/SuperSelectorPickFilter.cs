// File: BA.Core/Selection/SuperSelectorPickFilter.cs
using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;

namespace BA.Core.Selection
{
    // Used as the ISelectionFilter argument to UIDocument.Selection.PickObjects.
    // Category membership is checked first as a cheap reject before any
    // parameter evaluation runs. Does not touch UIDocument.Selection itself;
    // that call happens in the ViewModel's RevitExternalInvoker.Run action.
    public sealed class SuperSelectorPickFilter : ISelectionFilter
    {
        private readonly Document _doc;
        private readonly HashSet<ElementId> _allowedCategoryIds;
        private readonly IReadOnlyList<SuperSelectorCriterion> _criteria;
        private readonly SuperSelectorLogic _logic;

        public SuperSelectorPickFilter(
            Document doc,
            IEnumerable<ElementId> allowedCategoryIds,
            IReadOnlyList<SuperSelectorCriterion> criteria,
            SuperSelectorLogic logic)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            if (allowedCategoryIds == null) throw new ArgumentNullException(nameof(allowedCategoryIds));
            _allowedCategoryIds = new HashSet<ElementId>(allowedCategoryIds);
            _criteria = criteria ?? Array.Empty<SuperSelectorCriterion>();
            _logic = logic;
        }

        public bool AllowElement(Element elem)
        {
            if (elem?.Category == null) return false;
            if (!_allowedCategoryIds.Contains(elem.Category.Id)) return false;

            return SuperSelectorCriteriaEvaluator.Evaluate(_doc, elem, _criteria, _logic);
        }

        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}
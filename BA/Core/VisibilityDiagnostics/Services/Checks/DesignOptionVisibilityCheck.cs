// File: BA_Tools/VisibilityDiagnostics/Services/Checks/DesignOptionVisibilityCheck.cs
using System;
using Autodesk.Revit.DB;
using BA.VisibilityDiagnostics.Models;

namespace BA.VisibilityDiagnostics.Services.Checks
{
    /// <summary>
    /// Checks whether the element belongs to a non-Primary Design Option that is
    /// not currently the active option being edited.
    ///
    /// Elements with no Design Option belong to the Main Model and are never
    /// affected. Elements in a Design Option are shown when either: (a) that exact
    /// option is the document's currently active option
    /// (DesignOption.GetActiveDesignOptionId), or (b) no option from that same
    /// Option Set is currently active AND the element's option is the Primary
    /// option of its set. Any other combination hides the element.
    /// </summary>
    public sealed class DesignOptionVisibilityCheck : IVisibilityCheck
    {
        public string CheckName => "Design Option";

        public VisibilityCheckItem Evaluate(Document document, View view, Element element)
        {
            DesignOption? elementOption = element.DesignOption;
            if (elementOption is null)
            {
                return VisibilityCheckItem.Ok(CheckName,
                    "Element belongs to the Main Model (not assigned to any Design Option). Design Options cannot be hiding it.");
            }

            ElementId elementSetId = elementOption.get_Parameter(BuiltInParameter.OPTION_SET_ID)?.AsElementId() ?? ElementId.InvalidElementId;
            string setName = elementSetId != ElementId.InvalidElementId && document.GetElement(elementSetId) is Element setElement
                ? setElement.Name
                : "(unknown option set)";

            ElementId activeOptionId;
            try
            {
                activeOptionId = DesignOption.GetActiveDesignOptionId(document);
            }
            catch (Exception ex)
            {
                return VisibilityCheckItem.Error(CheckName, $"Could not read the active Design Option: {ex.Message}");
            }

            if (activeOptionId != ElementId.InvalidElementId && activeOptionId == elementOption.Id)
            {
                return VisibilityCheckItem.Ok(CheckName,
                    $"Element's Design Option '{elementOption.Name}' (set '{setName}') is currently ACTIVE for editing. It is displayed.");
            }

            ElementId activeOptionsSetId = ElementId.InvalidElementId;
            DesignOption? activeOption = null;
            if (activeOptionId != ElementId.InvalidElementId)
            {
                activeOption = document.GetElement(activeOptionId) as DesignOption;
                activeOptionsSetId = activeOption?.get_Parameter(BuiltInParameter.OPTION_SET_ID)?.AsElementId() ?? ElementId.InvalidElementId;
            }

            if (activeOptionsSetId != ElementId.InvalidElementId && activeOptionsSetId == elementSetId)
            {
                return VisibilityCheckItem.Hidden(CheckName,
                    $"A different Design Option ('{activeOption?.Name ?? "(unknown)"}') within the same Option Set " +
                    $"('{setName}') is currently active for editing. Elements in Design Option '{elementOption.Name}' are " +
                    "not shown while that's the case.",
                    recommendation: $"Activate Design Option '{elementOption.Name}' for editing (Manage tab > Design " +
                        "Options), or exit Design Option editing mode to fall back to the Primary option.");
            }

            if (elementOption.IsPrimary)
            {
                return VisibilityCheckItem.Ok(CheckName,
                    $"Element's Design Option '{elementOption.Name}' (set '{setName}') is the PRIMARY option of its set, " +
                    "and no other option in that set is currently active for editing, so it displays.");
            }

            return VisibilityCheckItem.Hidden(CheckName,
                $"Element's Design Option '{elementOption.Name}' (set '{setName}') is NOT the Primary option, and it is " +
                "not currently active for editing. Only the Primary option's elements display under these conditions.",
                recommendation: $"Either make '{elementOption.Name}' the Primary option for set '{setName}' (Manage tab > " +
                    "Design Options), or activate it for editing to see this element.");
        }
    }
}

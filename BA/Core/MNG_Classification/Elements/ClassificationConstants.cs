using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BA.Core.Classification
{
    public enum ClassificationMode
    {
        FillEmptyOnly,
        OverwriteAll,
        Cancel
    }

    public static class ClassificationConstants
    {
        public static readonly ClassificationParameterNames DefaultParameterNames =
            new ClassificationParameterNames(
                "BA_Class_Domain",
                "BA_Class_Group",
                "BA_Class_Subcode",
                "BA_Class_Code",
                "BA_Class_Name_CZ",
                "BA_Class_Name_EN");
    }

    public readonly record struct ClassificationParameterNames(
        string Domain,
        string Group,
        string Subcode,
        string Code,
        string NameCz,
        string NameEn
    );
}

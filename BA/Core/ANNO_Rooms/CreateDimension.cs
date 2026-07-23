using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BA.Core.Dim
{
    internal class CreateDimension
    {
        public Dimension CreateNewDimension(Document document, Line line, ReferenceArray references)
        {
            Autodesk.Revit.DB.View view = document.ActiveView;
            Dimension dimension = document.Create.NewDimension(view, line, references);
            return dimension;
        }
    }
}

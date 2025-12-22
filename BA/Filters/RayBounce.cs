using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using CommunityToolkit.Mvvm.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BA.Filters
{
    internal class RayBounce(UIApplication uiApp, UIDocument uiDoc, Document doc)
    {


        public void CalculateRayBounce()
        {


            // 1. Select an element
            Reference pickedRef = uiDoc.Selection.PickObject(ObjectType.Element, "Select an element to find the ceiling above.");
            Element selectedElement = doc.GetElement(pickedRef.ElementId);
            if (selectedElement == null)
            {
                TaskDialog.Show("Error", "No element was selected.");

            }

            // 2. Get center point of element
            BoundingBoxXYZ bbox = selectedElement.get_BoundingBox(null);
            if (bbox == null)
            {
                TaskDialog.Show("Error", "Selected element has no bounding box.");

            }

            XYZ center = (bbox.Min + bbox.Max) / 2;
            XYZ origin = new XYZ(center.X, center.Y, center.Z - 1.0); // Start below the element
            XYZ direction = XYZ.BasisZ; // Upwards

            // 3. Get any non-template 3D view
            View3D view3D = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.ThreeD);

            if (view3D == null)
            {
                TaskDialog.Show("Error", "No valid 3D view found.");

            }

            // 4. Ray bounce setup
            ReferenceIntersector intersector = new ReferenceIntersector(
                new ElementClassFilter(typeof(Ceiling)),
                FindReferenceTarget.Element,
                view3D
            )
            {
                FindReferencesInRevitLinks = true
            };

            ReferenceWithContext refContext = intersector.FindNearest(origin, direction);
            if (refContext == null)
            {
                TaskDialog.Show("No Ceiling Found", "No ceiling was detected above the selected element.");

            }

            Reference reference = refContext.GetReference();
            Element ceiling = null;

            // 5. Handle linked ceilings
            if (reference.LinkedElementId != ElementId.InvalidElementId)
            {
                RevitLinkInstance linkInstance = doc.GetElement(reference.ElementId) as RevitLinkInstance;
                if (linkInstance == null)
                {
                    TaskDialog.Show("Error", "Failed to resolve RevitLinkInstance.");

                }

                Document linkedDoc = linkInstance.GetLinkDocument();
                if (linkedDoc == null)
                {
                    TaskDialog.Show("Error", "Linked document could not be accessed.");

                }

                ceiling = linkedDoc.GetElement(reference.LinkedElementId);
            }
            else
            {
                ceiling = doc.GetElement(reference.ElementId);
            }

            if (ceiling == null)
            {
                TaskDialog.Show("Error", "Ceiling could not be retrieved.");

            }

            // 6. Get height offset from level
            Parameter heightOffsetParam = ceiling.get_Parameter(BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM);
            if (heightOffsetParam == null || !heightOffsetParam.HasValue)
            {
                TaskDialog.Show("Error", "Ceiling does not contain a valid 'Height Offset From Level' parameter.");

            }

            double heightOffset = heightOffsetParam.AsDouble();

            // 7. Write to selected element
            using (Transaction tx = new Transaction(doc, "Set Elevation from Level"))
            {
                tx.Start();

                Parameter elevationParam = selectedElement.LookupParameter("Elevation from Level");
                if (elevationParam != null && elevationParam.StorageType == StorageType.Double)
                {
                    elevationParam.Set(heightOffset);
                }
                else
                {
                    TaskDialog.Show("Error", "Selected element does not have a valid 'Elevation from Level' parameter.");
                    tx.RollBack();

                }

                tx.Commit();
            }

            TaskDialog.Show("Success", "Ceiling height successfully assigned to the element!");

        }
        
    }
}

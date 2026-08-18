using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.Commands.Rooms
{
    public static class RaycastUtil
    {
        /// <summary>
        /// ReferenceIntersector requires a 3D view. Use an existing non-template 3D view,
        /// or create one (optionally). This returns a usable 3D view.
        /// </summary>
        public static View3D GetOrCreate3DView(Document doc, bool createIfMissing = true)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            var v3 = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate);

            if (v3 != null)
                return v3;

            if (!createIfMissing)
                throw new InvalidOperationException("No non-template 3D view found. Create one and retry.");

            var vft = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional);

            if (vft == null)
                throw new InvalidOperationException("No ViewFamilyType for 3D views found.");

            // Must be created inside an open transaction in caller.
            var created = View3D.CreateIsometric(doc, vft.Id);
            created.Name = $"BA_Raycast_{Guid.NewGuid():N}".Substring(0, 16);
            return created;
        }

        /// <summary>
        /// Finds nearest element along a ray. Category is used as a hint (we expand some categories).
        /// Returns null if nothing hit.
        /// </summary>
        public static Element FindNearestByCategory(
            Document doc,
            View3D view3d,
            XYZ origin,
            XYZ direction,
            BuiltInCategory bic,
            double maxDistFeet)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (view3d == null) throw new ArgumentNullException(nameof(view3d));
            if (origin == null) throw new ArgumentNullException(nameof(origin));
            if (direction == null) throw new ArgumentNullException(nameof(direction));

            direction = direction.Normalize();

            // Build filter (expanded for real-world floor/ceiling cases)
            ElementFilter filter;

            if (bic == BuiltInCategory.OST_Floors)
            {
                filter = new LogicalOrFilter(new List<ElementFilter>
                {
                    new ElementCategoryFilter(BuiltInCategory.OST_Floors),
                    new ElementCategoryFilter(BuiltInCategory.OST_StructuralFoundation),
                    new ElementCategoryFilter(BuiltInCategory.OST_Roofs),
                    new ElementCategoryFilter(BuiltInCategory.OST_Parts) // floors split into parts
                });
            }
            else if (bic == BuiltInCategory.OST_Ceilings)
            {
                filter = new LogicalOrFilter(new List<ElementFilter>
                {
                    new ElementCategoryFilter(BuiltInCategory.OST_Ceilings),
                    new ElementCategoryFilter(BuiltInCategory.OST_Roofs),
                    new ElementCategoryFilter(BuiltInCategory.OST_Parts) // ceilings split into parts
                });
            }
            else
            {
                filter = new ElementCategoryFilter(bic);
            }

            // Create intersector ALWAYS
            var intersector = new ReferenceIntersector(filter, FindReferenceTarget.Element, view3d)
            {
                // Important for BIM workflows: hit stuff in links
                FindReferencesInRevitLinks = true
            };

            var rwc = intersector.FindNearest(origin, direction);
            if (rwc == null) return null;

            if (rwc.Proximity > maxDistFeet) return null;

            var r = rwc.GetReference();
            if (r == null) return null;

            // If hit is in a LINK, ElementId here is the RevitLinkInstance id.
            // We'll resolve to the linked element if possible.
            var hitElem = doc.GetElement(r.ElementId);
            if (hitElem == null) return null;

            if (hitElem is RevitLinkInstance linkInst && r.LinkedElementId != ElementId.InvalidElementId)
            {
                var linkDoc = linkInst.GetLinkDocument();
                if (linkDoc == null) return hitElem; // fallback

                var linkedElem = linkDoc.GetElement(r.LinkedElementId);
                return linkedElem ?? hitElem;
            }

            return hitElem;
        }

        /// <summary>
        /// Generates sample points inside room using bounding box candidates + IsPointInRoom test.
        /// Z is set to a mid-height plane of the room's bbox.
        /// </summary>
        public static List<XYZ> GetSamplePointsInRoom(Room room, int maxPoints = 9, double xyInsetFeet = 0.5)
        {
            var pts = new List<XYZ>();
            var bb = room?.get_BoundingBox(null);
            if (bb == null) return pts;

            double z = (bb.Min.Z + bb.Max.Z) * 0.5;

            var c = new XYZ((bb.Min.X + bb.Max.X) * 0.5, (bb.Min.Y + bb.Max.Y) * 0.5, z);

            var candidates = new List<XYZ>
            {
                c,
                new XYZ(c.X + xyInsetFeet, c.Y, z),
                new XYZ(c.X - xyInsetFeet, c.Y, z),
                new XYZ(c.X, c.Y + xyInsetFeet, z),
                new XYZ(c.X, c.Y - xyInsetFeet, z),

                new XYZ(c.X + xyInsetFeet, c.Y + xyInsetFeet, z),
                new XYZ(c.X + xyInsetFeet, c.Y - xyInsetFeet, z),
                new XYZ(c.X - xyInsetFeet, c.Y + xyInsetFeet, z),
                new XYZ(c.X - xyInsetFeet, c.Y - xyInsetFeet, z),
            };

            foreach (var p in candidates)
            {
                if (pts.Count >= maxPoints) break;

                try
                {
                    if (room.IsPointInRoom(p))
                        pts.Add(p);
                }
                catch
                {
                    // ignore
                }
            }

            return pts;
        }
    }
}
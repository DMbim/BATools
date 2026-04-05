using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BA.Keyplan
{
    public static class KeyplanSheetPlacementService
    {
        public static Viewport PlaceOrReplaceKeyplanViewport(
            Document doc,
            ViewSheet sheet,
            View viewToPlace,
            string generatedViewPrefix,
            bool deleteOldKeyplanViewport)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (sheet == null) throw new ArgumentNullException(nameof(sheet));
            if (viewToPlace == null) throw new ArgumentNullException(nameof(viewToPlace));

            if (deleteOldKeyplanViewport)
            {
                IList<ElementId> oldViewportIds = FindExistingKeyplanViewportIds(doc, sheet, generatedViewPrefix);
                if (oldViewportIds.Count > 0)
                {
                    doc.Delete(oldViewportIds);
                }
            }

            Viewport existingViewport = FindViewportForView(doc, sheet, viewToPlace.Id);
            if (existingViewport != null)
            {
                return existingViewport;
            }

            if (!Viewport.CanAddViewToSheet(doc, sheet.Id, viewToPlace.Id))
            {
                throw new InvalidOperationException(
                    $"View '{viewToPlace.Name}' cannot be added to sheet '{sheet.SheetNumber}'.");
            }

            // temporary placement, final position will be corrected afterwards
            XYZ tempPoint = XYZ.Zero;
            Viewport vp = Viewport.Create(doc, sheet.Id, viewToPlace.Id, tempPoint);
            return vp;
        }

        public static bool MoveViewportToTitleBlockAnchor(
            Document doc,
            ViewSheet sheet,
            Viewport viewport,
            double offsetFromRightFeet,
            double offsetFromTopFeet)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (sheet == null) throw new ArgumentNullException(nameof(sheet));
            if (viewport == null) throw new ArgumentNullException(nameof(viewport));

            FamilyInstance titleBlock = KeyplanTitleBlockUtils.GetFirstTitleBlockOnSheet(doc, sheet);
            if (titleBlock == null)
                return false;

            BoundingBoxXYZ bb = titleBlock.get_BoundingBox(sheet);
            if (bb == null)
                return false;

            Outline vpOutline = viewport.GetBoxOutline();
            if (vpOutline == null)
                return false;

            double vpWidth = vpOutline.MaximumPoint.X - vpOutline.MinimumPoint.X;
            double vpHeight = vpOutline.MaximumPoint.Y - vpOutline.MinimumPoint.Y;

            XYZ desiredTopRight = new XYZ(
                bb.Max.X - offsetFromRightFeet,
                bb.Max.Y - offsetFromTopFeet,
                0.0);

            XYZ desiredCenter = new XYZ(
                desiredTopRight.X - (vpWidth / 2.0),
                desiredTopRight.Y - (vpHeight / 2.0),
                0.0);

            viewport.SetBoxCenter(desiredCenter);
            return true;
        }

        private static IList<ElementId> FindExistingKeyplanViewportIds(
            Document doc,
            ViewSheet sheet,
            string generatedViewPrefix)
        {
            List<ElementId> ids = new List<ElementId>();
            if (sheet == null) return ids;

            foreach (ElementId vpId in sheet.GetAllViewports())
            {
                Viewport vp = doc.GetElement(vpId) as Viewport;
                if (vp == null) continue;

                View v = doc.GetElement(vp.ViewId) as View;
                if (v == null) continue;

                if (v.Name.StartsWith(generatedViewPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    ids.Add(vp.Id);
                }
            }

            return ids;
        }

        private static Viewport FindViewportForView(Document doc, ViewSheet sheet, ElementId viewId)
        {
            foreach (ElementId vpId in sheet.GetAllViewports())
            {
                Viewport vp = doc.GetElement(vpId) as Viewport;
                if (vp == null) continue;

                if (vp.ViewId == viewId)
                    return vp;
            }

            return null;
        }
    }
}
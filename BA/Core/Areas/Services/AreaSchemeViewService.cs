using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BA.Core.AreaSchemes.Constants;
using BA.Core.AreaSchemes.Models;
using ViewStatus = BA.Core.AreaSchemes.Models.ViewStatus;

namespace BA.Core.AreaSchemes.Services
{
    public static class AreaSchemeViewService
    {
        /// <summary>
        /// Returns all levels in the document, ordered by elevation.
        /// </summary>
        public static IReadOnlyList<Level> GetLevels(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();
        }

        /// <summary>
        /// Finds an AreaScheme by name. Returns null if not found.
        /// </summary>
        public static AreaScheme? FindAreaScheme(Document doc, string schemeName)
        {
            var code = ExtractCode(schemeName);

            return new FilteredElementCollector(doc)
                .OfClass(typeof(AreaScheme))
                .Cast<AreaScheme>()
                .FirstOrDefault(s => ExtractCode(s.Name) == code);
        }

        private static string ExtractCode(string schemeName)
        {
            // Extract the uppercase letters between the last ( and any closing bracket/char
            // Handles: "(LA)", "(NLA)", "(NLA(" — we just want the letters
            var start = schemeName.LastIndexOf('(');
            if (start < 0) return schemeName;

            var inner = schemeName.Substring(start + 1);
            // Take only letters until we hit a non-letter
            var code = new string(inner.TakeWhile(char.IsLetter).ToArray()).Trim().ToUpperInvariant();
            return string.IsNullOrEmpty(code) ? schemeName : code;
        }

        /// <summary>
        /// Finds or creates an AreaScheme by name.
        /// Must be called inside an open Transaction.
        /// </summary>
        public static AreaScheme EnsureAreaScheme(Document doc, string schemeName)
        {
            var existing = FindAreaScheme(doc, schemeName);
            if (existing != null)
                return existing;

            throw new InvalidOperationException(
                $"Area Scheme '{schemeName}' not found in the document.\n\n" +
                $"Please create it manually in Revit:\n" +
                $"Architecture tab → Room & Area → Area → Area Plan → " +
                $"select or create the scheme type '{schemeName}'.");
        }

        /// <summary>
        /// Finds an existing Area Plan view for the given level and scheme name.
        /// Returns null if not found.
        /// </summary>
        public static ViewPlan? FindAreaPlanView(
            Document doc,
            Level level,
            string schemeName)
        {
            var scheme = FindAreaScheme(doc, schemeName);
            if (scheme == null) return null;

            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .FirstOrDefault(v =>
                    v.ViewType == ViewType.AreaPlan &&
                    v.GenLevel?.Id == level.Id &&
                    v.AreaScheme?.Id == scheme.Id);
        }

        /// <summary>
        /// Creates a new Area Plan view for the given level and scheme.
        /// Must be called inside an open Transaction.
        /// </summary>
        public static ViewPlan CreateAreaPlanView(
            Document doc,
            Level level,
            string schemeName)
        {
            var scheme = EnsureAreaScheme(doc, schemeName);

            var viewFamilyType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(vft =>
                    vft.ViewFamily == ViewFamily.AreaPlan);

            if (viewFamilyType == null)
                throw new InvalidOperationException(
                    "No Area Plan view family type found in document.");

            var view = ViewPlan.CreateAreaPlan(doc, scheme.Id, level.Id);
            view.Name = $"{schemeName} - {level.Name}";

            return view;
        }

        /// <summary>
        /// Finds or creates an Area Plan view for the given level and scheme.
        /// Returns the view and whether it was just created.
        /// Must be called inside an open Transaction.
        /// </summary>
        public static (ViewPlan view, bool wasCreated) EnsureAreaPlanView(
            Document doc,
            Level level,
            string schemeName)
        {
            var existing = FindAreaPlanView(doc, level, schemeName);
            if (existing != null)
                return (existing, false);

            var created = CreateAreaPlanView(doc, level, schemeName);
            return (created, true);
        }

        /// <summary>
        /// Gets the state of an Area Plan view for a given level and scheme.
        /// </summary>
        public static AreaLevelState GetLevelState(
            Document doc,
            Level level,
            AreaSchemeDefinition definition)
        {
            var view = FindAreaPlanView(doc, level, definition.SchemeName);

            if (view == null)
            {
                return new AreaLevelState
                {
                    Definition = definition,
                    Level = level,
                    ViewStatus = ViewStatus.Missing,
                    ViewId = null,
                    AreaM2 = 0,
                    AreaCount = 0
                };
            }

            var areas = GetAreasInView(doc, view);
            double totalM2 = areas.Sum(a =>
                UnitUtils.ConvertFromInternalUnits(a.Area, UnitTypeId.SquareMeters));

            return new AreaLevelState
            {
                Definition = definition,
                Level = level,
                ViewStatus = areas.Any()
                    ? ViewStatus.ExistsWithAreas
                    : ViewStatus.ExistsEmpty,
                ViewId = view.Id,
                AreaM2 = totalM2,
                AreaCount = areas.Count
            };
        }

        /// <summary>
        /// Returns all placed Area elements in a given view.
        /// </summary>
        public static IReadOnlyList<Area> GetAreasInView(Document doc, ViewPlan view)
        {
            return new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(Area))
                .Cast<Area>()
                .Where(a => a.Area > 0)
                .ToList();
        }

        /// <summary>
        /// Activates a view in the Revit UI.
        /// Must be called from the Revit API thread.
        /// </summary>
        public static void ActivateView(Autodesk.Revit.UI.UIDocument uidoc, ViewPlan view)
        {
            uidoc.ActiveView = view;
        }
    }
}
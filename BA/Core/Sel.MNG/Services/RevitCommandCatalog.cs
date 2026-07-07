using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.UI;
using BATools.SelectionManager.Actions;
using BATools.SelectionManager.Models;

namespace BATools.SelectionManager.Services
{
    public static class RevitCommandCatalog
    {
        // Lazy — evaluated on first access, not at static class initialization time
        private static IReadOnlyList<RevitCommandEntry>? _all;
        public static IReadOnlyList<RevitCommandEntry> All =>
            _all ??= BuildCatalog();

        private static IReadOnlyList<RevitCommandEntry> BuildCatalog()
        {
            var entries = new List<(PostableCommand cmd, string name, string cat)>
        {
            // ── Architecture ────────────────────────────────────────────
            new(PostableCommand.Wall,             "Wall",               "Architecture"),
            new(PostableCommand.StructuralWall,   "Structural Wall",    "Architecture"),
            new(PostableCommand.Door,             "Door",               "Architecture"),
            new(PostableCommand.Window,           "Window",             "Architecture"),
            new(PostableCommand.PlaceAComponent,        "Place Component",    "Architecture"),
            new(PostableCommand.ArchitecturalFloor,            "Floor",              "Architecture"),
            new(PostableCommand.RoofByFace,             "Roof by Footprint",  "Architecture"),
            new(PostableCommand.RoofByExtrusion,        "Roof by Extrusion",  "Architecture"),
            new(PostableCommand.AutomaticCeiling,          "Ceiling",            "Architecture"),
            new(PostableCommand.Stair,            "Stair",              "Architecture"),
            new(PostableCommand.Ramp,             "Ramp",               "Architecture"),
            new(PostableCommand.Railing,          "Railing",            "Architecture"),
            new(PostableCommand.Room,             "Room",               "Architecture"),
            new(PostableCommand.RoomTag,          "Room Tag",           "Architecture"),
            new(PostableCommand.Area,             "Area",               "Architecture"),
            new(PostableCommand.AreaTag,          "Area Tag",           "Architecture"),
            new(PostableCommand.AreaBoundary, "Area Boundary Line", "Architecture"),
            new(PostableCommand.CurtainGrid,        "Curtain Grid",       "Architecture"),
            new(PostableCommand.CurtainWallMullion,       "Curtain Panel",      "Architecture"),
            new(PostableCommand.CurtainSystemByFace,    "Curtain System",     "Architecture"),
            new(PostableCommand.OpeningByFace,        "Curtain Wall",       "Architecture"),
            new(PostableCommand.ShaftOpening,        "Opening by Host",    "Architecture"),
            new(PostableCommand.WallOpening,      "Component (Family)", "Architecture"),
            new(PostableCommand.VerticalOpening,     "Opening by Host",    "Architecture"),
            new(PostableCommand.DormerOpening,   "Opening by Host",    "Architecture"),
            new(PostableCommand.SetWorkPlane, "Set Work Plane", "Architecture"),


            // ── Structure ───────────────────────────────────────────────
            new(PostableCommand.StructuralColumn, "Structural Column",  "Structure"),
            new(PostableCommand.Beam,             "Beam",               "Structure"),
            new(PostableCommand.Brace,            "Brace",              "Structure"),

            // ── Annotation ──────────────────────────────────────────────
            new(PostableCommand.TagByCategory,               "Tag by Category",    "Annotation"),
            new(PostableCommand.TagAllNotTagged,            "Tag All",            "Annotation"),
            new(PostableCommand.MultiCategoryTag,        "Tag by Multi-Category","Annotation"),
            new(PostableCommand.MaterialTag,          "Material Tag",          "Annotation"),
            new(PostableCommand.SpaceTag,            "Space Tag",              "Annotation"),
            new(PostableCommand.Text,          "Text Note",          "Annotation"),
            new(PostableCommand.AngularDimension,  "Angular Dimension",  "Annotation"),
            new(PostableCommand.DiameterDimension, "Diameter Dimension", "Annotation"),
            new(PostableCommand.SpotElevation,     "Spot Elevation",     "Annotation"),
            new(PostableCommand.SpotCoordinate,    "Spot Coordinate",    "Annotation"),
            new(PostableCommand.SpotSlope,         "Spot Slope",         "Annotation"),
            new(PostableCommand.ViewReference,     "View Reference",     "Annotation"),
            new(PostableCommand.StairTreadOrRiserNumber,       "Tread Number",       "Annotation"),
            new(PostableCommand.KeynoteLegend,             "Keynote Legend",     "Annotation"),
            new(PostableCommand.ColorFillLegend,          "Color Fill Legend",  "Annotation"),
            new(PostableCommand.KeynotingSettings,                "Keynote Settings",        "Annotation"),
            new(PostableCommand.ElementKeynote,              "Keynote by Element", "Annotation"),
            new(PostableCommand.MaterialKeynote,          "Material Keynote",     "Annotation"),
            new(PostableCommand.UserKeynote,           "User Keynote",           "Annotation"),
            new(PostableCommand.Grid,              "Grid",               "Annotation"),
            new(PostableCommand.Level,             "Level",              "Annotation"),
            new(PostableCommand.RevisionCloud,     "Revision Cloud",     "Annotation"),
            new(PostableCommand.FilledRegion,      "Filled Region",      "Annotation"),
            new(PostableCommand.Symbol,            "Legend Component",   "Annotation"),
            new(PostableCommand.RepeatComponent,   "Repeating Detail",   "Annotation"),
            new(PostableCommand.DetailComponent,   "Detail Component",   "Annotation"),
            new(PostableCommand.RepeatingDetailComponent,      "Repeating Detail Component",   "Annotation"),
            new(PostableCommand.DetailLine,        "Detail Line",        "Annotation"),

            // ── Modify ──────────────────────────────────────────────────
            new(PostableCommand.Move,   "Move",          "Modify"),
            new(PostableCommand.Copy,   "Copy",          "Modify"),
            new(PostableCommand.Rotate, "Rotate",        "Modify"),
            new(PostableCommand.MirrorDrawAxis, "Mirror",        "Modify"),
            new(PostableCommand.MirrorPickAxis, "Mirror - Pick Axis", "Modify"),
            new(PostableCommand.CutGeometry, "Cut Geometry", "Modify"),
            new(PostableCommand.JoinGeometry, "Join Geometry", "Modify"),
            new(PostableCommand.UnjoinGeometry, "Unjoin Geometry", "Modify"),
            new(PostableCommand.UncutGeometry, "Uncut Geometry", "Modify"),
            new(PostableCommand.SwitchJoinOrder, "Switch Join Order", "Modify"),
            new(PostableCommand.Align,  "Align",         "Modify"),
            new(PostableCommand.JoinOrUnjoinRoof, "Join/Unjoin Roof", "Modify"),
            new(PostableCommand.WallJoins, "Wall Joins", "Modify"),
            new(PostableCommand.BeamOrColumnJoins, "Beam/Column Joins", "Modify"),
            new(PostableCommand.SplitFace, "Split Face", "Modify"),
            new(PostableCommand.Paint, "Paint", "Modify"),
            new(PostableCommand.RemovePaint, "Remove Paint", "Modify"),
            new(PostableCommand.PasteFromClipboard, "Paste", "Modify"),
            new(PostableCommand.TrimOrExtendMultipleElements, "Trim/Extend Multiple", "Modify"),
            new(PostableCommand.SplitElement, "Split Element", "Modify"),
            new(PostableCommand.Scale, "Scale", "Modify"),
            new(PostableCommand.TrimOrExtendSingleElement, "Trim/Extend Single", "Modify"),
            new(PostableCommand.TrimOrExtendToCorner, "Trim/Extend to Corner", "Modify"),
            new(PostableCommand.Pin, "Pin", "Modify"),
            new(PostableCommand.Unpin, "Unpin", "Modify"),
            new(PostableCommand.Linework, "Linework", "Modify"),
            new(PostableCommand.AlignedDimension, "Aligned Dimension", "Modify"),
            new(PostableCommand.RadialDimension, "Radial Dimension", "Modify"),
            new(PostableCommand.RadialDimensionTypes, "Radial Dimension Types", "Modify"),
            new(PostableCommand.AlignedToPickedLevel, "Aligned to Picked Level", "Modify"),
            new(PostableCommand.AlignedToSamePlace, "Aligned to Same Place", "Modify"),
            new(PostableCommand.AlignedToSelectedLevels, "Aligned to Selected Levels", "Modify"),
            new(PostableCommand.AlignedToSelectedViews, "Aligned to Selected Views", "Modify"),
            new(PostableCommand.CreateGroup, "Create Group", "Modify"),
            new(PostableCommand.CreateAssembly, "Create Assembly", "Modify"),
            new(PostableCommand.PickAPlane, "Pick a Plane", "Modify"),
            new(PostableCommand.PickToEdit, "Pick to Edit", "Modify"),
            new(PostableCommand.Array,  "Array",         "Modify"),
            new(PostableCommand.Offset, "Offset",        "Modify"),
            new(PostableCommand.Delete, "Delete",        "Modify"),

            // ── Manage ──────────────────────────────────────────────────
            new(PostableCommand.Materials, "Materials", "Manage"),
            new(PostableCommand.ObjectStyles, "Object Styles", "Manage"),
            new(PostableCommand.Snaps, "Snaps", "Manage"),
            new(PostableCommand.ProjectBrowser, "Project Browser", "Manage"),
            new(PostableCommand.ProjectInformation, "Project Information", "Manage"),
            new(PostableCommand.ProjectParameters, "Project Parameters", "Manage"),
            new(PostableCommand.SharedParameters, "Shared Parameters", "Manage"),
            new(PostableCommand.GlobalParameters, "Global Parameters", "Manage"),
            new(PostableCommand.TransferProjectStandards, "Transfer Project Standards", "Manage"),
            new(PostableCommand.PurgeUnused, "Purge Unused", "Manage"),
            new(PostableCommand.ProjectUnits, "Project Units", "Manage"),
            new(PostableCommand.LinePatterns, "Line Patterns", "Manage"),
            new(PostableCommand.LineStyles, "Line Styles", "Manage"),
            new(PostableCommand.LineWeights, "Line Weights", "Manage"),
            new(PostableCommand.SheetIssuesOrRevisions, "Sheet Issues/Revisions", "Manage"),
            new(PostableCommand.FillPatterns, "Fill Patterns", "Manage"),
            new(PostableCommand.Arrowheads, "Arrowheads", "Manage"),
            new(PostableCommand.SectionTags, "Section Tags", "Manage"),
            new(PostableCommand.ElevationTags, "Elevation Tags", "Manage"),
            new(PostableCommand.MaterialAssets, "Material Assets", "Manage"),
            new(PostableCommand.SunSettings, "Sun Settings", "Manage"),
            new(PostableCommand.Location, "Location", "Manage"),
            new(PostableCommand.AcquireCoordinates, "Acquire Coordinates", "Manage"),
            new(PostableCommand.PublishCoordinates, "Publish Coordinates", "Manage"),
            new(PostableCommand.ResetSharedCoordinates, "Reset Shared Coordinates", "Manage"),
            new(PostableCommand.SpecifyCoordinatesAtPoint, "Specify Coordinates at Point", "Manage"),
            new(PostableCommand.ReportSharedCoordinates, "Report Shared Coordinates", "Manage"),
            new(PostableCommand.RelocateProject, "Relocate Project", "Manage"),
            new(PostableCommand.RotateProjectNorth, "Rotate Project North", "Manage"),
            new(PostableCommand.RotateTrueNorth, "Rotate True North", "Manage"),
            new(PostableCommand.DesignOptions, "Design Options", "Manage"),
            new(PostableCommand.StatusBarDesignOptions, "Status Bar Design Options", "Manage"),
            new(PostableCommand.Worksets, "Worksets", "Manage"),
            new(PostableCommand.ManageLinks, "Manage Links", "Manage"),
            new(PostableCommand.Phases, "Phases", "Manage"),
            new(PostableCommand.SelectById, "Select by ID", "Manage"),
            new(PostableCommand.ReviewWarnings, "Review Warnings", "Manage"),
            new(PostableCommand.ShowWarningsInViews, "Show Warnings in Views", "Manage"),
            new(PostableCommand.DynamoPlayer, "Dynamo Player", "Manage"),
            new(PostableCommand.Dynamo, "Dynamo", "Manage"),

            // ── Massing and Site ────────────────────────────────────────────────────
            new(PostableCommand.InPlaceMass, "In-Place Mass", "Massing & Site"),
            new(PostableCommand.PlaceMass, "Mass", "Massing & Site"),
            new(PostableCommand.WallByFaceWall, "Wall by Face", "Massing & Site"),
            new(PostableCommand.FloorByFaceFloor, "Floor by Face", "Massing & Site"),
            new(PostableCommand.Toposolid, "Toposolid", "Massing & Site"),
            new(PostableCommand.ToposolidByFace, "Toposolid by Face", "Massing & Site"),
            new(PostableCommand.ToposolidSmoothShading, "Toposolid Smooth Shading", "Massing & Site"),
            new(PostableCommand.TopographyCutVoidStability, "Topography Cut Void Stability", "Massing & Site"),
            new(PostableCommand.LinkTopography, "Link Topography", "Massing & Site"),
            new(PostableCommand.CreateFromImport, "Create from Import", "Massing & Site"),
            new(PostableCommand.SiteComponent, "Site Component", "Massing & Site"),
            new(PostableCommand.ParkingComponent, "Parking Component", "Massing & Site"),
            new(PostableCommand.PropertyLine, "Property Line", "Massing & Site"),
            new(PostableCommand.PropertyLineData, "Property Line Data", "Massing & Site"),
            new(PostableCommand.LabelContours, "Label Contours", "Massing & Site"),
            new(PostableCommand.GradedRegion, "Graded Region", "Massing & Site"),

            // ── View ────────────────────────────────────────────────────
            new(PostableCommand.Camera,       "3D Camera",     "View"),
            new(PostableCommand.Section,      "Section",       "View"),
            new(PostableCommand.ApplyTemplatePropertiesToCurrentView, "Apply Template Properties to Current View", "View"),
            new(PostableCommand.ManageViewTemplates, "Manage View Templates", "View"),
            new(PostableCommand.CreateTemplateFromCurrentView, "Create Template from Current View", "View"),
            new(PostableCommand.VisibilityOrGraphics, "Visibility/Graphics", "View"),
            new(PostableCommand.Filters, "Filters", "View"),
            new(PostableCommand.ThinLines, "Thin Lines", "View"),
            new(PostableCommand.ShowHiddenLinesByElement, "Show Hidden Lines", "View"),
            new(PostableCommand.RemoveHiddenLinesByElement, "Remove Hidden Lines", "View"),
            new(PostableCommand.CutProfile, "Cut Profile", "View"),
            new(PostableCommand.Render, "Render", "View"),
            new(PostableCommand.RenderGallery, "Render Gallery", "View"),
            new(PostableCommand.PlanRegion, "Plan Region", "View"),
            new(PostableCommand.AreaPlan, "Area Plan", "View"),
            new(PostableCommand.FloorPlan, "Floor Plan", "View"),
            new(PostableCommand.ReflectedCeilingPlan, "Reflected Ceiling Plan", "View"),
            new(PostableCommand.StructuralPlan, "Structural Plan", "View"),
            new(PostableCommand.BuildingElevation, "Building Elevation", "View"),
            new(PostableCommand.Callout, "Callout", "View"),
            new(PostableCommand.DraftingView, "Drafting View", "View"),
            new(PostableCommand.DuplicateAsDependent, "Duplicate as Dependent", "View"),
            new(PostableCommand.DuplicateView, "Duplicate View", "View"),
            new(PostableCommand.DuplicateWithDetailing, "Duplicate with Detailing", "View"),
            new(PostableCommand.Legend, "Legend", "View"),
            new(PostableCommand.LegendComponent, "Legend Component", "View"),
            new(PostableCommand.ScheduleOrQuantities, "Schedule", "View"),
            new(PostableCommand.GraphicalColumnSchedule,  "Graphical Column Schedule", "View"),
            new(PostableCommand.MaterialTakeoff, "Material Takeoff", "View"),
            new(PostableCommand.SheetList, "SheetList", "View"),
            new(PostableCommand.NoteBlock, "Note Block", "View"),
            new(PostableCommand.ViewList, "View List", "View"),
            new(PostableCommand.ScopeBox, "Scope Box", "View"),
            new(PostableCommand.NewSheet, "New Sheet", "View"),
            new(PostableCommand.TabViews, "Tab Views", "View"),
            new(PostableCommand.TileViews, "Tile Views", "View"),
            new(PostableCommand.GuideGrid, "Guide Grid", "View"),
            new(PostableCommand.Matchline, "Matchline", "View"),
            new(PostableCommand.ViewReference, "View Reference", "View"),
            new(PostableCommand.CloseInactiveViews, "Close Inactive Views", "View"),


            // ── Edit ────────────────────────────────────────────────────
            new(PostableCommand.Undo,      "Undo",       "Edit"),
            new(PostableCommand.Redo,      "Redo",       "Edit"),
            new(PostableCommand.Save,      "Save",       "Edit"),
            new(PostableCommand.Print,     "Print",      "Edit"),
            new(PostableCommand.PrintPreview, "Print Preview", "Edit"),
            new(PostableCommand.PrintSetup, "Print Setup", "Edit"),
            new(PostableCommand.BatchPrint, "Batch Print", "Edit"),
            new(PostableCommand.ExportCADFormatsDWG, "Export CAD (DWG)", "Edit"),
            new(PostableCommand.ExportIFC, "Export IFC", "Edit"),
            new(PostableCommand.ExportPDF, "Export PDF", "Edit"),

        };

            // Build catalog — skip invalid commands and deduplicate by PostableCommand value.
            // Duplicates in the entries list (same command in multiple categories) are reduced
            // to the first occurrence. This prevents ToDictionary from throwing on ById access.
            var seen = new HashSet<PostableCommand>();
            var result = new List<RevitCommandEntry>();

            foreach (var (cmd, name, cat) in entries)
            {
                if (!seen.Add(cmd))
                {
                    Debug.WriteLine(
                        $"[RevitCommandCatalog] Duplicate skipped: {cmd} ({name})");
                    continue;
                }

                try
                {
                    var cmdId = RevitCommandId.LookupPostableCommandId(cmd);
                    if (cmdId != null)
                        result.Add(new RevitCommandEntry(cmd, name, cat));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        $"[RevitCommandCatalog] Skipping {cmd}: {ex.Message}");
                }
            }

            return result;
        }

        private static Dictionary<string, RevitCommandEntry>? _byId;
        private static Dictionary<string, RevitCommandEntry> ById
        {
            get
            {
                if (_byId != null) return _byId;
                _byId = new Dictionary<string, RevitCommandEntry>();
                foreach (var entry in All)
                {
                    string key = ActionIdFor(entry.Command);
                    if (!_byId.TryAdd(key, entry))
                        Debug.WriteLine($"[RevitCommandCatalog] ById duplicate key: {key}");
                }
                return _byId;
            }
        }

        public static string ActionIdFor(PostableCommand cmd)
            => $"revit_{(int)cmd}";

        public static IQuickAction? GetActionById(string actionId)
        {
            if (!ById.TryGetValue(actionId, out var entry)) return null;
            return new RevitPostableAction(entry.Command, entry.DisplayName, entry.Category);
        }
    }
}
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace BA.UI.KeyplanGrid
{
    public sealed class KeyplanGridViewModel : INotifyPropertyChanged
    {
        // -------------------------------------------------------------------------
        // Backing fields — settings
        // -------------------------------------------------------------------------

        private string _sourceViewName = "1_Gross Area (GFA)";
        private string _targetDraftingViewName = "X.NP_Keyplan_Drafting";
        private string _filledRegionTypeName = "BA_Keyplan_Cell";

        private int _xDivisionCount = 4;
        private int _yDivisionCount = 4;

        private bool _clearTargetViewFirst = false;
        private bool _copySourceViewSpecificElements = false;
        private bool _drawGridLines = true;
        private bool _createFilledRegions = true;
        private bool _drawOutline = true;
        private bool _useOutlineAsPrimaryFill = true;

        private KeyplanCellFillMode _fillMode = KeyplanCellFillMode.FullCellIfOccupied;
        private double _minimumOccupancyRatio = 0.05;
        private double _globalScaleFactor = 1.0 / 300.0;

        // -------------------------------------------------------------------------
        // Backing fields — runtime state
        // -------------------------------------------------------------------------

        private CurveLoop _sourceOuterLoop;
        private KeyplanGridPreviewData _previewData = new KeyplanGridPreviewData();
        private string _statusText = "Ready.";

        // Last canvas dimensions passed by the window — used for internal rebuilds
        // so polygon coordinates always match the actual canvas size.
        private double _cachedCanvasWidth = 860.0;
        private double _cachedCanvasHeight = 720.0;
        private double _cachedPadding = 24.0;

        // Single-selection state (used by nudge / delete).
        private string _selectedSplitId;
        private AxisOrientation? _selectedSplitOrientation;

        private readonly Dictionary<string, CellEditState> _cellEdits =
            new Dictionary<string, CellEditState>(StringComparer.Ordinal);

        private readonly HashSet<string> _selectedCellKeys =
            new HashSet<string>(StringComparer.Ordinal);

        // -------------------------------------------------------------------------
        // Zone label session state
        // -------------------------------------------------------------------------

        private KeyplanZoneLabelSession _activeZoneSession;
        private KeyplanZoneLabelStyle _zoneLabelStyle = KeyplanZoneLabelStyle.Numeric;

        /// <summary>
        /// Last successful GenerationResult.  Populated after Generate().
        /// Required by the zone label session — holds centroids and UniqueIds.
        /// </summary>
        public GenerationResult LastGenerationResult { get; private set; }

        public KeyplanZoneLabelSession ActiveZoneSession
        {
            get => _activeZoneSession;
            private set
            {
                _activeZoneSession = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsZoneSessionActive));
                OnPropertyChanged(nameof(ZoneSessionStateLabel));
            }
        }

        public bool IsZoneSessionActive => _activeZoneSession != null;

        public string ZoneSessionStateLabel
        {
            get
            {
                if (_activeZoneSession == null)
                    return string.Empty;

                int count = _activeZoneSession.PickedKeysInOrder.Count;

                return count == 0
                    ? "Click regions in the order you want them labelled."
                    : $"{count} region(s) labelled. Click more, click a labelled region to remove it, or Commit.";
            }
        }

        public bool CanCommitZoneLabels => _activeZoneSession?.CanCommit ?? false;

        public KeyplanZoneLabelStyle ZoneLabelStyle
        {
            get => _zoneLabelStyle;
            set
            {
                if (_zoneLabelStyle == value) return;
                _zoneLabelStyle = value;
                OnPropertyChanged();

                // Live labels are derived on the fly via session.GetLabel(), so
                // changing the style just needs a preview rebuild to re-render them.
                if (_activeZoneSession != null)
                    RebuildPreview();
            }
        }

        public Array ZoneLabelStyleValues => Enum.GetValues(typeof(KeyplanZoneLabelStyle));

        // -------------------------------------------------------------------------
        // Split collections
        // -------------------------------------------------------------------------

        public ObservableCollection<KeyplanSplitLineItem> VerticalSplits { get; } =
            new ObservableCollection<KeyplanSplitLineItem>();

        public ObservableCollection<KeyplanSplitLineItem> HorizontalSplits { get; } =
            new ObservableCollection<KeyplanSplitLineItem>();

        // -------------------------------------------------------------------------
        // Bound properties — settings
        // -------------------------------------------------------------------------

        public string SourceViewName
        {
            get => _sourceViewName;
            set { _sourceViewName = value ?? ""; OnPropertyChanged(); }
        }

        public string TargetDraftingViewName
        {
            get => _targetDraftingViewName;
            set { _targetDraftingViewName = value ?? ""; OnPropertyChanged(); }
        }


        public string FilledRegionTypeName
        {
            get => _filledRegionTypeName;
            set { _filledRegionTypeName = value ?? ""; OnPropertyChanged(); }
        }

        public int XDivisionCount
        {
            get => _xDivisionCount;
            set
            {
                int v = Math.Max(1, value);
                if (_xDivisionCount == v) return;
                _xDivisionCount = v;
                RebuildSplitCollections();
                RebuildPreview();
                OnPropertyChanged();
            }
        }

        public int YDivisionCount
        {
            get => _yDivisionCount;
            set
            {
                int v = Math.Max(1, value);
                if (_yDivisionCount == v) return;
                _yDivisionCount = v;
                RebuildSplitCollections();
                RebuildPreview();
                OnPropertyChanged();
            }
        }

        public bool ClearTargetViewFirst
        {
            get => _clearTargetViewFirst;
            set { _clearTargetViewFirst = value; OnPropertyChanged(); }
        }

        public bool CopySourceViewSpecificElements
        {
            get => _copySourceViewSpecificElements;
            set { _copySourceViewSpecificElements = value; OnPropertyChanged(); }
        }

        public bool DrawGridLines
        {
            get => _drawGridLines;
            set { if (_drawGridLines == value) return; _drawGridLines = value; RebuildPreview(); OnPropertyChanged(); }
        }

        public bool CreateFilledRegions
        {
            get => _createFilledRegions;
            set { if (_createFilledRegions == value) return; _createFilledRegions = value; RebuildPreview(); OnPropertyChanged(); }
        }

        public bool DrawOutline
        {
            get => _drawOutline;
            set { if (_drawOutline == value) return; _drawOutline = value; RebuildPreview(); OnPropertyChanged(); }
        }

        public bool UseOutlineAsPrimaryFill
        {
            get => _useOutlineAsPrimaryFill;
            set { if (_useOutlineAsPrimaryFill == value) return; _useOutlineAsPrimaryFill = value; RebuildPreview(); OnPropertyChanged(); }
        }

        public KeyplanCellFillMode FillMode
        {
            get => _fillMode;
            set { if (_fillMode == value) return; _fillMode = value; RebuildPreview(); OnPropertyChanged(); }
        }

        public double MinimumOccupancyRatio
        {
            get => _minimumOccupancyRatio;
            set
            {
                double v = Math.Max(0.0, Math.Min(1.0, value));
                if (Math.Abs(_minimumOccupancyRatio - v) <= 1e-9) return;
                _minimumOccupancyRatio = v;
                RebuildPreview();
                OnPropertyChanged();
            }
        }

        public double GlobalScaleFactor
        {
            get => _globalScaleFactor;
            set
            {
                double v = value <= 0.0 ? 1.0 : value;
                if (Math.Abs(_globalScaleFactor - v) <= 1e-12) return;
                _globalScaleFactor = v;
                RebuildPreview();
                OnPropertyChanged();
                OnPropertyChanged(nameof(GlobalScaleFactorDisplay));
            }
        }

        public string GlobalScaleFactorDisplay
        {
            get
            {
                if (_globalScaleFactor <= 0.0)
                    return "1:1";

                double inverse = 1.0 / _globalScaleFactor;
                double rounded = Math.Round(inverse);

                return Math.Abs(inverse - rounded) < 1e-6
                    ? $"1:{rounded:0}"
                    : $"1:{inverse:0.##}";
            }
        }

        public Array FillModeValues => Enum.GetValues(typeof(KeyplanCellFillMode));

        // -------------------------------------------------------------------------
        // Bound properties — preview / status
        // -------------------------------------------------------------------------

        public KeyplanGridPreviewData PreviewData
        {
            get => _previewData;
            private set
            {
                _previewData = value ?? new KeyplanGridPreviewData();
                OnPropertyChanged();
            }
        }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value ?? ""; OnPropertyChanged(); }
        }

        public int SelectedCellCount => _selectedCellKeys.Count;
        public int SelectedVerticalSplitCount => VerticalSplits.Count(x => x.IsSelected);
        public int SelectedHorizontalSplitCount => HorizontalSplits.Count(x => x.IsSelected);

        // -------------------------------------------------------------------------
        // Factory
        // -------------------------------------------------------------------------

        public static KeyplanGridViewModel CreateDefault()
        {
            KeyplanGridViewModel vm = new KeyplanGridViewModel();
            vm.RebuildSplitCollections();
            return vm;
        }

        // -------------------------------------------------------------------------
        // Initialisation
        // -------------------------------------------------------------------------

        public void LoadInitialPreview(CurveLoop sourceOuterLoop)
        {
            _sourceOuterLoop = sourceOuterLoop ?? throw new ArgumentNullException(nameof(sourceOuterLoop));
            RebuildSplitCollections();
            RebuildPreview();
        }

        // -------------------------------------------------------------------------
        // Options snapshot
        // -------------------------------------------------------------------------

        public KeyplanGridOptions BuildOptions()
        {
            return new KeyplanGridOptions
            {
                SourceViewName = SourceViewName,
                TargetDraftingViewName = TargetDraftingViewName,
                FilledRegionTypeName = FilledRegionTypeName,
                ClearTargetViewFirst = ClearTargetViewFirst,
                CopySourceViewSpecificElements = CopySourceViewSpecificElements,
                DrawGridLines = DrawGridLines,
                CreateFilledRegions = CreateFilledRegions,
                DrawOutline = DrawOutline,
                UseOutlineAsPrimaryFill = UseOutlineAsPrimaryFill,
                FillMode = FillMode,
                MinimumOccupancyRatio = MinimumOccupancyRatio,
                GlobalScaleFactor = GlobalScaleFactor
            };
        }

        public IReadOnlyDictionary<string, CellEditState> GetCellEditsSnapshot()
        {
            return new Dictionary<string, CellEditState>(
                _cellEdits.ToDictionary(
                    x => x.Key,
                    x => new CellEditState
                    {
                        IsExcluded = x.Value?.IsExcluded ?? false,
                        MergeGroupId = x.Value?.MergeGroupId ?? string.Empty
                    }),
                StringComparer.Ordinal);
        }

        public CurveLoop GetSourceOuterLoop() => _sourceOuterLoop;

        public IReadOnlyList<KeyplanSplitLineItem> GetVerticalSplitSnapshot()
        {
            return VerticalSplits.Select(CloneSplit).ToList();
        }

        public IReadOnlyList<KeyplanSplitLineItem> GetHorizontalSplitSnapshot()
        {
            return HorizontalSplits.Select(CloneSplit).ToList();
        }

        // -------------------------------------------------------------------------
        // Preview rebuild
        // -------------------------------------------------------------------------

        public void RebuildPreview(
            double canvasWidth = 0.0,
            double canvasHeight = 0.0,
            double padding = 0.0)
        {
            if (canvasWidth > 0) _cachedCanvasWidth = canvasWidth;
            if (canvasHeight > 0) _cachedCanvasHeight = canvasHeight;
            if (padding > 0) _cachedPadding = padding;

            if (_sourceOuterLoop == null)
                return;

            try
            {
                double[] xBreaks = GetXNormalizedBreaks();
                double[] yBreaks = GetYNormalizedBreaks();

                IReadOnlyDictionary<string, string> committedLabels = BuildCommittedZoneLabelLookup();

                KeyplanGridPreviewData data = KeyplanGridPreviewBuilder.BuildPreview(
                    _sourceOuterLoop,
                    xBreaks,
                    yBreaks,
                    FillMode,
                    MinimumOccupancyRatio,
                    DrawOutline,
                    UseOutlineAsPrimaryFill,
                    DrawGridLines,
                    CreateFilledRegions,
                    GlobalScaleFactor,
                    _cachedCanvasWidth,
                    _cachedCanvasHeight,
                    _cachedPadding,
                    _cellEdits,
                    _selectedCellKeys,
                    VerticalSplits.Where(x => x.IsEnabled).ToList(),
                    HorizontalSplits.Where(x => x.IsEnabled).ToList(),
                    committedLabels);

                // Overlay zone pick roles/live labels from the active session (if any).
                // This runs after BuildPreview so live session labels take priority
                // over stale committed labels for the same cell.
                ApplyZoneSessionRoles(data);

                PreviewData = data;

                StatusText =
                    $"Preview updated. V splits: {VerticalSplits.Count}, " +
                    $"H splits: {HorizontalSplits.Count}, " +
                    $"Scale: {GlobalScaleFactor:0.############}, " +
                    $"Fill: {FillMode}, Selected cells: {SelectedCellCount}.";
            }
            catch (Exception ex)
            {
                StatusText = $"Preview error: {ex.Message}";
            }
        }

        private IReadOnlyDictionary<string, string> BuildCommittedZoneLabelLookup()
        {
            if (LastGenerationResult == null)
                return null;

            Dictionary<string, string> lookup = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (GeneratedElementRecord rec in LastGenerationResult.CreatedItems)
            {
                if (rec == null || string.IsNullOrWhiteSpace(rec.ZoneLabel))
                    continue;

                lookup[rec.StableKey] = rec.ZoneLabel;
            }

            return lookup.Count > 0 ? lookup : null;
        }

        // -------------------------------------------------------------------------
        // Breaks
        // -------------------------------------------------------------------------

        public double[] GetXNormalizedBreaks() =>
            BuildNormalizedBreaksFromFreeSplits(VerticalSplits);

        public double[] GetYNormalizedBreaks() =>
            BuildNormalizedBreaksFromFreeSplits(HorizontalSplits);

        // -------------------------------------------------------------------------
        // Split interaction
        // -------------------------------------------------------------------------

        public void MoveSplit(string splitId, AxisOrientation orientation, double normalized)
        {
            KeyplanSplitLineItem item = FindSplit(splitId, orientation);
            if (item == null) return;

            double snapped = normalized;
            if (_sourceOuterLoop != null)
            {
                snapped = KeyplanSplitSnapService.GetSnappedNormalized(
                    _sourceOuterLoop,
                    VerticalSplits,
                    HorizontalSplits,
                    orientation,
                    splitId,
                    normalized);
            }

            item.Normalized = snapped;
            RebuildPreview();
        }

        /// <summary>
        /// Selects a single split, optionally additive (Ctrl-click).
        /// Also updates the nudge/delete backing fields.
        /// </summary>
        public void SelectSplit(string id, AxisOrientation orientation, bool additive = false)
        {
            ObservableCollection<KeyplanSplitLineItem> list =
                orientation == AxisOrientation.Vertical ? VerticalSplits : HorizontalSplits;

            if (!additive)
            {
                foreach (KeyplanSplitLineItem s in VerticalSplits) s.IsSelected = false;
                foreach (KeyplanSplitLineItem s in HorizontalSplits) s.IsSelected = false;
            }

            KeyplanSplitLineItem target = FindSplit(id, orientation);
            if (target != null)
                target.IsSelected = !target.IsSelected || !additive;

            // Keep single-id state in sync for nudge / delete.
            _selectedSplitId = id;
            _selectedSplitOrientation = orientation;

            OnPropertyChanged(nameof(SelectedVerticalSplitCount));
            OnPropertyChanged(nameof(SelectedHorizontalSplitCount));
            RebuildPreview();
        }

        public void DeleteSelectedSplit()
        {
            if (string.IsNullOrWhiteSpace(_selectedSplitId) || !_selectedSplitOrientation.HasValue)
                return;

            ObservableCollection<KeyplanSplitLineItem> list =
                _selectedSplitOrientation.Value == AxisOrientation.Vertical
                    ? VerticalSplits
                    : HorizontalSplits;

            KeyplanSplitLineItem item = list.FirstOrDefault(x =>
                string.Equals(x.Id, _selectedSplitId, StringComparison.Ordinal));

            if (item != null)
                list.Remove(item);

            _selectedSplitId = null;
            _selectedSplitOrientation = null;

            OnPropertyChanged(nameof(SelectedVerticalSplitCount));
            OnPropertyChanged(nameof(SelectedHorizontalSplitCount));
            RebuildPreview();
        }

        public void NudgeSelectedSplit(double delta)
        {
            if (string.IsNullOrWhiteSpace(_selectedSplitId) || !_selectedSplitOrientation.HasValue)
                return;

            // Nudge ALL selected splits in the relevant list, not just the last clicked one.
            ObservableCollection<KeyplanSplitLineItem> list =
                _selectedSplitOrientation.Value == AxisOrientation.Vertical
                    ? VerticalSplits
                    : HorizontalSplits;

            foreach (KeyplanSplitLineItem split in list.Where(x => x.IsSelected).ToList())
                MoveSplit(split.Id, split.Orientation, split.Normalized + delta);
        }

        public void AddVerticalSplit()
        {
            VerticalSplits.Add(CreateSplit(AxisOrientation.Vertical,
                FindSuggestedPosition(VerticalSplits)));
            OnPropertyChanged(nameof(SelectedVerticalSplitCount));
            RebuildPreview();
        }

        public void AddHorizontalSplit()
        {
            HorizontalSplits.Add(CreateSplit(AxisOrientation.Horizontal,
                FindSuggestedPosition(HorizontalSplits)));
            OnPropertyChanged(nameof(SelectedHorizontalSplitCount));
            RebuildPreview();
        }

        public void RemoveSelectedVerticalSplits()
        {
            RemoveSelectedSplits(VerticalSplits);
            OnPropertyChanged(nameof(SelectedVerticalSplitCount));
            RebuildPreview();
        }

        public void RemoveSelectedHorizontalSplits()
        {
            RemoveSelectedSplits(HorizontalSplits);
            OnPropertyChanged(nameof(SelectedHorizontalSplitCount));
            RebuildPreview();
        }

        // -------------------------------------------------------------------------
        // Cell selection
        // -------------------------------------------------------------------------

        public void ToggleCellSelection(string cellKey, bool additiveSelection)
        {
            if (string.IsNullOrWhiteSpace(cellKey)) return;

            if (!additiveSelection)
                _selectedCellKeys.Clear();

            if (_selectedCellKeys.Contains(cellKey))
            {
                if (additiveSelection)
                    _selectedCellKeys.Remove(cellKey);
                else
                    _selectedCellKeys.Add(cellKey);
            }
            else
            {
                _selectedCellKeys.Add(cellKey);
            }

            OnPropertyChanged(nameof(SelectedCellCount));
            RebuildPreview();
        }

        public void ClearSelection()
        {
            if (_selectedCellKeys.Count == 0 &&
                VerticalSplits.All(x => !x.IsSelected) &&
                HorizontalSplits.All(x => !x.IsSelected))
                return;

            _selectedCellKeys.Clear();

            foreach (KeyplanSplitLineItem item in VerticalSplits) item.IsSelected = false;
            foreach (KeyplanSplitLineItem item in HorizontalSplits) item.IsSelected = false;

            OnPropertyChanged(nameof(SelectedCellCount));
            OnPropertyChanged(nameof(SelectedVerticalSplitCount));
            OnPropertyChanged(nameof(SelectedHorizontalSplitCount));
            RebuildPreview();
        }

        // -------------------------------------------------------------------------
        // Cell edit states
        // -------------------------------------------------------------------------

        public void ExcludeSelectedCells()
        {
            foreach (string key in _selectedCellKeys.ToList())
                GetOrCreateCellEditState(key).IsExcluded = true;

            RebuildPreview();
        }

        public void IncludeSelectedCells()
        {
            foreach (string key in _selectedCellKeys.ToList())
            {
                CellEditState state = GetOrCreateCellEditState(key);
                state.IsExcluded = false;
                if (string.IsNullOrWhiteSpace(state.MergeGroupId))
                    RemoveEditIfEmpty(key, state);
            }

            RebuildPreview();
        }

        public void IncludeAllCells()
        {
            foreach (string key in _cellEdits.Keys.ToList())
            {
                CellEditState state = _cellEdits[key];
                state.IsExcluded = false;
                if (string.IsNullOrWhiteSpace(state.MergeGroupId))
                    _cellEdits.Remove(key);
            }

            RebuildPreview();
        }

        // -------------------------------------------------------------------------
        // Zone label session — public API
        // -------------------------------------------------------------------------

        /// <summary>
        /// Starts a new zone label pick session.
        /// Requires that LastGenerationResult is populated (i.e. Generate was called first).
        /// </summary>
        public bool BeginZoneLabelSession(out string error)
        {
            error = string.Empty;

            if (LastGenerationResult == null)
            {
                error = "Generate the keyplan first before assigning zone labels.";
                return false;
            }

            int fillCount = LastGenerationResult.CreatedItems
                .Count(r => r.Role == "FilledRegion" && r.UniqueId != string.Empty);

            if (fillCount < 1)
            {
                error = "No filled regions are available to label.";
                return false;
            }

            ActiveZoneSession = new KeyplanZoneLabelSession
            {
                LabelStyle = _zoneLabelStyle
            };

            StatusText = ZoneSessionStateLabel;
            RebuildPreview();
            return true;
        }
        /// <summary>
        /// Cancels the active zone label session without writing anything.
        /// </summary>
        public void CancelZoneLabelSession()
        {
            ActiveZoneSession = null;
            StatusText = "Zone label session cancelled.";
            RebuildPreview();
        }

        /// <summary>
        /// Routes a region pick during an active session.
        /// Advances the session state machine.
        /// Called by the window when a cell polygon is clicked while IsZoneSessionActive.
        /// </summary>
        public void HandleZoneRegionPick(string stableKey)
        {
            if (_activeZoneSession == null || string.IsNullOrWhiteSpace(stableKey))
                return;

            if (!_activeZoneSession.TogglePick(stableKey))
                return;

            OnPropertyChanged(nameof(ZoneSessionStateLabel));
            OnPropertyChanged(nameof(CanCommitZoneLabels));
            StatusText = ZoneSessionStateLabel;
            RebuildPreview();
        }
        /// <summary>
        /// Writes zone labels to Revit elements.
        /// Must be called from within an open Transaction, or this method opens one itself.
        /// </summary>
        private const string ZoneTagFamilyName = "BA_TAG_KeyPlan";

        public ZoneWriteResult CommitZoneLabels(Document doc)
        {
            if (_activeZoneSession == null || !_activeZoneSession.CanCommit)
                return new ZoneWriteResult();

            if (LastGenerationResult == null)
                return new ZoneWriteResult();

            List<KeyplanZoneAssignment> assignments = KeyplanZoneLabelService.BuildAssignments(
                LastGenerationResult.CreatedItems,
                _activeZoneSession.PickedKeysInOrder,
                _activeZoneSession.LabelStyle);

            List<string> allFilledRegionUniqueIds = LastGenerationResult.CreatedItems
                .Where(r => r != null && r.Role == "FilledRegion" && !string.IsNullOrWhiteSpace(r.UniqueId))
                .Select(r => r.UniqueId)
                .ToList();

            ZoneWriteResult writeResult;

            using (Transaction tx = new Transaction(doc, "Assign Keyplan Zone Labels"))
            {
                tx.Start();

                // 1. Clear stale parameter values from a previous session.
                KeyplanZoneParameterWriter.ClearZoneLabels(doc, allFilledRegionUniqueIds);

                // 2. Delete previously-created zone tags before creating new ones.
                DeleteExistingZoneTags(doc, LastGenerationResult.CreatedItems);

                // 3. Write new parameter values.
                writeResult = KeyplanZoneParameterWriter.WriteAssignments(doc, assignments);

                // 4. Create new tags for the new sequence.
                CreateZoneTags(doc, assignments, LastGenerationResult);

                // 5. Mirror labels (and clear stale ones) back onto records.
                foreach (GeneratedElementRecord rec in LastGenerationResult.CreatedItems)
                {
                    if (rec == null || rec.Role != "FilledRegion")
                        continue;

                    rec.ZoneLabel = string.Empty;
                    rec.ZoneParameterName = string.Empty;
                }

                foreach (KeyplanZoneAssignment a in assignments)
                {
                    GeneratedElementRecord rec = LastGenerationResult.CreatedItems
                        .FirstOrDefault(r => string.Equals(
                            r.StableKey, a.StableKey, StringComparison.Ordinal));

                    if (rec != null)
                    {
                        rec.ZoneLabel = a.Label;
                        rec.ZoneParameterName = a.ParameterName;
                    }
                }

                tx.Commit();
            }

            StatusText = "Zone labels committed. " + writeResult.Summary;

            ActiveZoneSession = null;
            RebuildPreview();

            return writeResult;
        }
        private static void DeleteExistingZoneTags(Document doc, IEnumerable<GeneratedElementRecord> records)
        {
            List<ElementId> toDelete = records
                .Where(r => r != null && r.ZoneTagElementId != null && r.ZoneTagElementId != ElementId.InvalidElementId)
                .Select(r => r.ZoneTagElementId)
                .Where(id => doc.GetElement(id) != null)
                .ToList();

            if (toDelete.Count == 0)
                return;

            try
            {
                doc.Delete(toDelete);
            }
            catch
            {
                // Best-effort cleanup; if a tag was already deleted (e.g. its
                // tagged element was removed), Delete may throw for that id.
                // Fall back to deleting individually so one bad id doesn't
                // block cleanup of the rest.
                foreach (ElementId id in toDelete)
                {
                    try
                    {
                        if (doc.GetElement(id) != null)
                            doc.Delete(id);
                    }
                    catch
                    {
                        // Ignore — element already gone.
                    }
                }
            }
            finally
            {
                foreach (GeneratedElementRecord rec in records)
                {
                    if (rec != null)
                        rec.ZoneTagElementId = ElementId.InvalidElementId;
                }
            }
        }

        private void CreateZoneTags(
            Document doc,
            IReadOnlyList<KeyplanZoneAssignment> assignments,
            GenerationResult generationResult)
        {
            if (assignments == null || assignments.Count == 0)
                return;

            Autodesk.Revit.DB.View targetView = doc.GetElement(generationResult.TargetViewId) as Autodesk.Revit.DB.View;
            if (targetView == null)
            {
                StatusText = "Zone labels written, but tags could not be created: target view not found.";
                return;
            }

            FamilySymbol tagSymbol = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(fs => fs.Family != null &&
                    string.Equals(fs.Family.Name, ZoneTagFamilyName, StringComparison.OrdinalIgnoreCase));

            if (tagSymbol == null)
            {
                StatusText = $"Zone labels written, but tag family '{ZoneTagFamilyName}' " +
                              "was not found in the project. Load it and re-commit to add tags.";
                return;
            }

            if (!tagSymbol.IsActive)
            {
                tagSymbol.Activate();
                doc.Regenerate();
            }

            foreach (KeyplanZoneAssignment assignment in assignments)
            {
                GeneratedElementRecord rec = generationResult.CreatedItems
                    .FirstOrDefault(r => string.Equals(r.StableKey, assignment.StableKey, StringComparison.Ordinal));

                if (rec == null || rec.Centroid == null || string.IsNullOrWhiteSpace(rec.UniqueId))
                    continue;

                Element regionElement = doc.GetElement(rec.UniqueId);
                if (regionElement is not FilledRegion region)
                    continue;

                try
                {
                    Reference reference = new Reference(region);

                    IndependentTag tag = IndependentTag.Create(
                        doc,
                        tagSymbol.Id,
                        targetView.Id,
                        reference,
                        false,
                        TagOrientation.Horizontal,
                        rec.Centroid);

                    rec.ZoneTagElementId = tag?.Id ?? ElementId.InvalidElementId;
                }
                catch (Exception ex)
                {
                    StatusText = $"Zone label written for '{assignment.Label}', " +
                                  $"but tag creation failed: {ex.Message}";
                }
            }
        }
        // -------------------------------------------------------------------------
        // Stores the last GenerationResult (called by the window after generation)
        // -------------------------------------------------------------------------

        public void SetLastGenerationResult(GenerationResult result)
        {
            LastGenerationResult = result;
            OnPropertyChanged(nameof(LastGenerationResult));
        }

        // -------------------------------------------------------------------------
        // Private — zone session helpers
        // -------------------------------------------------------------------------


        private void ApplyZoneSessionRoles(KeyplanGridPreviewData data)
        {
            if (_activeZoneSession == null || data?.FilledPolygons == null)
                return;

            foreach (PreviewCellPolygon poly in data.FilledPolygons)
            {
                if (poly == null) continue;

                poly.ZonePickRole = _activeZoneSession.GetRole(poly.CellKey);

                string liveLabel = _activeZoneSession.GetLabel(poly.CellKey);
                if (!string.IsNullOrWhiteSpace(liveLabel))
                    poly.ZoneLabel = liveLabel;
                // else: leave poly.ZoneLabel as set by BuildPreview (committed label, if any)
            }
        }
        // -------------------------------------------------------------------------
        // Private — split helpers
        // -------------------------------------------------------------------------

        private void RebuildSplitCollections()
        {
            PreserveAndRebuildFreeSplits(
                VerticalSplits, AxisOrientation.Vertical, Math.Max(0, XDivisionCount - 1));
            PreserveAndRebuildFreeSplits(
                HorizontalSplits, AxisOrientation.Horizontal, Math.Max(0, YDivisionCount - 1));
        }

        private static void PreserveAndRebuildFreeSplits(
            ObservableCollection<KeyplanSplitLineItem> items,
            AxisOrientation orientation,
            int targetCount)
        {
            List<KeyplanSplitLineItem> old = items.ToList();
            items.Clear();

            for (int i = 0; i < targetCount; i++)
            {
                KeyplanSplitLineItem existing = i < old.Count ? old[i] : null;
                double fallback = (i + 1.0) / (targetCount + 1.0);

                items.Add(new KeyplanSplitLineItem
                {
                    Id = existing?.Id ?? Guid.NewGuid().ToString("N"),
                    Orientation = orientation,
                    Normalized = existing?.Normalized ?? fallback,
                    IsEnabled = existing?.IsEnabled ?? true,
                    IsSelected = false,
                    Name = existing?.Name ?? string.Empty
                });
            }
        }

        private static KeyplanSplitLineItem CreateSplit(AxisOrientation orientation, double normalized)
        {
            return new KeyplanSplitLineItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Orientation = orientation,
                Normalized = normalized,
                IsEnabled = true,
                IsSelected = true
            };
        }

        private static void RemoveSelectedSplits(ObservableCollection<KeyplanSplitLineItem> items)
        {
            foreach (KeyplanSplitLineItem item in items.Where(x => x.IsSelected).ToList())
                items.Remove(item);
        }

        private static double FindSuggestedPosition(IEnumerable<KeyplanSplitLineItem> items)
        {
            List<double> used = items
                .Where(x => x != null && x.IsEnabled)
                .Select(x => x.Normalized)
                .OrderBy(x => x)
                .ToList();

            if (used.Count == 0) return 0.5;

            double bestPos = 0.5;
            double bestGap = -1.0;

            List<double> all = new List<double> { 0.0 };
            all.AddRange(used);
            all.Add(1.0);

            for (int i = 0; i < all.Count - 1; i++)
            {
                double gap = all[i + 1] - all[i];
                if (gap > bestGap) { bestGap = gap; bestPos = 0.5 * (all[i] + all[i + 1]); }
            }

            return bestPos;
        }

        private static double[] BuildNormalizedBreaksFromFreeSplits(
            IEnumerable<KeyplanSplitLineItem> splits)
        {
            List<double> interior = splits?
                .Where(x => x != null && x.IsEnabled)
                .Select(x => Clamp01(x.Normalized))
                .OrderBy(x => x)
                .ToList() ?? new List<double>();

            List<double> result = new List<double> { 0.0 };
            result.AddRange(interior);
            result.Add(1.0);
            return result.ToArray();
        }

        private KeyplanSplitLineItem FindSplit(string splitId, AxisOrientation orientation)
        {
            if (string.IsNullOrWhiteSpace(splitId)) return null;

            IEnumerable<KeyplanSplitLineItem> list =
                orientation == AxisOrientation.Vertical ? VerticalSplits : HorizontalSplits;

            return list.FirstOrDefault(x =>
                string.Equals(x.Id, splitId, StringComparison.Ordinal));
        }

        private CellEditState GetOrCreateCellEditState(string key)
        {
            if (!_cellEdits.TryGetValue(key, out CellEditState state) || state == null)
            {
                state = new CellEditState();
                _cellEdits[key] = state;
            }
            return state;
        }

        private void RemoveEditIfEmpty(string key, CellEditState state)
        {
            if (state != null && !state.IsExcluded &&
                string.IsNullOrWhiteSpace(state.MergeGroupId))
                _cellEdits.Remove(key);
        }

        private static KeyplanSplitLineItem CloneSplit(KeyplanSplitLineItem x)
        {
            return new KeyplanSplitLineItem
            {
                Id = x.Id,
                Orientation = x.Orientation,
                Normalized = x.Normalized,
                IsEnabled = x.IsEnabled,
                IsSelected = x.IsSelected,
                Name = x.Name
            };
        }

        private static double Clamp01(double value)
        {
            if (value < 0.0) return 0.0;
            if (value > 1.0) return 1.0;
            return value;
        }

        // -------------------------------------------------------------------------
        // INotifyPropertyChanged
        // -------------------------------------------------------------------------

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

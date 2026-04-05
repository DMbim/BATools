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
        private string _sourceViewName = "X.NP_Keyplan";
        private string _targetDraftingViewName = "X.NP_Keyplan_Drafting";
        private string _targetViewTemplateName = "X.NP_Keyplan_TEMPLATE";
        private string _filledRegionTypeName = "BA_Keyplan_Cell";

        private int _xDivisionCount = 4;
        private int _yDivisionCount = 4;

        private bool _clearTargetViewFirst = false;
        private bool _copySourceViewSpecificElements = false;
        private bool _drawGridLines = true;
        private bool _createFilledRegions = true;
        private bool _drawOutline = true;

        private KeyplanCellFillMode _fillMode = KeyplanCellFillMode.FullCellIfOccupied;
        private double _minimumOccupancyRatio = 0.05;

        private CurveLoop _sourceOuterLoop;
        private KeyplanGridPreviewData _previewData = new KeyplanGridPreviewData();
        private string _statusText = "Ready.";

        private bool _suspendAxisCallbacks;

        private readonly Dictionary<string, CellEditState> _cellEdits = new Dictionary<string, CellEditState>(StringComparer.Ordinal);
        private readonly HashSet<string> _selectedCellKeys = new HashSet<string>(StringComparer.Ordinal);

        public ObservableCollection<AxisPositionItem> XPositions { get; } = new ObservableCollection<AxisPositionItem>();
        public ObservableCollection<AxisPositionItem> YPositions { get; } = new ObservableCollection<AxisPositionItem>();

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

        public string TargetViewTemplateName
        {
            get => _targetViewTemplateName;
            set { _targetViewTemplateName = value ?? ""; OnPropertyChanged(); }
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
                int newValue = Math.Max(1, value);
                if (_xDivisionCount == newValue) return;

                _xDivisionCount = newValue;
                RebuildAxisCollections();
                RebuildPreview();
                OnPropertyChanged();
            }
        }

        public int YDivisionCount
        {
            get => _yDivisionCount;
            set
            {
                int newValue = Math.Max(1, value);
                if (_yDivisionCount == newValue) return;

                _yDivisionCount = newValue;
                RebuildAxisCollections();
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
            set
            {
                if (_drawGridLines == value) return;
                _drawGridLines = value;
                RebuildPreview();
                OnPropertyChanged();
            }
        }

        public bool CreateFilledRegions
        {
            get => _createFilledRegions;
            set
            {
                if (_createFilledRegions == value) return;
                _createFilledRegions = value;
                RebuildPreview();
                OnPropertyChanged();
            }
        }

        public bool DrawOutline
        {
            get => _drawOutline;
            set
            {
                if (_drawOutline == value) return;
                _drawOutline = value;
                RebuildPreview();
                OnPropertyChanged();
            }
        }

        public KeyplanCellFillMode FillMode
        {
            get => _fillMode;
            set
            {
                if (_fillMode == value) return;
                _fillMode = value;
                RebuildPreview();
                OnPropertyChanged();
            }
        }

        public double MinimumOccupancyRatio
        {
            get => _minimumOccupancyRatio;
            set
            {
                double clamped = value;
                if (clamped < 0.0) clamped = 0.0;
                if (clamped > 1.0) clamped = 1.0;

                if (Math.Abs(_minimumOccupancyRatio - clamped) > 1e-9)
                {
                    _minimumOccupancyRatio = clamped;
                    RebuildPreview();
                    OnPropertyChanged();
                }
            }
        }

        public Array FillModeValues => Enum.GetValues(typeof(KeyplanCellFillMode));

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

        public static KeyplanGridViewModel CreateDefault()
        {
            KeyplanGridViewModel vm = new KeyplanGridViewModel();
            vm.RebuildAxisCollections();
            return vm;
        }

        public void LoadInitialPreview(CurveLoop sourceOuterLoop)
        {
            _sourceOuterLoop = sourceOuterLoop ?? throw new ArgumentNullException(nameof(sourceOuterLoop));
            RebuildAxisCollections();
            RebuildPreview();
        }

        public KeyplanGridOptions BuildOptions()
        {
            return new KeyplanGridOptions
            {
                SourceViewName = SourceViewName,
                TargetDraftingViewName = TargetDraftingViewName,
                TargetViewTemplateName = TargetViewTemplateName,
                FilledRegionTypeName = FilledRegionTypeName,

                ClearTargetViewFirst = ClearTargetViewFirst,
                CopySourceViewSpecificElements = CopySourceViewSpecificElements,
                DrawGridLines = DrawGridLines,
                CreateFilledRegions = CreateFilledRegions,
                DrawOutline = DrawOutline,
                UseOutlineAsPrimaryFill = UseOutlineAsPrimaryFill,

                FillMode = FillMode,
                MinimumOccupancyRatio = MinimumOccupancyRatio
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

        public CurveLoop GetSourceOuterLoop()
        {
            return _sourceOuterLoop;
        }

        public void RebuildPreview(double canvasWidth = 860.0, double canvasHeight = 720.0, double padding = 24.0)
        {
            if (_sourceOuterLoop == null)
                return;

            try
            {
                double[] xBreaks = GetXNormalizedBreaks();
                double[] yBreaks = GetYNormalizedBreaks();

                PreviewData = KeyplanGridPreviewBuilder.BuildPreview(
                    _sourceOuterLoop,
                    xBreaks,
                    yBreaks,
                    FillMode,
                    MinimumOccupancyRatio,
                    DrawOutline,
                    UseOutlineAsPrimaryFill,
                    DrawGridLines,
                    CreateFilledRegions,
                    canvasWidth,
                    canvasHeight,
                    padding,
                    _cellEdits,
                    _selectedCellKeys);

                StatusText =
                    $"Preview updated. X cells: {XDivisionCount}, Y cells: {YDivisionCount}, " +
                    $"Mode: {FillMode}, Threshold: {MinimumOccupancyRatio:0.###}, " +
                    $"OutlineFill: {UseOutlineAsPrimaryFill}, Selected: {SelectedCellCount}.";
            }
            catch (Exception ex)
            {
                StatusText = $"Preview error: {ex.Message}";
            }
        }

        public double[] GetXNormalizedBreaks()
        {
            return BuildNormalizedBreaks(XDivisionCount, XPositions.Select(x => x.Normalized).ToArray());
        }

        public double[] GetYNormalizedBreaks()
        {
            return BuildNormalizedBreaks(YDivisionCount, YPositions.Select(y => y.Normalized).ToArray());
        }

        public void MoveAxis(AxisOrientation orientation, int interiorIndex, double normalized)
        {
            normalized = Clamp01(normalized);

            if (orientation == AxisOrientation.Vertical)
                MoveAxisInternal(XPositions, interiorIndex, normalized);
            else
                MoveAxisInternal(YPositions, interiorIndex, normalized);

            RebuildPreview();
        }

        public void ToggleCellSelection(string cellKey, bool additiveSelection)
        {
            if (string.IsNullOrWhiteSpace(cellKey))
                return;

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
            if (_selectedCellKeys.Count == 0)
                return;

            _selectedCellKeys.Clear();
            OnPropertyChanged(nameof(SelectedCellCount));
            RebuildPreview();
        }

        public void ExcludeSelectedCells()
        {
            foreach (string key in _selectedCellKeys.ToList())
            {
                CellEditState state = GetOrCreateCellEditState(key);
                state.IsExcluded = true;
            }

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
            List<string> keys = _cellEdits.Keys.ToList();
            foreach (string key in keys)
            {
                CellEditState state = _cellEdits[key];
                state.IsExcluded = false;
                if (string.IsNullOrWhiteSpace(state.MergeGroupId))
                    _cellEdits.Remove(key);
            }

            RebuildPreview();
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
            if (state == null)
                return;

            if (!state.IsExcluded && string.IsNullOrWhiteSpace(state.MergeGroupId))
                _cellEdits.Remove(key);
        }

        private void MoveAxisInternal(ObservableCollection<AxisPositionItem> items, int interiorIndex, double normalized)
        {
            if (items == null || interiorIndex < 0 || interiorIndex >= items.Count)
                return;

            double minGap = 0.01;
            double min = (interiorIndex == 0) ? minGap : items[interiorIndex - 1].Normalized + minGap;
            double max = (interiorIndex == items.Count - 1) ? 1.0 - minGap : items[interiorIndex + 1].Normalized - minGap;

            if (normalized < min) normalized = min;
            if (normalized > max) normalized = max;

            try
            {
                _suspendAxisCallbacks = true;
                items[interiorIndex].Normalized = normalized;
            }
            finally
            {
                _suspendAxisCallbacks = false;
            }
        }

        private void RebuildAxisCollections()
        {
            try
            {
                _suspendAxisCallbacks = true;

                PreserveAndRebuild(XPositions, XDivisionCount);
                PreserveAndRebuild(YPositions, YDivisionCount);

                foreach (AxisPositionItem x in XPositions)
                    x.PropertyChanged -= AxisItem_PropertyChanged;
                foreach (AxisPositionItem y in YPositions)
                    y.PropertyChanged -= AxisItem_PropertyChanged;

                foreach (AxisPositionItem x in XPositions)
                    x.PropertyChanged += AxisItem_PropertyChanged;
                foreach (AxisPositionItem y in YPositions)
                    y.PropertyChanged += AxisItem_PropertyChanged;

                NormalizeAxisCollection(XPositions);
                NormalizeAxisCollection(YPositions);
            }
            finally
            {
                _suspendAxisCallbacks = false;
            }
        }

        private void AxisItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_suspendAxisCallbacks)
                return;

            if (e.PropertyName == nameof(AxisPositionItem.Normalized))
            {
                try
                {
                    _suspendAxisCallbacks = true;

                    NormalizeAxisCollection(XPositions);
                    NormalizeAxisCollection(YPositions);
                }
                finally
                {
                    _suspendAxisCallbacks = false;
                }

                RebuildPreview();
            }
        }

        private static void PreserveAndRebuild(ObservableCollection<AxisPositionItem> items, int divisionCount)
        {
            double[] old = items.Select(i => i.Normalized).ToArray();
            items.Clear();

            int interiorCount = Math.Max(0, divisionCount - 1);
            for (int i = 0; i < interiorCount; i++)
            {
                double value = (i + 1.0) / divisionCount;
                if (i < old.Length)
                    value = old[i];

                items.Add(new AxisPositionItem
                {
                    Index = i + 1,
                    Normalized = value
                });
            }
        }

        private static void NormalizeAxisCollection(ObservableCollection<AxisPositionItem> items)
        {
            if (items == null || items.Count == 0)
                return;

            double minGap = 0.01;

            var ordered = items.OrderBy(i => i.Normalized).ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                double min = (i == 0) ? minGap : ordered[i - 1].Normalized + minGap;
                double max = (i == ordered.Count - 1) ? 1.0 - minGap : ordered[i + 1].Normalized - minGap;

                double v = ordered[i].Normalized;
                if (v < min) v = min;
                if (v > max) v = max;

                ordered[i].Normalized = v;
            }

            items.Clear();
            for (int i = 0; i < ordered.Count; i++)
            {
                ordered[i].Index = i + 1;
                items.Add(ordered[i]);
            }
        }

        private static double[] BuildNormalizedBreaks(int divisionCount, double[] interior)
        {
            double[] result = new double[Math.Max(2, divisionCount + 1)];
            result[0] = 0.0;
            result[result.Length - 1] = 1.0;

            for (int i = 1; i < result.Length - 1; i++)
            {
                double fallback = (double)i / divisionCount;
                double value = (interior != null && i - 1 < interior.Length) ? interior[i - 1] : fallback;

                if (value < 0.0) value = 0.0;
                if (value > 1.0) value = 1.0;

                result[i] = value;
            }

            Array.Sort(result);

            double minGap = 0.01;
            for (int i = 1; i < result.Length; i++)
            {
                if (result[i] <= result[i - 1] + minGap)
                    result[i] = Math.Min(1.0, result[i - 1] + minGap);
            }

            result[0] = 0.0;
            result[result.Length - 1] = 1.0;

            return result;
        }

        private static double Clamp01(double value)
        {
            if (value < 0.0) return 0.0;
            if (value > 1.0) return 1.0;
            return value;
        }
        private bool _useOutlineAsPrimaryFill = true;
        public bool UseOutlineAsPrimaryFill
        {
            get => _useOutlineAsPrimaryFill;
            set
            {
                if (_useOutlineAsPrimaryFill == value)
                    return;

                _useOutlineAsPrimaryFill = value;
                RebuildPreview();
                OnPropertyChanged();
            }
        }
        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
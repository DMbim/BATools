// File: BA/ViewModels/CurveToElement/CurveTypeGroupViewModel.cs
// Action: REPLACE (full file)

using System;
using System.Collections.Generic; // <- NEW
using System.Collections.ObjectModel;
using System.Globalization;
using Autodesk.Revit.DB;
using BA.Core.CurveToElement.Models;
using BA.Core.CurveToElement.Services;
using BA.UI.Mvvm;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BA.ViewModels.CurveToElement
{
    public class CurveTypeGroupViewModel : BA.UI.Mvvm.ObservableObject
    {
        private readonly Action<Guid, ElementId> _requestPreview;
        private readonly Units _documentUnits;

        private WallTypeOption _selectedWallType;
        private LevelOption _selectedBaseLevel;
        private LevelOption _selectedTopLevel;
        private WallLocationLine _selectedLocationLine = WallLocationLine.WallCenterline;
        private WallHeightMode _heightMode = WallHeightMode.Unconnected;
        private string _baseOffsetText = "0";
        private string _unconnectedHeightText;
        private string _topOffsetText = "0";
        private bool _flipSide;
        private bool _allowEndJoins = true;
        private bool _structuralUsage;

        private bool _isPreviewSupported;
        private string _previewUnsupportedReason;
        private string _totalWidthDisplay = "-";
        private string _selectedLocationLineOffsetDisplay = "-";

        public CurveTypeGroupViewModel(
            CurveTypeGroup group,
            IReadOnlyList<CurveChain> chains, // <- NEW (replaces the old standalone hasOpenChain bool)
            ObservableCollection<WallTypeOption> availableWallTypes,
            ObservableCollection<LevelOption> availableLevels,
            Units documentUnits,
            Action<Guid, ElementId> requestPreview)
        {
            Group = group ?? throw new ArgumentNullException(nameof(group));
            Chains = chains ?? throw new ArgumentNullException(nameof(chains)); // <- NEW
            _requestPreview = requestPreview ?? throw new ArgumentNullException(nameof(requestPreview));
            _documentUnits = documentUnits ?? throw new ArgumentNullException(nameof(documentUnits));

            GroupId = Guid.NewGuid();
            HasOpenChain = Chains.Count > 0 && !AllChainsClosed(Chains); // <- CHANGED (derived, not passed in)
            AvailableWallTypes = availableWallTypes ?? throw new ArgumentNullException(nameof(availableWallTypes));
            AvailableLevels = availableLevels ?? throw new ArgumentNullException(nameof(availableLevels));

            _unconnectedHeightText = FormatFeetForDisplay(9.8425);
        }

        public Guid GroupId { get; }
        public CurveTypeGroup Group { get; }

        /// <summary>
        /// The chain topology this group's curves resolved to, computed once at construction.
        /// WallGenerationService consumes this same list - chains are never rebuilt at
        /// generation time, so the UI's HasOpenChain state and the generated geometry are
        /// guaranteed to be based on identical chain data.
        /// </summary>
        public IReadOnlyList<CurveChain> Chains { get; } // <- NEW

        public string StyleName => Group.StyleName;
        public int CurveCount => Group.Curves.Count;
        public bool HasOpenChain { get; }

        public ObservableCollection<WallTypeOption> AvailableWallTypes { get; }
        public ObservableCollection<LevelOption> AvailableLevels { get; }

        public static WallLocationLine[] AvailableLocationLines { get; } =
        {
            WallLocationLine.WallCenterline,
            WallLocationLine.CoreCenterline,
            WallLocationLine.FinishFaceExterior,
            WallLocationLine.FinishFaceInterior,
            WallLocationLine.CoreExterior,
            WallLocationLine.CoreInterior
        };

        public static WallHeightMode[] AvailableHeightModes { get; } =
        {
            WallHeightMode.Unconnected,
            WallHeightMode.UpToLevel
        };

        public WallTypeOption SelectedWallType
        {
            get => _selectedWallType;
            set
            {
                if (!SetProperty(ref _selectedWallType, value))
                    return;

                if (value != null)
                    _requestPreview(GroupId, value.Id);
            }
        }

        public LevelOption SelectedBaseLevel
        {
            get => _selectedBaseLevel;
            set => SetProperty(ref _selectedBaseLevel, value);
        }

        public LevelOption SelectedTopLevel
        {
            get => _selectedTopLevel;
            set => SetProperty(ref _selectedTopLevel, value);
        }

        public WallLocationLine SelectedLocationLine
        {
            get => _selectedLocationLine;
            set
            {
                if (!SetProperty(ref _selectedLocationLine, value))
                    return;

                RefreshSelectedLocationLineOffsetDisplay();
            }
        }

        public WallHeightMode HeightMode
        {
            get => _heightMode;
            set
            {
                if (!SetProperty(ref _heightMode, value))
                    return;

                OnPropertyChanged(nameof(IsUnconnectedHeightMode));
                OnPropertyChanged(nameof(IsUpToLevelHeightMode));
            }
        }

        public bool IsUnconnectedHeightMode => HeightMode == WallHeightMode.Unconnected;
        public bool IsUpToLevelHeightMode => HeightMode == WallHeightMode.UpToLevel;

        public string BaseOffsetText
        {
            get => _baseOffsetText;
            set => SetProperty(ref _baseOffsetText, value);
        }

        public string UnconnectedHeightText
        {
            get => _unconnectedHeightText;
            set => SetProperty(ref _unconnectedHeightText, value);
        }

        public string TopOffsetText
        {
            get => _topOffsetText;
            set => SetProperty(ref _topOffsetText, value);
        }

        public bool FlipSide
        {
            get => _flipSide;
            set => SetProperty(ref _flipSide, value);
        }

        public bool AllowEndJoins
        {
            get => _allowEndJoins;
            set => SetProperty(ref _allowEndJoins, value);
        }

        public bool StructuralUsage
        {
            get => _structuralUsage;
            set => SetProperty(ref _structuralUsage, value);
        }

        public bool IsPreviewSupported
        {
            get => _isPreviewSupported;
            private set => SetProperty(ref _isPreviewSupported, value);
        }

        public string PreviewUnsupportedReason
        {
            get => _previewUnsupportedReason;
            private set => SetProperty(ref _previewUnsupportedReason, value);
        }

        public string TotalWidthDisplay
        {
            get => _totalWidthDisplay;
            private set => SetProperty(ref _totalWidthDisplay, value);
        }

        public string SelectedLocationLineOffsetDisplay
        {
            get => _selectedLocationLineOffsetDisplay;
            private set => SetProperty(ref _selectedLocationLineOffsetDisplay, value);
        }

        private WallPreviewResult _lastPreviewResult;

        public void ApplyPreviewResult(WallPreviewResult result)
        {
            if (result == null || result.GroupId != GroupId)
                return;

            _lastPreviewResult = result;

            IsPreviewSupported = result.Preview.IsSupported;
            PreviewUnsupportedReason = result.Preview.UnsupportedReason;
            TotalWidthDisplay = result.FormattedTotalWidth;

            RefreshSelectedLocationLineOffsetDisplay();
        }

        private void RefreshSelectedLocationLineOffsetDisplay()
        {
            if (_lastPreviewResult == null || !_lastPreviewResult.Preview.IsSupported)
            {
                SelectedLocationLineOffsetDisplay = "-";
                return;
            }

            switch (SelectedLocationLine)
            {
                case WallLocationLine.WallCenterline:
                    SelectedLocationLineOffsetDisplay = "0";
                    return;
                case WallLocationLine.CoreCenterline:
                    SelectedLocationLineOffsetDisplay = _lastPreviewResult.FormattedCoreCenterline;
                    return;
                case WallLocationLine.FinishFaceExterior:
                    SelectedLocationLineOffsetDisplay = _lastPreviewResult.FormattedFinishSide1Face;
                    return;
                case WallLocationLine.FinishFaceInterior:
                    SelectedLocationLineOffsetDisplay = _lastPreviewResult.FormattedFinishSide2Face;
                    return;
                case WallLocationLine.CoreExterior:
                    SelectedLocationLineOffsetDisplay = _lastPreviewResult.FormattedCoreSide1Face;
                    return;
                case WallLocationLine.CoreInterior:
                    SelectedLocationLineOffsetDisplay = _lastPreviewResult.FormattedCoreSide2Face;
                    return;
                default:
                    SelectedLocationLineOffsetDisplay = "-";
                    return;
            }
        }

        public bool TryBuildSettings(out WallGroupSettings settings, out string validationError)
        {
            settings = null;

            if (SelectedWallType == null)
            {
                validationError = $"Group '{StyleName}': no wall type selected.";
                return false;
            }

            if (SelectedBaseLevel == null)
            {
                validationError = $"Group '{StyleName}': no base level selected.";
                return false;
            }

            if (!LengthInputParser.TryParse(_documentUnits, BaseOffsetText, out double baseOffset))
            {
                validationError = $"Group '{StyleName}': base offset '{BaseOffsetText}' is not a valid length.";
                return false;
            }

            double unconnectedHeight = 0.0;
            double topOffset = 0.0;

            if (HeightMode == WallHeightMode.Unconnected)
            {
                if (!LengthInputParser.TryParse(_documentUnits, UnconnectedHeightText, out unconnectedHeight) || unconnectedHeight <= 0.0)
                {
                    validationError = $"Group '{StyleName}': unconnected height '{UnconnectedHeightText}' is not a valid positive length.";
                    return false;
                }
            }
            else
            {
                if (SelectedTopLevel == null)
                {
                    validationError = $"Group '{StyleName}': height mode is 'Up To Level' but no top level is selected.";
                    return false;
                }

                if (!LengthInputParser.TryParse(_documentUnits, TopOffsetText, out topOffset))
                {
                    validationError = $"Group '{StyleName}': top offset '{TopOffsetText}' is not a valid length.";
                    return false;
                }
            }

            settings = new WallGroupSettings
            {
                WallTypeId = SelectedWallType.Id,
                BaseLevelId = SelectedBaseLevel.Id,
                BaseOffset = baseOffset,
                HeightMode = HeightMode,
                UnconnectedHeight = unconnectedHeight,
                TopLevelId = SelectedTopLevel?.Id ?? ElementId.InvalidElementId,
                TopOffset = topOffset,
                LocationLine = SelectedLocationLine,
                FlipSide = FlipSide,
                AllowEndJoins = AllowEndJoins,
                StructuralUsage = StructuralUsage
            };

            validationError = null;
            return true;
        }

        private static bool AllChainsClosed(IReadOnlyList<CurveChain> chains) // <- NEW
        {
            for (int i = 0; i < chains.Count; i++)
            {
                if (!chains[i].IsClosed)
                    return false;
            }
            return true;
        }

        private string FormatFeetForDisplay(double feet)
        {
            return UnitFormatUtils.Format(_documentUnits, SpecTypeId.Length, feet, false);
        }
    }
}
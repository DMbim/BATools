using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using BA.Core.Export.Infrastructure;
using BA.Core.Export.Models;
using BA.UI.Mvvm;
using Microsoft.Win32;

namespace BA.ViewModels.Export
{
    /// <summary>
    /// Wraps one ExportJobSettings for editing in the settings window. Holds
    /// its own copy of scalar values so XAML can bind directly with change
    /// notification; ExportJobSettings itself has none. ToModel() is the
    /// single place that converts back to the persisted shape.
    ///
    /// DWG and PDF settings are exposed as flat properties here rather than
    /// nested view model objects, matching every other field in this class,
    /// even though there are a lot of them. ToModel()/the constructor are
    /// the only two places that translate to and from DwgSettings/PdfSettings.
    /// </summary>
    public class ExportJobEditorViewModel : BA.UI.Mvvm.ObservableObject
    {
        private string _jobName;
        private bool _enabled;
        private bool _exportPdf;
        private bool _exportDwg;
        private string _namingTemplate;
        private string _dateFormat;
        private string _outputFolderTemplate;
        private bool _scheduleEnabled;
        private string _scheduledTimeText;
        private int _catchUpMinutes;
        private string _previewFileName = string.Empty;
        private string _previewFolder = string.Empty;
        private string _statusMessage = string.Empty;
        private bool _isBusy;

        // DWG settings, flat.
        private ACADVersion _dwgFileVersion;
        private DwgLayerMappingStandard _dwgLayerMapping;
        private ExportUnit _dwgTargetUnit;
        private bool _dwgMergedViews;
        private bool _dwgSharedCoords;
        private bool _dwgExportingAreas;
        private bool _dwgHideScopeBox;
        private bool _dwgHideReferencePlane;
        private LineScaling _dwgLineScaling;
        private ExportColorMode _dwgColors;
        private PropOverrideMode _dwgPropOverrides;
        private string _dwgPredefinedSetupName = string.Empty;

        // PDF settings, flat.
        private ColorDepthType _pdfColorDepth;
        private PDFExportQualityType _pdfExportQuality;
        private ZoomType _pdfZoomType;
        private int _pdfZoomPercentage;
        private bool _pdfAlwaysUseRaster;
        private bool _pdfHideCropBoundaries;
        private bool _pdfHideScopeBoxes;
        private bool _pdfHideReferencePlane;
        private bool _pdfViewLinksInBlue;

        public Guid JobId { get; }

        public ObservableCollection<DayToggleViewModel> Days { get; }

        public string JobName
        {
            get => _jobName;
            set => SetProperty(ref _jobName, value);
        }

        public bool Enabled
        {
            get => _enabled;
            set => SetProperty(ref _enabled, value);
        }

        public bool ExportPdf
        {
            get => _exportPdf;
            set => SetProperty(ref _exportPdf, value);
        }

        public bool ExportDwg
        {
            get => _exportDwg;
            set => SetProperty(ref _exportDwg, value);
        }

        private List<string> _selectedSheetNumbers = new List<string>();

        public string SheetSelectionSummary => _selectedSheetNumbers.Count == 0
            ? "No sheets selected"
            : _selectedSheetNumbers.Count == 1
                ? "1 sheet selected"
                : $"{_selectedSheetNumbers.Count} sheets selected";

        private List<string> _selectedViewUniqueIds = new List<string>();

        public string ViewSelectionSummary => _selectedViewUniqueIds.Count == 0
            ? "No views selected"
            : _selectedViewUniqueIds.Count == 1
                ? "1 view selected"
                : $"{_selectedViewUniqueIds.Count} views selected";

        private ExportSourceMode _sourceMode = ExportSourceMode.Sheets;
        private bool _useActiveViewOrSheet;
        public ExportSourceMode SourceMode
        {
            get => _sourceMode;
            set
            {
                if (SetProperty(ref _sourceMode, value))
                {
                    OnPropertyChanged(nameof(IsSheetsMode));
                    OnPropertyChanged(nameof(IsViewsMode));
                }
            }
        }

        public bool IsSheetsMode
        {
            get => SourceMode == ExportSourceMode.Sheets;
            set
            {
                if (value)
                {
                    SourceMode = ExportSourceMode.Sheets;
                }
            }
        }

        public bool IsViewsMode
        {
            get => SourceMode == ExportSourceMode.Views;
            set
            {
                if (value)
                {
                    SourceMode = ExportSourceMode.Views;
                }
            }
        }

        /// <summary>
        /// When true, Pick Sheets/Pick Views selection is ignored entirely
        /// at run time and whatever is active in Revit at that moment is
        /// exported instead. A mismatch (active view is a sheet in Views
        /// mode, or not a sheet in Sheets mode) fails the job with a clear
        /// message rather than silently falling back to the configured
        /// selection.
        /// </summary>
        public bool UseActiveViewOrSheet
        {
            get => _useActiveViewOrSheet;
            set => SetProperty(ref _useActiveViewOrSheet, value);
        }

        public string NamingTemplate
        {
            get => _namingTemplate;
            set => SetProperty(ref _namingTemplate, value);
        }

        public string DateFormat
        {
            get => _dateFormat;
            set => SetProperty(ref _dateFormat, value);
        }

        public string OutputFolderTemplate
        {
            get => _outputFolderTemplate;
            set => SetProperty(ref _outputFolderTemplate, value);
        }

        public bool ScheduleEnabled
        {
            get => _scheduleEnabled;
            set => SetProperty(ref _scheduleEnabled, value);
        }

        /// <summary>
        /// "HH:mm" text bound directly to a textbox, parsed on ToModel().
        /// Invalid text falls back to 18:00 rather than throwing.
        /// </summary>
        public string ScheduledTimeText
        {
            get => _scheduledTimeText;
            set => SetProperty(ref _scheduledTimeText, value);
        }

        public int CatchUpMinutes
        {
            get => _catchUpMinutes;
            set => SetProperty(ref _catchUpMinutes, value);
        }

        public string PreviewFileName
        {
            get => _previewFileName;
            private set => SetProperty(ref _previewFileName, value);
        }

        public string PreviewFolder
        {
            get => _previewFolder;
            private set => SetProperty(ref _previewFolder, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set => SetProperty(ref _isBusy, value);
        }

        // ---- DWG settings properties ----

        public ACADVersion DwgFileVersion
        {
            get => _dwgFileVersion;
            set => SetProperty(ref _dwgFileVersion, value);
        }

        public DwgLayerMappingStandard DwgLayerMapping
        {
            get => _dwgLayerMapping;
            set => SetProperty(ref _dwgLayerMapping, value);
        }

        public ExportUnit DwgTargetUnit
        {
            get => _dwgTargetUnit;
            set => SetProperty(ref _dwgTargetUnit, value);
        }

        public bool DwgMergedViews
        {
            get => _dwgMergedViews;
            set => SetProperty(ref _dwgMergedViews, value);
        }

        public bool DwgSharedCoords
        {
            get => _dwgSharedCoords;
            set => SetProperty(ref _dwgSharedCoords, value);
        }

        public bool DwgExportingAreas
        {
            get => _dwgExportingAreas;
            set => SetProperty(ref _dwgExportingAreas, value);
        }

        public bool DwgHideScopeBox
        {
            get => _dwgHideScopeBox;
            set => SetProperty(ref _dwgHideScopeBox, value);
        }

        public bool DwgHideReferencePlane
        {
            get => _dwgHideReferencePlane;
            set => SetProperty(ref _dwgHideReferencePlane, value);
        }

        /// <summary>
        /// "Set linetype scale" in Revit's native DWG export UI.
        /// </summary>
        public LineScaling DwgLineScaling
        {
            get => _dwgLineScaling;
            set => SetProperty(ref _dwgLineScaling, value);
        }

        public IEnumerable<LineScaling> AvailableDwgLineScalings { get; } =
            Enum.GetValues(typeof(LineScaling)).Cast<LineScaling>();

        /// <summary>
        /// IndexColors snaps to the nearest of 255 fixed AutoCAD palette
        /// colors and can visibly shift colors that don't already match
        /// the palette. TrueColorRGB preserves exact RGB from the model.
        /// </summary>
        public ExportColorMode DwgColors
        {
            get => _dwgColors;
            set => SetProperty(ref _dwgColors, value);
        }

        public IEnumerable<ExportColorMode> AvailableDwgColorModes { get; } =
            Enum.GetValues(typeof(ExportColorMode)).Cast<ExportColorMode>();

        /// <summary>
        /// "How to export overridden object styles" in Revit's native DWG
        /// export UI. ByEntity writes explicit per-object color and
        /// lineweight overrides, ByLayer inherits from the layer instead,
        /// the standard, expected convention for a clean CAD deliverable.
        /// </summary>
        public PropOverrideMode DwgPropOverrides
        {
            get => _dwgPropOverrides;
            set => SetProperty(ref _dwgPropOverrides, value);
        }

        public IEnumerable<PropOverrideMode> AvailableDwgPropOverrideModes { get; } =
            Enum.GetValues(typeof(PropOverrideMode)).Cast<PropOverrideMode>();

        /// <summary>
        /// "&lt;In-session export setup&gt;", the same literal placeholder
        /// Revit's own Select Export Setup list always shows first, always
        /// present in AvailablePredefinedSetupNames regardless of what the
        /// document has, selecting it clears back to using the settings
        /// above rather than requiring the field to be manually cleared.
        /// </summary>
        private const string InSessionSetupLabel = "<In-session export setup>";

        /// <summary>
        /// Name of a DWG export setup already built in Revit's own
        /// "Modify DWG/DXF Export Setup" dialog. When set, that setup is
        /// used completely as-is at export time, every other DWG setting
        /// is ignored, not just the layer table, since a predefined setup
        /// carries Colors and Object Style Overrides too. The getter
        /// returns InSessionSetupLabel when nothing is set, the setter
        /// normalizes that same label back to empty, so the combo box
        /// always shows a real, selectable "none" option instead of an
        /// empty field the person has to know to clear by hand. Selecting
        /// a real setup name also reloads and displays that setup's
        /// actual values in the DWG controls below, readjusting them
        /// rather than leaving them showing something that won't actually
        /// be used.
        /// </summary>
        public string DwgPredefinedSetupName
        {
            get => string.IsNullOrWhiteSpace(_dwgPredefinedSetupName) ? InSessionSetupLabel : _dwgPredefinedSetupName;
            set
            {
                var normalized = string.Equals(value, InSessionSetupLabel, StringComparison.Ordinal)
                    ? string.Empty
                    : (value ?? string.Empty);

                if (SetProperty(ref _dwgPredefinedSetupName, normalized))
                {
                    OnPropertyChanged(nameof(IsCustomDwgSettingsEnabled));

                    if (!string.IsNullOrWhiteSpace(_dwgPredefinedSetupName))
                    {
                        LoadAndApplyPredefinedDetails(_dwgPredefinedSetupName);
                    }
                }
            }
        }

        /// <summary>
        /// Drives IsEnabled on every other DWG setting control. False
        /// (grayed out) whenever a real predefined setup is selected,
        /// since none of those settings apply in that case, editing them
        /// would be misleading, not just ineffective.
        /// </summary>
        public bool IsCustomDwgSettingsEnabled => string.IsNullOrWhiteSpace(_dwgPredefinedSetupName);

        public ObservableCollection<string> AvailablePredefinedSetupNames { get; } = new ObservableCollection<string>();

        public IEnumerable<ACADVersion> AvailableAcadVersions { get; } =
            Enum.GetValues(typeof(ACADVersion)).Cast<ACADVersion>().Distinct();

        public IEnumerable<DwgLayerMappingStandard> AvailableDwgLayerMappings { get; } =
            Enum.GetValues(typeof(DwgLayerMappingStandard)).Cast<DwgLayerMappingStandard>();

        public IEnumerable<ExportUnit> AvailableExportUnits { get; } =
            Enum.GetValues(typeof(ExportUnit)).Cast<ExportUnit>();

        // ---- PDF settings properties ----

        public ColorDepthType PdfColorDepth
        {
            get => _pdfColorDepth;
            set => SetProperty(ref _pdfColorDepth, value);
        }

        public PDFExportQualityType PdfExportQuality
        {
            get => _pdfExportQuality;
            set => SetProperty(ref _pdfExportQuality, value);
        }

        public ZoomType PdfZoomType
        {
            get => _pdfZoomType;
            set
            {
                if (SetProperty(ref _pdfZoomType, value))
                {
                    OnPropertyChanged(nameof(PdfZoomPercentageEnabled));
                }
            }
        }

        /// <summary>
        /// Drives IsEnabled on the zoom percentage field in XAML, only
        /// meaningful when PdfZoomType is Zoom, PDFExportOptions ignores
        /// it otherwise.
        /// </summary>
        public bool PdfZoomPercentageEnabled => PdfZoomType == ZoomType.Zoom;

        public int PdfZoomPercentage
        {
            get => _pdfZoomPercentage;
            set => SetProperty(ref _pdfZoomPercentage, value);
        }

        public bool PdfAlwaysUseRaster
        {
            get => _pdfAlwaysUseRaster;
            set => SetProperty(ref _pdfAlwaysUseRaster, value);
        }

        public bool PdfHideCropBoundaries
        {
            get => _pdfHideCropBoundaries;
            set => SetProperty(ref _pdfHideCropBoundaries, value);
        }

        public bool PdfHideScopeBoxes
        {
            get => _pdfHideScopeBoxes;
            set => SetProperty(ref _pdfHideScopeBoxes, value);
        }

        public bool PdfHideReferencePlane
        {
            get => _pdfHideReferencePlane;
            set => SetProperty(ref _pdfHideReferencePlane, value);
        }

        public bool PdfViewLinksInBlue
        {
            get => _pdfViewLinksInBlue;
            set => SetProperty(ref _pdfViewLinksInBlue, value);
        }

        public IEnumerable<ColorDepthType> AvailableColorDepths { get; } =
            Enum.GetValues(typeof(ColorDepthType)).Cast<ColorDepthType>();

        public IEnumerable<PDFExportQualityType> AvailablePdfQualities { get; } =
            Enum.GetValues(typeof(PDFExportQualityType)).Cast<PDFExportQualityType>();

        public IEnumerable<ZoomType> AvailablePdfZoomTypes { get; } =
            Enum.GetValues(typeof(ZoomType)).Cast<ZoomType>();

        // ---- Commands ----

        public BA.UI.Mvvm.RelayCommand PreviewCommand { get; }
        public BA.UI.Mvvm.RelayCommand RunNowCommand { get; }
        public BA.UI.Mvvm.RelayCommand PickSheetsCommand { get; }
        public BA.UI.Mvvm.RelayCommand PickViewsCommand { get; }
        public BA.UI.Mvvm.RelayCommand EditNamingCommand { get; }
        public BA.UI.Mvvm.RelayCommand BrowseOutputFolderCommand { get; }
        public BA.UI.Mvvm.RelayCommand RefreshPredefinedDwgSetupsCommand { get; }

        public ExportJobEditorViewModel(ExportJobSettings model)
        {
            JobId = model.JobId;
            _jobName = model.JobName;
            _enabled = model.Enabled;
            _exportPdf = model.ExportPdf;
            _exportDwg = model.ExportDwg;
            _selectedSheetNumbers = new List<string>(model.SelectedSheetNumbers ?? new List<string>());
            _selectedViewUniqueIds = new List<string>(model.SelectedViewUniqueIds ?? new List<string>());
            _sourceMode = model.SourceMode;
            _useActiveViewOrSheet = model.UseActiveViewOrSheet;
            _namingTemplate = model.NamingTemplate;
            _dateFormat = model.DateFormat;
            _outputFolderTemplate = model.OutputFolderTemplate;
            _scheduleEnabled = model.ScheduleEnabled;
            _scheduledTimeText = model.ScheduledTimeOfDay.ToString(@"hh\:mm");
            _catchUpMinutes = (int)model.CatchUpWindow.TotalMinutes;

            var dwg = model.DwgSettings ?? new DwgSettings();
            _dwgFileVersion = dwg.FileVersion;
            _dwgLayerMapping = dwg.LayerMapping;
            _dwgTargetUnit = dwg.TargetUnit;
            _dwgMergedViews = dwg.MergedViews;
            _dwgSharedCoords = dwg.SharedCoords;
            _dwgExportingAreas = dwg.ExportingAreas;
            _dwgHideScopeBox = dwg.HideScopeBox;
            _dwgHideReferencePlane = dwg.HideReferencePlane;
            _dwgLineScaling = dwg.LineScaling;
            _dwgColors = dwg.Colors;
            _dwgPropOverrides = dwg.PropOverrides;
            _dwgPredefinedSetupName = dwg.PredefinedSetupName;

            var pdf = model.PdfSettings ?? new PdfSettings();
            _pdfColorDepth = pdf.ColorDepth;
            _pdfExportQuality = pdf.ExportQuality;
            _pdfZoomType = pdf.ZoomType;
            _pdfZoomPercentage = pdf.ZoomPercentage;
            _pdfAlwaysUseRaster = pdf.AlwaysUseRaster;
            _pdfHideCropBoundaries = pdf.HideCropBoundaries;
            _pdfHideScopeBoxes = pdf.HideScopeBoxes;
            _pdfHideReferencePlane = pdf.HideReferencePlane;
            _pdfViewLinksInBlue = pdf.ViewLinksInBlue;

            Days = new ObservableCollection<DayToggleViewModel>(
                Enum.GetValues(typeof(DayOfWeek))
                    .Cast<DayOfWeek>()
                    .Select(d => new DayToggleViewModel(d) { IsSelected = model.ScheduledDays.Contains(d) }));

            PreviewCommand = new BA.UI.Mvvm.RelayCommand(_ => RunPreview());
            RunNowCommand = new BA.UI.Mvvm.RelayCommand(_ => RunNow());
            PickSheetsCommand = new BA.UI.Mvvm.RelayCommand(_ => PickSheets());
            PickViewsCommand = new BA.UI.Mvvm.RelayCommand(_ => PickViews());
            EditNamingCommand = new BA.UI.Mvvm.RelayCommand(_ => EditNaming());
            BrowseOutputFolderCommand = new BA.UI.Mvvm.RelayCommand(_ => BrowseOutputFolder());
            RefreshPredefinedDwgSetupsCommand = new BA.UI.Mvvm.RelayCommand(_ => LoadPredefinedDwgSetups());

            LoadPredefinedDwgSetups();

            if (!string.IsNullOrWhiteSpace(_dwgPredefinedSetupName))
            {
                LoadAndApplyPredefinedDetails(_dwgPredefinedSetupName);
            }
        }

        /// <summary>
        /// Reloads the list of DWG export setups from the live document.
        /// Called once at construction, and again on demand via
        /// RefreshPredefinedDwgSetupsCommand, since this list can change
        /// from outside this tool entirely (Revit's own Modify DWG/DXF
        /// Export Setup dialog), a job editor left open across that
        /// change would otherwise keep showing stale data indefinitely.
        /// InSessionSetupLabel is always seeded as the first entry,
        /// regardless of what the document has, matching Revit's own
        /// Select Export Setup list.
        /// </summary>
        private void LoadPredefinedDwgSetups()
        {
            var request = new ExportUiRequest { Action = ExportUiAction.GetPredefinedDwgSetups };

            ExportUiBridge.Submit(request, response =>
            {
                if (!response.Success)
                {
                    StatusMessage = $"Failed to load DWG export setups: {response.ErrorMessage}";
                    return;
                }

                AvailablePredefinedSetupNames.Clear();
                AvailablePredefinedSetupNames.Add(InSessionSetupLabel);

                foreach (var name in response.PredefinedDwgSetupNames)
                {
                    AvailablePredefinedSetupNames.Add(name);
                }

                StatusMessage = $"Ready. {response.PredefinedDwgSetupNames.Count} DWG export setup(s) found.";
            });
        }

        /// <summary>
        /// Loads the actual values a named predefined setup carries and
        /// applies them to the DWG controls below, purely for display,
        /// so the panel shows what will really be used rather than
        /// something stale or misleading while that setup is active.
        /// These controls are also disabled at that point
        /// (IsCustomDwgSettingsEnabled), so this never conflicts with
        /// anything the person is actively editing.
        /// </summary>
        private void LoadAndApplyPredefinedDetails(string setupName)
        {
            var request = new ExportUiRequest
            {
                Action = ExportUiAction.GetPredefinedDwgSetupDetails,
                SetupNameToInspect = setupName
            };

            ExportUiBridge.Submit(request, response =>
            {
                if (!response.Success || response.PredefinedSetupDetails == null)
                {
                    StatusMessage = $"Failed to load setup details: {response.ErrorMessage}";
                    return;
                }

                var details = response.PredefinedSetupDetails;

                DwgFileVersion = details.FileVersion;
                DwgTargetUnit = details.TargetUnit;
                DwgMergedViews = details.MergedViews;
                DwgSharedCoords = details.SharedCoords;
                DwgExportingAreas = details.ExportingAreas;
                DwgHideScopeBox = details.HideScopeBox;
                DwgHideReferencePlane = details.HideReferencePlane;
                DwgLineScaling = details.LineScaling;
                DwgColors = details.Colors;
                DwgPropOverrides = details.PropOverrides;

                StatusMessage = $"Ready. Showing '{setupName}' settings, read-only while this setup is selected.";
            });
        }

        public ExportJobSettings ToModel()
        {
            var scheduledTime = TimeSpan.TryParseExact(ScheduledTimeText, @"hh\:mm", CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : new TimeSpan(18, 0, 0);

            return new ExportJobSettings
            {
                JobId = JobId,
                JobName = JobName ?? string.Empty,
                Enabled = Enabled,
                ExportPdf = ExportPdf,
                ExportDwg = ExportDwg,
                SourceMode = SourceMode,
                UseActiveViewOrSheet = UseActiveViewOrSheet,
                SelectedSheetNumbers = new List<string>(_selectedSheetNumbers),
                SelectedViewUniqueIds = new List<string>(_selectedViewUniqueIds),
                NamingTemplate = NamingTemplate ?? string.Empty,
                DateFormat = DateFormat ?? "yyyyMMdd",
                OutputFolderTemplate = OutputFolderTemplate ?? string.Empty,
                ScheduleEnabled = ScheduleEnabled,
                ScheduledTimeOfDay = scheduledTime,
                CatchUpWindow = TimeSpan.FromMinutes(Math.Max(0, CatchUpMinutes)),
                ScheduledDays = Days.Where(d => d.IsSelected).Select(d => d.Day).ToList(),
                DwgSettings = new DwgSettings
                {
                    FileVersion = DwgFileVersion,
                    LayerMapping = DwgLayerMapping,
                    TargetUnit = DwgTargetUnit,
                    MergedViews = DwgMergedViews,
                    SharedCoords = DwgSharedCoords,
                    ExportingAreas = DwgExportingAreas,
                    HideScopeBox = DwgHideScopeBox,
                    HideReferencePlane = DwgHideReferencePlane,
                    LineScaling = DwgLineScaling,
                    Colors = DwgColors,
                    PropOverrides = DwgPropOverrides,
                    PredefinedSetupName = _dwgPredefinedSetupName ?? string.Empty
                },
                PdfSettings = new PdfSettings
                {
                    ColorDepth = PdfColorDepth,
                    ExportQuality = PdfExportQuality,
                    ZoomType = PdfZoomType,
                    ZoomPercentage = PdfZoomPercentage,
                    AlwaysUseRaster = PdfAlwaysUseRaster,
                    HideCropBoundaries = PdfHideCropBoundaries,
                    HideScopeBoxes = PdfHideScopeBoxes,
                    HideReferencePlane = PdfHideReferencePlane,
                    ViewLinksInBlue = PdfViewLinksInBlue
                }
            };
        }

        private void BrowseOutputFolder()
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Output Folder"
            };

            if (!string.IsNullOrWhiteSpace(OutputFolderTemplate) &&
                !OutputFolderTemplate.Contains("{") &&
                Directory.Exists(OutputFolderTemplate))
            {
                dialog.InitialDirectory = OutputFolderTemplate;
            }

            if (dialog.ShowDialog() == true)
            {
                OutputFolderTemplate = dialog.FolderName;
            }
        }

        private void PickSheets()
        {
            IsBusy = true;
            StatusMessage = "Loading sheets...";

            var request = new ExportUiRequest { Action = ExportUiAction.GetAllSheets };

            ExportUiBridge.Submit(request, response =>
            {
                IsBusy = false;

                if (!response.Success)
                {
                    StatusMessage = $"Failed to load sheets: {response.ErrorMessage}";
                    return;
                }

                StatusMessage = "Ready.";

                var picker = new BA.Views.Export.SheetPickerWindow(
                    response.Sheets,
                    _selectedSheetNumbers,
                    RequestDiscoverColumnsForPicker,
                    RequestResolveColumnValuesForPicker,
                    RequestPaperSizeInfoForPicker);

                var activeWindow = System.Windows.Application.Current?.Windows
                    .OfType<System.Windows.Window>()
                    .FirstOrDefault(w => w.IsActive);

                if (activeWindow != null)
                {
                    picker.Owner = activeWindow;
                }

                if (picker.ShowDialog() == true)
                {
                    _selectedSheetNumbers = picker.GetSelectedSheetNumbers();
                    OnPropertyChanged(nameof(SheetSelectionSummary));
                }
            });
        }

        private void PickViews()
        {
            IsBusy = true;
            StatusMessage = "Loading views...";

            var request = new ExportUiRequest { Action = ExportUiAction.GetAllViews };

            ExportUiBridge.Submit(request, response =>
            {
                IsBusy = false;

                if (!response.Success)
                {
                    StatusMessage = $"Failed to load views: {response.ErrorMessage}";
                    return;
                }

                StatusMessage = "Ready.";

                var picker = new BA.Views.Export.ViewPickerWindow(response.Views, _selectedViewUniqueIds);

                var activeWindow = System.Windows.Application.Current?.Windows
                    .OfType<System.Windows.Window>()
                    .FirstOrDefault(w => w.IsActive);

                if (activeWindow != null)
                {
                    picker.Owner = activeWindow;
                }

                if (picker.ShowDialog() == true)
                {
                    _selectedViewUniqueIds = picker.GetSelectedViewUniqueIds();
                    OnPropertyChanged(nameof(ViewSelectionSummary));
                }
            });
        }

        private static readonly List<string> BuiltInNamingTokens = new List<string>
        {
            "SheetNumber", "SheetName", "ProjectNumber", "ProjectName", "Date", "Revision"
        };

        private static readonly List<string> BuiltInViewNamingTokens = new List<string>
        {
            "ViewName", "ViewType", "ProjectNumber", "ProjectName", "Date"
        };

        private void EditNaming()
        {
            if (SourceMode == ExportSourceMode.Views)
            {
                EditNamingForViews();
            }
            else
            {
                EditNamingForSheets();
            }
        }

        private void EditNamingForSheets()
        {
            if (_selectedSheetNumbers.Count == 0)
            {
                StatusMessage = "Pick sheets first, the parameter list comes from a sheet in this job's selection.";
                return;
            }

            IsBusy = true;
            StatusMessage = "Loading parameters...";

            var request = new ExportUiRequest
            {
                Action = ExportUiAction.GetSheetParameterNames,
                SampleSheetNumber = _selectedSheetNumbers[0]
            };

            ExportUiBridge.Submit(request, response =>
            {
                IsBusy = false;

                if (!response.Success)
                {
                    StatusMessage = $"Failed to load parameters: {response.ErrorMessage}";
                    return;
                }

                StatusMessage = "Ready.";

                var availableNames = BuiltInNamingTokens
                    .Concat(response.StringList.Where(n => !BuiltInNamingTokens.Contains(n, StringComparer.OrdinalIgnoreCase)))
                    .ToList();

                OpenNamingBuilder(availableNames);
            });
        }

        private void EditNamingForViews()
        {
            if (_selectedViewUniqueIds.Count == 0)
            {
                StatusMessage = "Pick views first, the parameter list comes from a view in this job's selection.";
                return;
            }

            IsBusy = true;
            StatusMessage = "Loading parameters...";

            var request = new ExportUiRequest
            {
                Action = ExportUiAction.GetViewParameterNames,
                SampleViewUniqueId = _selectedViewUniqueIds[0]
            };

            ExportUiBridge.Submit(request, response =>
            {
                IsBusy = false;

                if (!response.Success)
                {
                    StatusMessage = $"Failed to load parameters: {response.ErrorMessage}";
                    return;
                }

                StatusMessage = "Ready.";

                var availableNames = BuiltInViewNamingTokens
                    .Concat(response.StringList.Where(n => !BuiltInViewNamingTokens.Contains(n, StringComparer.OrdinalIgnoreCase)))
                    .ToList();

                OpenNamingBuilder(availableNames);
            });
        }

        private void OpenNamingBuilder(List<string> availableNames)
        {
            var builder = new BA.Views.Export.NamingTemplateBuilderWindow(
                NamingTemplate ?? string.Empty,
                availableNames,
                RequestPreviewForBuilder);

            var activeWindow = System.Windows.Application.Current?.Windows
                .OfType<System.Windows.Window>()
                .FirstOrDefault(w => w.IsActive);

            if (activeWindow != null)
            {
                builder.Owner = activeWindow;
            }

            if (builder.ShowDialog() == true)
            {
                NamingTemplate = builder.ResultTemplate;
            }
        }

        /// <summary>
        /// Passed into NamingTemplateBuilderWindow so it can preview a
        /// candidate template without ever touching ExportUiBridge or
        /// Document itself. Builds a throwaway job snapshot with only the
        /// naming template swapped out, everything else (sheet set, date
        /// format, enabled formats, and so on) comes from this editor's
        /// current state, so the preview reflects every format currently
        /// enabled on this job, not just one.
        /// </summary>
        private void RequestPreviewForBuilder(string candidateTemplate, Action<List<NamingPreviewResult>> callback)
        {
            var jobSnapshot = ToModel();
            jobSnapshot.NamingTemplate = candidateTemplate;

            var request = new ExportUiRequest
            {
                Action = ExportUiAction.PreviewNaming,
                JobForPreviewOrRun = jobSnapshot
            };

            ExportUiBridge.Submit(request, response =>
            {
                callback(response.PreviewResults ?? new List<NamingPreviewResult>());
            });
        }

        private void RequestDiscoverColumnsForPicker(IList<string> sheetNumbers, Action<List<ParameterColumnCandidate>> callback)
        {
            var request = new ExportUiRequest
            {
                Action = ExportUiAction.DiscoverParameterColumns,
                SheetNumbersForColumns = sheetNumbers
            };

            ExportUiBridge.Submit(request, response =>
            {
                callback(response.Success ? response.ParameterColumnCandidates : new List<ParameterColumnCandidate>());
            });
        }

        private void RequestResolveColumnValuesForPicker(
            IList<string> sheetNumbers,
            IList<ParameterColumnDescriptor> columns,
            Action<Dictionary<string, Dictionary<string, string>>> callback)
        {
            var request = new ExportUiRequest
            {
                Action = ExportUiAction.ResolveParameterColumnValues,
                SheetNumbersForColumns = sheetNumbers,
                ColumnsToResolve = columns
            };

            ExportUiBridge.Submit(request, response =>
            {
                callback(response.Success ? response.ParameterColumnValues : new Dictionary<string, Dictionary<string, string>>());
            });
        }

        private void RequestPaperSizeInfoForPicker(IList<string> sheetNumbers, Action<Dictionary<string, PaperSizeInfo>> callback)
        {
            var request = new ExportUiRequest
            {
                Action = ExportUiAction.GetPaperSizeInfo,
                SheetNumbersForColumns = sheetNumbers
            };

            ExportUiBridge.Submit(request, response =>
            {
                callback(response.Success ? response.PaperSizeInfoBySheet : new Dictionary<string, PaperSizeInfo>());
            });
        }

        private void RunPreview()
        {
            IsBusy = true;
            StatusMessage = "Resolving preview...";

            var request = new ExportUiRequest
            {
                Action = ExportUiAction.PreviewNaming,
                JobForPreviewOrRun = ToModel()
            };

            ExportUiBridge.Submit(request, response =>
            {
                IsBusy = false;

                var results = response.PreviewResults ?? new List<NamingPreviewResult>();

                if (response.Success && results.Count > 0)
                {
                    PreviewFileName = string.Join("\n", results.Select(r => $"{r.Format}: {r.FileName}"));
                    PreviewFolder = string.Join("\n", results.Select(r => $"{r.Format}: {r.Folder}"));
                    StatusMessage = "Preview updated.";
                }
                else
                {
                    PreviewFileName = string.Empty;
                    PreviewFolder = string.Empty;

                    var firstError = results.FirstOrDefault(r => !r.Success)?.ErrorMessage;
                    StatusMessage = $"Preview failed: {firstError ?? response.ErrorMessage}";
                }
            });
        }

        private void RunNow()
        {
            IsBusy = true;
            StatusMessage = "Running export...";

            var request = new ExportUiRequest
            {
                Action = ExportUiAction.RunJobNow,
                JobForPreviewOrRun = ToModel()
            };

            ExportUiBridge.Submit(request, response =>
            {
                IsBusy = false;

                var results = response.JobResults ?? new List<ExportJobResult>();

                if (results.Count == 0)
                {
                    StatusMessage = $"Run failed: {response.ErrorMessage}";
                    return;
                }

                var detail = new System.Text.StringBuilder();

                foreach (var result in results)
                {
                    if (result.HasJobLevelError)
                    {
                        detail.AppendLine($"{result.Format}: FAILED - {result.JobLevelError}");
                        continue;
                    }

                    detail.AppendLine($"{result.Format}: {result.SuccessCount} succeeded, {result.FailureCount} failed.");

                    foreach (var outcome in result.Outcomes)
                    {
                        if (!outcome.Success)
                        {
                            detail.AppendLine($"  {outcome.SheetNumber}: {outcome.ErrorMessage}");
                        }
                    }
                }

                StatusMessage = detail.ToString().TrimEnd();
            });
        }
    }
}
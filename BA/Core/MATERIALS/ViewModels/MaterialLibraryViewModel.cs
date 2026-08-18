// Path: BA\Materials\UI\MaterialLibraryViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;
using Autodesk.Revit.UI;
using BA.Materials.Models;
using BA.UI.ExternalEvents;

namespace BA.Materials.UI
{
    public sealed class FillPatternOption
    {
        public ElementId PatternId { get; set; } = ElementId.InvalidElementId;
        public string DisplayName { get; set; } = "<none>";
    }

    public sealed class MaterialLibraryViewModel : BA.UI.Mvvm.ObservableObject, IDisposable
    {
        // --- Fixed category taxonomy, matches BA_MaterialLibrary's folder structure ---
        private static readonly string[] FixedCategories =
        {
            "Asphalt", "Concrete", "Glass", "Insulation", "Masonry",
            "Metal", "Paint", "Stone", "Textile", "Tile", "Wood"
        };
        private const string AllCategoriesLabel = "All materials";

        private readonly RevitExternalInvoker _invoker;
        private readonly LibraryDocumentService _libraryDocumentService;
        private readonly MaterialIdentityService _identityService;
        private readonly MaterialGraphicsService _graphicsService;
        private readonly MaterialAssetReader _assetReader;
        private readonly MaterialAssetWriter _assetWriter;
        private readonly LibraryCopyService _copyService;

        private readonly MaterialWriteDebouncer _appearanceDebouncer;
        private bool _isPopulatingSelection;
        private ElementId _currentAppearanceAssetElementId = ElementId.InvalidElementId;

        public MaterialLibraryViewModel(
            RevitExternalInvoker invoker,
            LibraryDocumentService libraryDocumentService,
            MaterialIdentityService identityService,
            MaterialGraphicsService graphicsService,
            MaterialAssetReader assetReader,
            MaterialAssetWriter assetWriter,
            LibraryCopyService copyService)
        {
            _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
            _libraryDocumentService = libraryDocumentService ?? throw new ArgumentNullException(nameof(libraryDocumentService));
            _identityService = identityService ?? throw new ArgumentNullException(nameof(identityService));
            _graphicsService = graphicsService ?? throw new ArgumentNullException(nameof(graphicsService));
            _assetReader = assetReader ?? throw new ArgumentNullException(nameof(assetReader));
            _assetWriter = assetWriter ?? throw new ArgumentNullException(nameof(assetWriter));
            _copyService = copyService ?? throw new ArgumentNullException(nameof(copyService));

            _appearanceDebouncer = new MaterialWriteDebouncer(OnAppearanceDebouncedFlush, TimeSpan.FromMilliseconds(200));

            Categories = new ObservableCollection<string>();
            Categories.Add(AllCategoriesLabel);
            foreach (string c in FixedCategories) Categories.Add(c);

            AllMaterials = new List<MaterialLibraryEntry>();
            FilteredMaterials = new ObservableCollection<MaterialLibraryEntry>();
            AvailableDraftingPatterns = new ObservableCollection<FillPatternOption>();

            _selectedCategory = AllCategoriesLabel;

            LoadLibraryCommand = new BA.UI.Mvvm.RelayCommand(ExecuteLoadLibrary, () => !IsBusy);
            NewMaterialCommand = new BA.UI.Mvvm.RelayCommand(ExecuteNewMaterial, () => !IsBusy && IsLibraryLoaded && !string.IsNullOrWhiteSpace(NewMaterialName));
            LoadIntoProjectCommand = new BA.UI.Mvvm.RelayCommand(ExecuteLoadIntoProject, () => !IsBusy && SelectedMaterial != null);
            CommitIdentityCommand = new BA.UI.Mvvm.RelayCommand(ExecuteCommitIdentity, () => !IsBusy && SelectedMaterial != null);
            CommitGraphicsCommand = new BA.UI.Mvvm.RelayCommand(ExecuteCommitGraphics, () => !IsBusy && SelectedMaterial != null);
            LoadFromAssetLibraryCommand = new BA.UI.Mvvm.RelayCommand(ExecuteLoadFromAssetLibrary, () => !IsBusy && SelectedMaterial != null);
        }

        /// <summary>
        /// Raised when Load Into Project detects a name conflict. Window subscribes and
        /// shows the resolution UI, calling back onKeep or onOverwrite with the user's
        /// choice. Currently backed by a plain MessageBox in the window's code-behind,
        /// see class remarks for why the styled modal from the mockup isn't wired yet.
        /// </summary>
        public event Action<string, Action, Action> ConflictDetected;

        // ------------------------------------------------------------------
        // Collections and selection
        // ------------------------------------------------------------------

        public ObservableCollection<string> Categories { get; }
        private List<MaterialLibraryEntry> AllMaterials { get; set; }
        public ObservableCollection<MaterialLibraryEntry> FilteredMaterials { get; }
        public ObservableCollection<FillPatternOption> AvailableDraftingPatterns { get; }

        private string _selectedCategory;
        public string SelectedCategory
        {
            get => _selectedCategory;
            set { if (SetProperty(ref _selectedCategory, value)) RefreshFilteredMaterials(); }
        }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set { if (SetProperty(ref _searchText, value)) RefreshFilteredMaterials(); }
        }

        private MaterialLibraryEntry _selectedMaterial;
        public MaterialLibraryEntry SelectedMaterial
        {
            get => _selectedMaterial;
            set
            {
                if (SetProperty(ref _selectedMaterial, value))
                {
                    RaiseCommandsCanExecuteChanged();
                    if (value != null) LoadSelectedMaterialDetail(value.MaterialId);
                }
            }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set { if (SetProperty(ref _isBusy, value)) RaiseCommandsCanExecuteChanged(); }
        }

        private bool _isLibraryLoaded;
        public bool IsLibraryLoaded
        {
            get => _isLibraryLoaded;
            private set => SetProperty(ref _isLibraryLoaded, value);
        }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        private string _newMaterialName = string.Empty;
        public string NewMaterialName
        {
            get => _newMaterialName;
            set { if (SetProperty(ref _newMaterialName, value)) NewMaterialCommand.RaiseCanExecuteChanged(); }
        }

        // ------------------------------------------------------------------
        // Identity fields
        // ------------------------------------------------------------------

        private string _identityName = string.Empty;
        public string IdentityName { get => _identityName; set => SetProperty(ref _identityName, value); }

        private string _identityClass = string.Empty;
        public string IdentityClass { get => _identityClass; set => SetProperty(ref _identityClass, value); }

        private string _identityDescription = string.Empty;
        public string IdentityDescription { get => _identityDescription; set => SetProperty(ref _identityDescription, value); }

        private string _identityKeynote = string.Empty;
        public string IdentityKeynote { get => _identityKeynote; set => SetProperty(ref _identityKeynote, value); }

        // ------------------------------------------------------------------
        // Graphics fields
        // ------------------------------------------------------------------

        private bool _useRenderAppearanceForShading;
        public bool UseRenderAppearanceForShading { get => _useRenderAppearanceForShading; set => SetProperty(ref _useRenderAppearanceForShading, value); }

        private System.Windows.Media.Color _shadingColor = System.Windows.Media.Color.FromRgb(120, 120, 120);
        public System.Windows.Media.Color ShadingColor { get => _shadingColor; set => SetProperty(ref _shadingColor, value); }

        private int _graphicsTransparency;
        public int GraphicsTransparency { get => _graphicsTransparency; set => SetProperty(ref _graphicsTransparency, value); }

        private FillPatternOption _surfaceForegroundPattern;
        public FillPatternOption SurfaceForegroundPattern { get => _surfaceForegroundPattern; set => SetProperty(ref _surfaceForegroundPattern, value); }
        private System.Windows.Media.Color _surfaceForegroundColor = System.Windows.Media.Color.FromRgb(120, 120, 120);
        public System.Windows.Media.Color SurfaceForegroundColor { get => _surfaceForegroundColor; set => SetProperty(ref _surfaceForegroundColor, value); }

        private FillPatternOption _surfaceBackgroundPattern;
        public FillPatternOption SurfaceBackgroundPattern { get => _surfaceBackgroundPattern; set => SetProperty(ref _surfaceBackgroundPattern, value); }
        private System.Windows.Media.Color _surfaceBackgroundColor = System.Windows.Media.Color.FromRgb(120, 120, 120);
        public System.Windows.Media.Color SurfaceBackgroundColor { get => _surfaceBackgroundColor; set => SetProperty(ref _surfaceBackgroundColor, value); }

        private FillPatternOption _cutForegroundPattern;
        public FillPatternOption CutForegroundPattern { get => _cutForegroundPattern; set => SetProperty(ref _cutForegroundPattern, value); }
        private System.Windows.Media.Color _cutForegroundColor = System.Windows.Media.Color.FromRgb(120, 120, 120);
        public System.Windows.Media.Color CutForegroundColor { get => _cutForegroundColor; set => SetProperty(ref _cutForegroundColor, value); }

        private FillPatternOption _cutBackgroundPattern;
        public FillPatternOption CutBackgroundPattern { get => _cutBackgroundPattern; set => SetProperty(ref _cutBackgroundPattern, value); }
        private System.Windows.Media.Color _cutBackgroundColor = System.Windows.Media.Color.FromRgb(120, 120, 120);
        public System.Windows.Media.Color CutBackgroundColor { get => _cutBackgroundColor; set => SetProperty(ref _cutBackgroundColor, value); }

        // ------------------------------------------------------------------
        // Appearance fields, every setter feeds the debouncer, not Revit directly
        // ------------------------------------------------------------------

        private System.Windows.Media.Color _albedoColor = System.Windows.Media.Color.FromRgb(200, 200, 200);
        public System.Windows.Media.Color AlbedoColor { get => _albedoColor; set { if (SetProperty(ref _albedoColor, value)) PushAppearanceChange(); } }

        private double _roughness = 0.5;
        public double Roughness { get => _roughness; set { if (SetProperty(ref _roughness, value)) PushAppearanceChange(); } }

        private double _reflectivity;
        public double Reflectivity { get => _reflectivity; set { if (SetProperty(ref _reflectivity, value)) PushAppearanceChange(); } }

        private double _bumpAmount;
        public double BumpAmount { get => _bumpAmount; set { if (SetProperty(ref _bumpAmount, value)) PushAppearanceChange(); } }

        private System.Windows.Media.Color _emissiveColor = System.Windows.Media.Color.FromRgb(0, 0, 0);
        public System.Windows.Media.Color EmissiveColor { get => _emissiveColor; set { if (SetProperty(ref _emissiveColor, value)) PushAppearanceChange(); } }

        private double _emissiveLuminanceCdM2;
        public double EmissiveLuminanceCdM2 { get => _emissiveLuminanceCdM2; set { if (SetProperty(ref _emissiveLuminanceCdM2, value)) PushAppearanceChange(); } }

        private double _appearanceTransparency;
        public double AppearanceTransparency { get => _appearanceTransparency; set { if (SetProperty(ref _appearanceTransparency, value)) PushAppearanceChange(); } }

        private double _cutoutOpacity = 1.0;
        public double CutoutOpacity { get => _cutoutOpacity; set { if (SetProperty(ref _cutoutOpacity, value)) PushAppearanceChange(); } }

        // ------------------------------------------------------------------
        // Commands
        // ------------------------------------------------------------------

        public BA.UI.Mvvm.RelayCommand LoadLibraryCommand { get; }
        public BA.UI.Mvvm.RelayCommand NewMaterialCommand { get; }
        public BA.UI.Mvvm.RelayCommand LoadIntoProjectCommand { get; }
        public BA.UI.Mvvm.RelayCommand CommitIdentityCommand { get; }
        public BA.UI.Mvvm.RelayCommand CommitGraphicsCommand { get; }
        public BA.UI.Mvvm.RelayCommand LoadFromAssetLibraryCommand { get; }

        private void RaiseCommandsCanExecuteChanged()
        {
            LoadLibraryCommand.RaiseCanExecuteChanged();
            NewMaterialCommand.RaiseCanExecuteChanged();
            LoadIntoProjectCommand.RaiseCanExecuteChanged();
            CommitIdentityCommand.RaiseCanExecuteChanged();
            CommitGraphicsCommand.RaiseCanExecuteChanged();
            LoadFromAssetLibraryCommand.RaiseCanExecuteChanged();
        }

        private sealed class LoadLibraryResult
        {
            public bool Success;
            public string FailureReason = string.Empty;
            public List<MaterialLibraryEntry> Materials = new List<MaterialLibraryEntry>();
        }

        private void ExecuteLoadLibrary()
        {
            IsBusy = true;
            StatusMessage = "Opening material library...";

            _invoker.Run(
                uiApp =>
                {
                    var openResult = _libraryDocumentService.OpenForEditing(uiApp, requestWriteAccess: true, allowStaleOverride: false);

                    var result = new LoadLibraryResult { Success = openResult.Success, FailureReason = openResult.FailureReason };

                    if (openResult.Success)
                    {
                        Document doc = _libraryDocumentService.LibraryDocument;
                        result.Materials = new FilteredElementCollector(doc)
                            .OfClass(typeof(Material))
                            .Cast<Material>()
                            .Select(m => new MaterialLibraryEntry
                            {
                                MaterialId = m.Id,
                                Name = m.Name,
                                MaterialClass = m.MaterialClass ?? string.Empty,
                                HasAppearanceAsset = m.AppearanceAssetId != ElementId.InvalidElementId
                            })
                            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                            .ToList();
                    }

                    return result;
                },
                onCompleted: result =>
                {
                    RunOnUiThread(() =>
                    {
                        IsBusy = false;

                        if (!result.Success)
                        {
                            StatusMessage = result.FailureReason;
                            IsLibraryLoaded = false;
                            return;
                        }

                        AllMaterials = result.Materials;
                        IsLibraryLoaded = true;
                        StatusMessage = $"Loaded {AllMaterials.Count} materials.";
                        RefreshFilteredMaterials();
                    });
                },
                onError: ex =>
                {
                    RunOnUiThread(() =>
                    {
                        IsBusy = false;
                        StatusMessage = "Failed to open material library. See BA Tools log for details.";
                    });
                });
        }

        private void RefreshFilteredMaterials()
        {
            IEnumerable<MaterialLibraryEntry> query = AllMaterials;

            if (!string.Equals(SelectedCategory, AllCategoriesLabel, StringComparison.Ordinal))
            {
                query = query.Where(e => string.Equals(e.MaterialClass, SelectedCategory, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                query = query.Where(e => e.Name.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            FilteredMaterials.Clear();
            foreach (var entry in query) FilteredMaterials.Add(entry);
        }

        private sealed class MaterialDetailDto
        {
            public MaterialIdentityInfo Identity;
            public MaterialGraphicsInfo Graphics;
            public MaterialChannelSet Channels;
            public ElementId AppearanceAssetElementId = ElementId.InvalidElementId;
            public List<FillPatternOption> Patterns = new List<FillPatternOption>();
        }

        public void LoadSelectedMaterialDetail(ElementId materialId)
        {
            IsBusy = true;

            _invoker.Run(
                uiApp =>
                {
                    Document doc = _libraryDocumentService.LibraryDocument;
                    Material material = doc.GetElement(materialId) as Material;

                    if (material == null)
                        return null;

                    var dto = new MaterialDetailDto
                    {
                        Identity = _identityService.GetIdentity(material),
                        Graphics = _graphicsService.GetGraphics(material),
                        AppearanceAssetElementId = material.AppearanceAssetId
                    };

                    dto.Patterns = _graphicsService.GetAvailableDraftingPatterns(doc)
                        .Select(fp => new FillPatternOption { PatternId = fp.Id, DisplayName = fp.Name })
                        .ToList();
                    dto.Patterns.Insert(0, new FillPatternOption { PatternId = ElementId.InvalidElementId, DisplayName = "<none>" });

                    if (material.AppearanceAssetId != ElementId.InvalidElementId)
                    {
                        AppearanceAssetElement assetElement = doc.GetElement(material.AppearanceAssetId) as AppearanceAssetElement;
                        dto.Channels = assetElement != null
                            ? _assetReader.ReadChannelSet(assetElement, material.Name)
                            : new MaterialChannelSet { MaterialName = material.Name };
                    }
                    else
                    {
                        dto.Channels = new MaterialChannelSet { MaterialName = material.Name };
                    }

                    return dto;
                },
                onCompleted: (MaterialDetailDto dto) =>
                {
                    RunOnUiThread(() =>
                    {
                        IsBusy = false;
                        if (dto == null)
                        {
                            StatusMessage = "Selected material could not be loaded, it may have been deleted.";
                            return;
                        }

                        PopulateDetailPane(dto);
                    });
                },
                onError: ex =>
                {
                    RunOnUiThread(() =>
                    {
                        IsBusy = false;
                        StatusMessage = "Failed to load material detail. See BA Tools log for details.";
                    });
                });
        }

        private void PopulateDetailPane(MaterialDetailDto dto)
        {
            _isPopulatingSelection = true;
            try
            {
                IdentityName = dto.Identity.Name;
                IdentityClass = dto.Identity.MaterialClass;
                IdentityDescription = dto.Identity.Description;
                IdentityKeynote = dto.Identity.Keynote;

                UseRenderAppearanceForShading = dto.Graphics.UseRenderAppearanceForShading;
                ShadingColor = System.Windows.Media.Color.FromRgb(dto.Graphics.ShadingColorR, dto.Graphics.ShadingColorG, dto.Graphics.ShadingColorB);
                GraphicsTransparency = dto.Graphics.Transparency;

                AvailableDraftingPatterns.Clear();
                foreach (var p in dto.Patterns) AvailableDraftingPatterns.Add(p);

                SurfaceForegroundPattern = AvailableDraftingPatterns.FirstOrDefault(p => p.PatternId == dto.Graphics.SurfaceForegroundPatternId)
                                           ?? AvailableDraftingPatterns.First();
                SurfaceForegroundColor = System.Windows.Media.Color.FromRgb(dto.Graphics.SurfaceForegroundColorR, dto.Graphics.SurfaceForegroundColorG, dto.Graphics.SurfaceForegroundColorB);

                SurfaceBackgroundPattern = AvailableDraftingPatterns.FirstOrDefault(p => p.PatternId == dto.Graphics.SurfaceBackgroundPatternId)
                                           ?? AvailableDraftingPatterns.First();
                SurfaceBackgroundColor = System.Windows.Media.Color.FromRgb(dto.Graphics.SurfaceBackgroundColorR, dto.Graphics.SurfaceBackgroundColorG, dto.Graphics.SurfaceBackgroundColorB);

                CutForegroundPattern = AvailableDraftingPatterns.FirstOrDefault(p => p.PatternId == dto.Graphics.CutForegroundPatternId)
                                       ?? AvailableDraftingPatterns.First();
                CutForegroundColor = System.Windows.Media.Color.FromRgb(dto.Graphics.CutForegroundColorR, dto.Graphics.CutForegroundColorG, dto.Graphics.CutForegroundColorB);

                CutBackgroundPattern = AvailableDraftingPatterns.FirstOrDefault(p => p.PatternId == dto.Graphics.CutBackgroundPatternId)
                                       ?? AvailableDraftingPatterns.First();
                CutBackgroundColor = System.Windows.Media.Color.FromRgb(dto.Graphics.CutBackgroundColorR, dto.Graphics.CutBackgroundColorG, dto.Graphics.CutBackgroundColorB);

                _currentAppearanceAssetElementId = dto.AppearanceAssetElementId;

                AlbedoColor = System.Windows.Media.Color.FromRgb(dto.Channels.AlbedoR, dto.Channels.AlbedoG, dto.Channels.AlbedoB);
                Roughness = dto.Channels.Roughness;
                Reflectivity = dto.Channels.Reflectivity;
                BumpAmount = dto.Channels.BumpAmount;
                EmissiveColor = System.Windows.Media.Color.FromRgb(dto.Channels.EmissiveR, dto.Channels.EmissiveG, dto.Channels.EmissiveB);
                EmissiveLuminanceCdM2 = dto.Channels.EmissiveLuminanceCdM2;
                AppearanceTransparency = dto.Channels.Transparency;
                CutoutOpacity = dto.Channels.CutoutOpacity;
            }
            finally
            {
                _isPopulatingSelection = false;
            }
        }

        // ------------------------------------------------------------------
        // Appearance debounced write path
        // ------------------------------------------------------------------

        private void PushAppearanceChange()
        {
            if (_isPopulatingSelection || SelectedMaterial == null) return;

            var channels = new MaterialChannelSet
            {
                MaterialName = SelectedMaterial.Name,
                AlbedoR = AlbedoColor.R,
                AlbedoG = AlbedoColor.G,
                AlbedoB = AlbedoColor.B,
                Roughness = Roughness,
                Reflectivity = Reflectivity,
                BumpAmount = BumpAmount,
                EmissiveR = EmissiveColor.R,
                EmissiveG = EmissiveColor.G,
                EmissiveB = EmissiveColor.B,
                EmissiveLuminanceCdM2 = EmissiveLuminanceCdM2,
                Transparency = AppearanceTransparency,
                CutoutOpacity = CutoutOpacity
            };

            _appearanceDebouncer.Update(channels);
        }

        private void OnAppearanceDebouncedFlush(MaterialChannelSet channels)
        {
            if (_currentAppearanceAssetElementId == ElementId.InvalidElementId) return;

            ElementId assetId = _currentAppearanceAssetElementId;

            _invoker.Run(
                uiApp => _assetWriter.WriteChannels(_libraryDocumentService.LibraryDocument, assetId, channels),
                onCompleted: result =>
                {
                    if (!result.Success)
                    {
                        RunOnUiThread(() => StatusMessage = result.FailureReason);
                    }
                },
                onError: ex =>
                {
                    RunOnUiThread(() => StatusMessage = "Failed to write appearance changes. See BA Tools log for details.");
                });
        }

        /// <summary>Call from the window's Closing handler before SaveAndClose.</summary>
        public void FlushPendingAppearanceWrites()
        {
            _appearanceDebouncer.FlushImmediately();
        }

        // ------------------------------------------------------------------
        // Identity / Graphics commit (explicit, e.g. on TextBox LostFocus)
        // ------------------------------------------------------------------

        private void ExecuteCommitIdentity()
        {
            if (SelectedMaterial == null) return;

            var info = new MaterialIdentityInfo
            {
                Name = IdentityName,
                MaterialClass = IdentityClass,
                Description = IdentityDescription,
                Keynote = IdentityKeynote
            };

            ElementId materialId = SelectedMaterial.MaterialId;

            _invoker.Run(
                uiApp => _identityService.SetIdentity(_libraryDocumentService.LibraryDocument, materialId, info),
                onCompleted: result =>
                {
                    RunOnUiThread(() =>
                    {
                        if (!result.Success)
                        {
                            StatusMessage = result.FailureReason;
                            return;
                        }

                        var entry = AllMaterials.FirstOrDefault(e => e.MaterialId == materialId);
                        if (entry != null)
                        {
                            entry.Name = info.Name;
                            entry.MaterialClass = info.MaterialClass;
                            RefreshFilteredMaterials();
                        }
                    });
                },
                onError: ex => RunOnUiThread(() => StatusMessage = "Failed to update identity. See BA Tools log for details."));
        }

        private void ExecuteCommitGraphics()
        {
            if (SelectedMaterial == null) return;

            var info = new MaterialGraphicsInfo
            {
                UseRenderAppearanceForShading = UseRenderAppearanceForShading,
                ShadingColorR = ShadingColor.R,
                ShadingColorG = ShadingColor.G,
                ShadingColorB = ShadingColor.B,
                Transparency = GraphicsTransparency,

                SurfaceForegroundPatternId = SurfaceForegroundPattern?.PatternId ?? ElementId.InvalidElementId,
                SurfaceForegroundColorR = SurfaceForegroundColor.R,
                SurfaceForegroundColorG = SurfaceForegroundColor.G,
                SurfaceForegroundColorB = SurfaceForegroundColor.B,

                SurfaceBackgroundPatternId = SurfaceBackgroundPattern?.PatternId ?? ElementId.InvalidElementId,
                SurfaceBackgroundColorR = SurfaceBackgroundColor.R,
                SurfaceBackgroundColorG = SurfaceBackgroundColor.G,
                SurfaceBackgroundColorB = SurfaceBackgroundColor.B,

                CutForegroundPatternId = CutForegroundPattern?.PatternId ?? ElementId.InvalidElementId,
                CutForegroundColorR = CutForegroundColor.R,
                CutForegroundColorG = CutForegroundColor.G,
                CutForegroundColorB = CutForegroundColor.B,

                CutBackgroundPatternId = CutBackgroundPattern?.PatternId ?? ElementId.InvalidElementId,
                CutBackgroundColorR = CutBackgroundColor.R,
                CutBackgroundColorG = CutBackgroundColor.G,
                CutBackgroundColorB = CutBackgroundColor.B
            };

            ElementId materialId = SelectedMaterial.MaterialId;

            _invoker.Run(
                uiApp => _graphicsService.SetGraphics(_libraryDocumentService.LibraryDocument, materialId, info),
                onCompleted: result =>
                {
                    if (!result.Success) RunOnUiThread(() => StatusMessage = result.FailureReason);
                },
                onError: ex => RunOnUiThread(() => StatusMessage = "Failed to update graphics. See BA Tools log for details."));
        }

        // ------------------------------------------------------------------
        // New Material
        // ------------------------------------------------------------------

        private void ExecuteNewMaterial()
        {
            string name = NewMaterialName;
            string category = string.Equals(SelectedCategory, AllCategoriesLabel, StringComparison.Ordinal)
                ? string.Empty
                : SelectedCategory;

            IsBusy = true;

            _invoker.Run(
                uiApp =>
                {
                    var seedProvider = new SeedAssetProvider();
                    var writer = new MaterialAssetWriter(seedProvider);
                    var createResult = writer.CreateNew(_libraryDocumentService.LibraryDocument, uiApp.Application, name);

                    if (createResult.Success && !string.IsNullOrEmpty(category))
                    {
                        _identityService.SetIdentity(_libraryDocumentService.LibraryDocument, createResult.MaterialId,
                            new MaterialIdentityInfo { Name = name, MaterialClass = category });
                    }

                    return createResult;
                },
                onCompleted: result =>
                {
                    RunOnUiThread(() =>
                    {
                        IsBusy = false;

                        if (!result.Success)
                        {
                            StatusMessage = result.FailureReason;
                            return;
                        }

                        var entry = new MaterialLibraryEntry
                        {
                            MaterialId = result.MaterialId,
                            Name = name,
                            MaterialClass = category,
                            HasAppearanceAsset = true
                        };
                        AllMaterials.Add(entry);
                        RefreshFilteredMaterials();
                        NewMaterialName = string.Empty;
                        SelectedMaterial = FilteredMaterials.FirstOrDefault(e => e.MaterialId == entry.MaterialId);
                        StatusMessage = $"Created material '{name}'.";
                    });
                },
                onError: ex =>
                {
                    RunOnUiThread(() =>
                    {
                        IsBusy = false;
                        StatusMessage = "Failed to create material. See BA Tools log for details.";
                    });
                });
        }

        // ------------------------------------------------------------------
        // Load Into Project
        // ------------------------------------------------------------------

        private void ExecuteLoadIntoProject()
        {
            if (SelectedMaterial == null) return;

            ElementId libraryMaterialId = SelectedMaterial.MaterialId;
            string materialName = SelectedMaterial.Name;

            IsBusy = true;

            _invoker.Run(
                uiApp =>
                {
                    Document activeDoc = uiApp.ActiveUIDocument?.Document;
                    if (_libraryDocumentService.IsLibraryDocument(activeDoc))
                        return new ConflictCheck { ActiveDocumentIsLibrary = true };

                    return new ConflictCheck
                    {
                        ConflictMaterialId = _copyService.FindExistingMaterialByName(activeDoc, materialName)
                    };
                },
                onCompleted: (ConflictCheck check) =>
                {
                    RunOnUiThread(() =>
                    {
                        IsBusy = false;

                        if (check.ActiveDocumentIsLibrary)
                        {
                            StatusMessage = "Switch to your project window first, the material library is currently the active document.";
                            return;
                        }

                        if (check.ConflictMaterialId == ElementId.InvalidElementId)
                        {
                            RunCopyAsNew(libraryMaterialId);
                            return;
                        }

                        ConflictDetected?.Invoke(
                            materialName,
                            () => StatusMessage = $"Kept existing project version of '{materialName}'.",
                            () => RunOverwriteExisting(libraryMaterialId, check.ConflictMaterialId));
                    });
                },
                onError: ex =>
                {
                    RunOnUiThread(() =>
                    {
                        IsBusy = false;
                        StatusMessage = "Failed to check for existing material in project. See BA Tools log for details.";
                    });
                });
        }

        private sealed class ConflictCheck
        {
            public bool ActiveDocumentIsLibrary;
            public ElementId ConflictMaterialId = ElementId.InvalidElementId;
        }

        private void RunCopyAsNew(ElementId libraryMaterialId)
        {
            IsBusy = true;

            _invoker.Run(
                uiApp =>
                {
                    Document activeDoc = uiApp.ActiveUIDocument?.Document;
                    if (_libraryDocumentService.IsLibraryDocument(activeDoc))
                    {
                        return new CopyIntoProjectResult
                        {
                            Success = false,
                            FailureReason = "Switch to your project window first, the material library is currently the active document."
                        };
                    }

                    return _copyService.CopyIntoProjectAsNew(_libraryDocumentService.LibraryDocument, activeDoc, libraryMaterialId);
                },
                onCompleted: result =>
                {
                    RunOnUiThread(() =>
                    {
                        IsBusy = false;
                        StatusMessage = result.Success
                            ? "Material loaded into project."
                            : result.FailureReason;
                    });
                },
                onError: ex => RunOnUiThread(() =>
                {
                    IsBusy = false;
                    StatusMessage = "Failed to load material into project. See BA Tools log for details.";
                }));
        }

        private void RunOverwriteExisting(ElementId libraryMaterialId, ElementId targetMaterialId)
        {
            IsBusy = true;

            _invoker.Run(
                uiApp =>
                {
                    Document activeDoc = uiApp.ActiveUIDocument?.Document;
                    if (_libraryDocumentService.IsLibraryDocument(activeDoc))
                    {
                        return new CopyIntoProjectResult
                        {
                            Success = false,
                            FailureReason = "Switch to your project window first, the material library is currently the active document."
                        };
                    }

                    return _copyService.OverwriteExisting(_libraryDocumentService.LibraryDocument, activeDoc, libraryMaterialId, targetMaterialId);
                },
                onCompleted: result =>
                {
                    RunOnUiThread(() =>
                    {
                        IsBusy = false;
                        StatusMessage = result.Success
                            ? "Project material overwritten from library."
                            : result.FailureReason;
                    });
                },
                onError: ex => RunOnUiThread(() =>
                {
                    IsBusy = false;
                    StatusMessage = "Failed to overwrite project material. See BA Tools log for details.";
                }));
        }

        // ------------------------------------------------------------------
        // Load from asset library, NOT YET IMPLEMENTED, see class remarks
        // ------------------------------------------------------------------

        // ------------------------------------------------------------------
        // Load from asset library
        // ------------------------------------------------------------------

        /// <summary>Raised with the target material's ElementId when the user clicks
        /// Load from asset library. Window owns opening the actual browser dialog and
        /// reloading the detail pane afterward, same pattern as ConflictDetected.</summary>
        public event Action<ElementId> AssetLibraryRequested;

        private void ExecuteLoadFromAssetLibrary()
        {
            if (SelectedMaterial == null) return;
            AssetLibraryRequested?.Invoke(SelectedMaterial.MaterialId);
        }

        // ------------------------------------------------------------------
        // Bulk class assignment (drag multiple materials onto a category)
        // ------------------------------------------------------------------

        /// <summary>
        /// Called directly from the window's category-drop handler, not through
        /// RelayCommand, a drag payload (list of ElementId + target category string)
        /// isn't a natural fit for the object-parameter ICommand pattern already in use
        /// elsewhere in this class.
        /// </summary>
        public void AssignCategoryToMaterials(List<ElementId> materialIds, string newCategory)
        {
            if (materialIds == null || materialIds.Count == 0) return;
            if (string.IsNullOrWhiteSpace(newCategory)) return;

            IsBusy = true;
            StatusMessage = $"Assigning {materialIds.Count} material(s) to '{newCategory}'...";

            var assignments = materialIds.ToDictionary(id => id, id => newCategory);

            _invoker.Run(
                uiApp => _identityService.SetMaterialClassBulk(_libraryDocumentService.LibraryDocument, assignments),
                onCompleted: result =>
                {
                    RunOnUiThread(() =>
                    {
                        IsBusy = false;

                        foreach (ElementId id in materialIds)
                        {
                            if (result.FailedIds.Contains(id)) continue;

                            var entry = AllMaterials.FirstOrDefault(e => e.MaterialId == id);
                            if (entry != null) entry.MaterialClass = newCategory;
                        }

                        RefreshFilteredMaterials();

                        int succeeded = materialIds.Count - result.FailedIds.Count;
                        StatusMessage = result.Success
                            ? $"Assigned {succeeded} material(s) to '{newCategory}'."
                            : $"Assigned {succeeded} material(s), {result.FailedIds.Count} failed: {result.FailureReason}";

                        // If the currently-open detail pane belongs to one of the
                        // materials just reassigned, keep the Identity Class field in
                        // sync without a full reload round trip.
                        if (SelectedMaterial != null
                            && materialIds.Contains(SelectedMaterial.MaterialId)
                            && !result.FailedIds.Contains(SelectedMaterial.MaterialId))
                        {
                            IdentityClass = newCategory;
                        }
                    });
                },
                onError: ex =>
                {
                    RunOnUiThread(() =>
                    {
                        IsBusy = false;
                        StatusMessage = "Failed to assign category. See BA Tools log for details.";
                    });
                });
        }

        // ------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------

        private static void RunOnUiThread(Action action)
        {
            Application current = Application.Current;
            if (current == null || current.Dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                current.Dispatcher.Invoke(action);
            }
        }

        public void Dispose()
        {
            _appearanceDebouncer.Dispose();
        }
    }
}

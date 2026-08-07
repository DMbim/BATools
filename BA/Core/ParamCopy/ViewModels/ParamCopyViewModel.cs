using Autodesk.Revit.UI;
using BATools.ParamCopy.Handlers;
using BATools.ParamCopy.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace BATools.ParamCopy.ViewModels
{
    public partial class ParamCopyViewModel : ObservableObject
    {
        private readonly ParamCopyExternalInvoker _invoker;
        private readonly System.Windows.Threading.Dispatcher _dispatcher;

        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BATools", "ParamCopy.json");

        // ── Categories (shared) ──────────────────────────────────────────────

        /// <summary>
        /// Model-type category names present in the active document. Shared
        /// between the Source and Dest category dropdowns; loaded once on
        /// construction.
        /// </summary>
        public ObservableCollection<string> AvailableCategories { get; } = new();

        // ── Source ────────────────────────────────────────────────────────────

        private string _sourceCategoryName = string.Empty;
        public string SourceCategoryName
        {
            get => _sourceCategoryName;
            set
            {
                if (SetProperty(ref _sourceCategoryName, value))
                    LoadSourceCategoryParameters();
            }
        }

        /// <summary>
        /// Instance parameter names for the current Source category, ignoring
        /// FilterSets. Backs the Source filter-rule ParameterName dropdowns.
        /// </summary>
        public ObservableCollection<string> SourceCategoryParameterNames { get; } = new();

        /// <summary>
        /// Instance parameter names for elements matching the current Source
        /// category + FilterSets. Backs Source Display Params, the Mapping
        /// grid's Source column, and feeds PairingParameterCandidates.
        /// Refreshed on Reload Source.
        /// </summary>
        public ObservableCollection<string> SourceMatchedParameterNames { get; } = new();

        /// <summary>Checkbox entries for the Source Display Params multi-select.</summary>
        public ObservableCollection<CheckableParam> SourceDisplayParamOptions { get; } = new();

        private string _sourceDisplayParamsSummary = "None selected";
        public string SourceDisplayParamsSummary
        {
            get => _sourceDisplayParamsSummary;
            private set => SetProperty(ref _sourceDisplayParamsSummary, value);
        }

        /// <summary>
        /// Display param names carried over from a loaded settings file before
        /// the matched-parameter list has been populated by a Reload. Applied
        /// (and cleared) the first time RebuildSourceDisplayParamOptions runs,
        /// so saved selections are never silently dropped just because the
        /// user hasn't clicked Reload yet.
        /// </summary>
        private List<string> _pendingSourceDisplayParamNames = new();

        /// <summary>Fired after source reload with the current display param names.</summary>
        public event Action<IReadOnlyList<string>>? SourceColumnsChanged;

        /// <summary>Fired after dest reload with the current display param names.</summary>
        public event Action<IReadOnlyList<string>>? DestColumnsChanged;

        public ObservableCollection<FilterSet> SourceFilterSets { get; } = new();
        public ObservableCollection<ElementListItem> SourceElements { get; } = new();

        // ── Dest ──────────────────────────────────────────────────────────────

        private string _destCategoryName = string.Empty;
        public string DestCategoryName
        {
            get => _destCategoryName;
            set
            {
                if (SetProperty(ref _destCategoryName, value))
                    LoadDestCategoryParameters();
            }
        }

        /// <summary>
        /// Instance parameter names for the current Dest category, ignoring
        /// FilterSets. Backs the Dest filter-rule ParameterName dropdowns.
        /// </summary>
        public ObservableCollection<string> DestCategoryParameterNames { get; } = new();

        /// <summary>
        /// Instance parameter names for elements matching the current Dest
        /// category + FilterSets. Backs Dest Display Params, the Mapping
        /// grid's Dest column, and feeds PairingParameterCandidates.
        /// Refreshed on Reload Dest.
        /// </summary>
        public ObservableCollection<string> DestMatchedParameterNames { get; } = new();

        /// <summary>Checkbox entries for the Dest Display Params multi-select.</summary>
        public ObservableCollection<CheckableParam> DestDisplayParamOptions { get; } = new();

        private string _destDisplayParamsSummary = "None selected";
        public string DestDisplayParamsSummary
        {
            get => _destDisplayParamsSummary;
            private set => SetProperty(ref _destDisplayParamsSummary, value);
        }

        private List<string> _pendingDestDisplayParamNames = new();

        public ObservableCollection<FilterSet> DestFilterSets { get; } = new();
        public ObservableCollection<ElementListItem> DestElements { get; } = new();

        // ── Pairing ───────────────────────────────────────────────────────────

        private PairingMode _pairingMode = PairingMode.ByCommonParameter;
        public PairingMode PairingMode
        {
            get => _pairingMode;
            set => SetProperty(ref _pairingMode, value);
        }

        private string _pairingParameterName = string.Empty;
        public string PairingParameterName
        {
            get => _pairingParameterName;
            set => SetProperty(ref _pairingParameterName, value);
        }

        /// <summary>
        /// Candidate parameter names for pairing: the intersection of Source
        /// and Dest matched-parameter lists once both have been reloaded, or
        /// whichever side is populated if only one has. A previously-set
        /// PairingParameterName that falls outside the current candidates is
        /// appended rather than dropped, so an existing selection is never
        /// silently lost while the other side hasn't been reloaded yet.
        /// </summary>
        public ObservableCollection<string> PairingParameterCandidates { get; } = new();

        public IEnumerable<PairingMode> PairingModes { get; }
            = Enum.GetValues<PairingMode>();

        public ObservableCollection<ElementPair> Pairs { get; } = new();

        private ElementListItem? _selectedSourceItem;
        public ElementListItem? SelectedSourceItem
        {
            get => _selectedSourceItem;
            set => SetProperty(ref _selectedSourceItem, value);
        }

        private ElementListItem? _selectedDestItem;
        public ElementListItem? SelectedDestItem
        {
            get => _selectedDestItem;
            set => SetProperty(ref _selectedDestItem, value);
        }

        private ElementPair? _selectedPair;
        public ElementPair? SelectedPair
        {
            get => _selectedPair;
            set => SetProperty(ref _selectedPair, value);
        }

        // ── Mappings ──────────────────────────────────────────────────────────

        public ObservableCollection<ParamMapping> Mappings { get; } = new();

        private ParamMapping? _selectedMapping;
        public ParamMapping? SelectedMapping
        {
            get => _selectedMapping;
            set => SetProperty(ref _selectedMapping, value);
        }

        // ── Status ────────────────────────────────────────────────────────────

        private string _statusText = string.Empty;
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        // ── Commands ──────────────────────────────────────────────────────────

        public RelayCommand ReloadSourceCommand { get; }
        public RelayCommand ReloadDestCommand { get; }
        public RelayCommand AddSourceFilterSetCommand { get; }
        public RelayCommand AddDestFilterSetCommand { get; }
        public RelayCommand PairSelectedCommand { get; }
        public RelayCommand AutoPairCommand { get; }
        public RelayCommand RemovePairCommand { get; }
        public RelayCommand ClearPairsCommand { get; }
        public RelayCommand AddMappingCommand { get; }
        public RelayCommand RemoveMappingCommand { get; }
        public RelayCommand RunCopyCommand { get; }
        public RelayCommand SaveSettingsCommand { get; }
        public RelayCommand LoadSettingsCommand { get; }

        // ── Constructor ───────────────────────────────────────────────────────

        public ParamCopyViewModel(UIApplication uiApp)
        {
            _invoker = new ParamCopyExternalInvoker(uiApp);
            _dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;

            ReloadSourceCommand = new RelayCommand(ReloadSource);
            ReloadDestCommand = new RelayCommand(ReloadDest);
            AddSourceFilterSetCommand = new RelayCommand(() => SourceFilterSets.Add(new FilterSet()));
            AddDestFilterSetCommand = new RelayCommand(() => DestFilterSets.Add(new FilterSet()));
            PairSelectedCommand = new RelayCommand(PairSelected,
                () => SelectedSourceItem != null && SelectedDestItem != null);
            AutoPairCommand = new RelayCommand(AutoPair,
                () => !string.IsNullOrWhiteSpace(PairingParameterName));
            RemovePairCommand = new RelayCommand(
                () => { if (SelectedPair != null) Pairs.Remove(SelectedPair); },
                () => SelectedPair != null);
            ClearPairsCommand = new RelayCommand(() => Pairs.Clear());
            AddMappingCommand = new RelayCommand(() => Mappings.Add(new ParamMapping()));
            RemoveMappingCommand = new RelayCommand(
                () => { if (SelectedMapping != null) Mappings.Remove(SelectedMapping); },
                () => SelectedMapping != null);
            RunCopyCommand = new RelayCommand(RunCopy,
                () => Pairs.Count > 0 && Mappings.Count > 0);
            SaveSettingsCommand = new RelayCommand(SaveSettings);
            LoadSettingsCommand = new RelayCommand(LoadSettings);

            LoadCategories();
            LoadSettings();
        }

        // ── Categories ────────────────────────────────────────────────────────

        private void LoadCategories()
        {
            _invoker.LoadCategories(names =>
            {
                AvailableCategories.Clear();
                foreach (var n in names) AvailableCategories.Add(n);
            });
        }

        private void LoadSourceCategoryParameters()
        {
            SourceCategoryParameterNames.Clear();

            if (string.IsNullOrWhiteSpace(SourceCategoryName))
                return;

            _invoker.LoadSourceCategoryParameters(SourceCategoryName, names =>
            {
                SourceCategoryParameterNames.Clear();
                foreach (var n in names) SourceCategoryParameterNames.Add(n);
            });
        }

        private void LoadDestCategoryParameters()
        {
            DestCategoryParameterNames.Clear();

            if (string.IsNullOrWhiteSpace(DestCategoryName))
                return;

            _invoker.LoadDestCategoryParameters(DestCategoryName, names =>
            {
                DestCategoryParameterNames.Clear();
                foreach (var n in names) DestCategoryParameterNames.Add(n);
            });
        }

        // ── Reload ────────────────────────────────────────────────────────────

        private void ReloadSource()
        {
            IsLoading = true;
            StatusText = "Loading source elements...";
            var srcSettings = BuildSourceSettings();

            _invoker.ReloadSource(srcSettings, items =>
            {
                SourceElements.Clear();
                foreach (var i in items) SourceElements.Add(i);
                StatusText = $"Source: {items.Count} elements loaded.";
                IsLoading = false;
                SourceColumnsChanged?.Invoke(srcSettings.DisplayParameterNames);
            });

            _invoker.LoadSourceMatchedParameters(
                srcSettings.CategoryName, srcSettings.FilterSets, names =>
                {
                    SourceMatchedParameterNames.Clear();
                    foreach (var n in names) SourceMatchedParameterNames.Add(n);
                    RebuildSourceDisplayParamOptions();
                    RecomputePairingCandidates();
                });
        }

        private void ReloadDest()
        {
            IsLoading = true;
            StatusText = "Loading dest elements...";
            var dstSettings = BuildDestSettings();

            _invoker.ReloadDest(dstSettings, items =>
            {
                DestElements.Clear();
                foreach (var i in items) DestElements.Add(i);
                StatusText = $"Dest: {items.Count} elements loaded.";
                IsLoading = false;
                DestColumnsChanged?.Invoke(dstSettings.DisplayParameterNames);
            });

            _invoker.LoadDestMatchedParameters(
                dstSettings.CategoryName, dstSettings.FilterSets, names =>
                {
                    DestMatchedParameterNames.Clear();
                    foreach (var n in names) DestMatchedParameterNames.Add(n);
                    RebuildDestDisplayParamOptions();
                    RecomputePairingCandidates();
                });
        }

        // ── Display Params (checkbox multi-select) ───────────────────────────

        private void RebuildSourceDisplayParamOptions()
        {
            var keepSelected = SourceDisplayParamOptions
                .Where(p => p.IsSelected)
                .Select(p => p.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var pending in _pendingSourceDisplayParamNames)
                keepSelected.Add(pending);
            _pendingSourceDisplayParamNames.Clear();

            foreach (var item in SourceDisplayParamOptions)
                item.SelectionChanged -= OnSourceDisplayParamSelectionChanged;
            SourceDisplayParamOptions.Clear();

            // Union: current matched params, plus any previously-selected
            // names not currently matched — never silently drop a selection.
            var allNames = new SortedSet<string>(SourceMatchedParameterNames, StringComparer.OrdinalIgnoreCase);
            foreach (var extra in keepSelected)
                allNames.Add(extra);

            foreach (var name in allNames)
            {
                var item = new CheckableParam(name, keepSelected.Contains(name));
                item.SelectionChanged += OnSourceDisplayParamSelectionChanged;
                SourceDisplayParamOptions.Add(item);
            }

            UpdateSourceDisplayParamsSummary();
        }

        private void RebuildDestDisplayParamOptions()
        {
            var keepSelected = DestDisplayParamOptions
                .Where(p => p.IsSelected)
                .Select(p => p.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var pending in _pendingDestDisplayParamNames)
                keepSelected.Add(pending);
            _pendingDestDisplayParamNames.Clear();

            foreach (var item in DestDisplayParamOptions)
                item.SelectionChanged -= OnDestDisplayParamSelectionChanged;
            DestDisplayParamOptions.Clear();

            var allNames = new SortedSet<string>(DestMatchedParameterNames, StringComparer.OrdinalIgnoreCase);
            foreach (var extra in keepSelected)
                allNames.Add(extra);

            foreach (var name in allNames)
            {
                var item = new CheckableParam(name, keepSelected.Contains(name));
                item.SelectionChanged += OnDestDisplayParamSelectionChanged;
                DestDisplayParamOptions.Add(item);
            }

            UpdateDestDisplayParamsSummary();
        }

        private void OnSourceDisplayParamSelectionChanged() => UpdateSourceDisplayParamsSummary();
        private void OnDestDisplayParamSelectionChanged() => UpdateDestDisplayParamsSummary();

        private void UpdateSourceDisplayParamsSummary()
        {
            var selected = SourceDisplayParamOptions.Where(p => p.IsSelected).ToList();
            SourceDisplayParamsSummary = selected.Count switch
            {
                0 => "None selected",
                <= 2 => string.Join(", ", selected.Select(p => p.Name)),
                _ => $"{selected.Count} selected"
            };
        }

        private void UpdateDestDisplayParamsSummary()
        {
            var selected = DestDisplayParamOptions.Where(p => p.IsSelected).ToList();
            DestDisplayParamsSummary = selected.Count switch
            {
                0 => "None selected",
                <= 2 => string.Join(", ", selected.Select(p => p.Name)),
                _ => $"{selected.Count} selected"
            };
        }

        // ── Pairing candidates ────────────────────────────────────────────────

        private void RecomputePairingCandidates()
        {
            bool srcHas = SourceMatchedParameterNames.Count > 0;
            bool dstHas = DestMatchedParameterNames.Count > 0;

            IEnumerable<string> result;
            if (srcHas && dstHas)
                result = SourceMatchedParameterNames.Intersect(
                    DestMatchedParameterNames, StringComparer.OrdinalIgnoreCase);
            else if (srcHas)
                result = SourceMatchedParameterNames;
            else if (dstHas)
                result = DestMatchedParameterNames;
            else
                result = Enumerable.Empty<string>();

            var sorted = result
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            PairingParameterCandidates.Clear();
            foreach (var name in sorted)
                PairingParameterCandidates.Add(name);

            // Don't silently drop an already-set pairing parameter just
            // because the other side hasn't been (re)loaded yet.
            if (!string.IsNullOrEmpty(PairingParameterName) &&
                !PairingParameterCandidates.Contains(PairingParameterName, StringComparer.OrdinalIgnoreCase))
            {
                PairingParameterCandidates.Add(PairingParameterName);
            }
        }

        // ── Pairing ───────────────────────────────────────────────────────────

        private void PairSelected()
        {
            if (SelectedSourceItem == null || SelectedDestItem == null) return;

            Pairs.Add(new ElementPair
            {
                SourceId = SelectedSourceItem.ElementId,
                DestId = SelectedDestItem.ElementId,
                SourceLabel = $"{SelectedSourceItem.Name} ({SelectedSourceItem.ElementId.Value})",
                DestLabel = $"{SelectedDestItem.Name} ({SelectedDestItem.ElementId.Value})"
            });

            StatusText = $"{Pairs.Count} pairs defined.";
        }

        private void AutoPair()
        {
            if (string.IsNullOrWhiteSpace(PairingParameterName)) return;

            // Build lookup: pairing param value -> dest element
            var destLookup = new Dictionary<string, ElementListItem>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var d in DestElements)
            {
                string val = d.GetParameterValue(PairingParameterName);
                if (!string.IsNullOrEmpty(val))
                    destLookup[val] = d;
            }

            int added = 0;
            foreach (var src in SourceElements)
            {
                string srcVal = src.GetParameterValue(PairingParameterName);
                if (string.IsNullOrEmpty(srcVal)) continue;

                if (destLookup.TryGetValue(srcVal, out var dst))
                {
                    bool exists = Pairs.Any(p =>
                        p.SourceId == src.ElementId && p.DestId == dst.ElementId);

                    if (!exists)
                    {
                        Pairs.Add(new ElementPair
                        {
                            SourceId = src.ElementId,
                            DestId = dst.ElementId,
                            SourceLabel = $"{src.Name} [{srcVal}]",
                            DestLabel = $"{dst.Name} [{dst.GetParameterValue(PairingParameterName)}]"
                        });
                        added++;
                    }
                }
            }

            StatusText = $"Auto-paired {added} elements by '{PairingParameterName}'.";
        }

        // ── Run ───────────────────────────────────────────────────────────────

        private void RunCopy()
        {
            if (Pairs.Count == 0 || Mappings.Count == 0)
            {
                StatusText = "Define at least one pair and one mapping before running.";
                return;
            }

            StatusText = "Running copy...";
            _invoker.RunCopy(
                Pairs.ToList(),
                Mappings.ToList(),
                msg => _dispatcher.Invoke(() => StatusText = msg));
        }

        // ── Settings ──────────────────────────────────────────────────────────

        private void SaveSettings()
        {
            try
            {
                var s = new ParamCopySettings
                {
                    Source = BuildSourceSettings(),
                    Dest = BuildDestSettings(),
                    Mappings = Mappings.ToList(),
                    PairingMode = PairingMode,
                    PairingParameterName = PairingParameterName
                };

                var dir = Path.GetDirectoryName(SettingsPath)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.WriteAllText(SettingsPath,
                    JsonSerializer.Serialize(s,
                        new JsonSerializerOptions { WriteIndented = true }));

                StatusText = "Settings saved.";
            }
            catch (Exception ex)
            {
                StatusText = "Save failed: " + ex.Message;
            }
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return;

                var s = JsonSerializer.Deserialize<ParamCopySettings>(
                    File.ReadAllText(SettingsPath));
                if (s == null) return;

                // Stash desired display params before assigning CategoryName —
                // the checkbox lists don't exist yet (no Reload has run), so
                // these are applied once RebuildSourceDisplayParamOptions runs.
                _pendingSourceDisplayParamNames = s.Source.DisplayParameterNames.ToList();
                _pendingDestDisplayParamNames = s.Dest.DisplayParameterNames.ToList();

                SourceCategoryName = s.Source.CategoryName;
                SourceFilterSets.Clear();
                foreach (var fs in s.Source.FilterSets) SourceFilterSets.Add(fs);

                DestCategoryName = s.Dest.CategoryName;
                DestFilterSets.Clear();
                foreach (var fs in s.Dest.FilterSets) DestFilterSets.Add(fs);

                // Reflect pending selections immediately, even before Reload —
                // matched-parameter lists are empty at this point, so the union
                // logic in RebuildSourceDisplayParamOptions falls back to
                // showing just the pending names as checked.
                RebuildSourceDisplayParamOptions();
                RebuildDestDisplayParamOptions();

                Mappings.Clear();
                foreach (var m in s.Mappings) Mappings.Add(m);

                PairingMode = s.PairingMode;
                PairingParameterName = s.PairingParameterName;
                RecomputePairingCandidates();

                StatusText = "Settings loaded.";
            }
            catch
            {
                // Swallow — corrupt settings must not crash the tool
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private ListSettings BuildSourceSettings() => new()
        {
            CategoryName = SourceCategoryName,
            FilterSets = SourceFilterSets.ToList(),
            DisplayParameterNames = SourceDisplayParamOptions
                .Where(p => p.IsSelected)
                .Select(p => p.Name)
                .ToList()
        };

        private ListSettings BuildDestSettings() => new()
        {
            CategoryName = DestCategoryName,
            FilterSets = DestFilterSets.ToList(),
            DisplayParameterNames = DestDisplayParamOptions
                .Where(p => p.IsSelected)
                .Select(p => p.Name)
                .ToList()
        };
    }
}
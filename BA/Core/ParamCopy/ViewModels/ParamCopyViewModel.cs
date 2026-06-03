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

        // ── Source ────────────────────────────────────────────────────────────

        private string _sourceCategoryName = string.Empty;
        public string SourceCategoryName
        {
            get => _sourceCategoryName;
            set => SetProperty(ref _sourceCategoryName, value);
        }

        // Comma-separated parameter names entered by the user
        private string _sourceDisplayParams = string.Empty;
        public string SourceDisplayParams
        {
            get => _sourceDisplayParams;
            set => SetProperty(ref _sourceDisplayParams, value);
        }

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
            set => SetProperty(ref _destCategoryName, value);
        }

        private string _destDisplayParams = string.Empty;
        public string DestDisplayParams
        {
            get => _destDisplayParams;
            set => SetProperty(ref _destDisplayParams, value);
        }

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
            _invoker    = new ParamCopyExternalInvoker(uiApp);
            _dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;

            ReloadSourceCommand        = new RelayCommand(ReloadSource);
            ReloadDestCommand          = new RelayCommand(ReloadDest);
            AddSourceFilterSetCommand  = new RelayCommand(() => SourceFilterSets.Add(new FilterSet()));
            AddDestFilterSetCommand    = new RelayCommand(() => DestFilterSets.Add(new FilterSet()));
            PairSelectedCommand        = new RelayCommand(PairSelected,
                () => SelectedSourceItem != null && SelectedDestItem != null);
            AutoPairCommand            = new RelayCommand(AutoPair,
                () => !string.IsNullOrWhiteSpace(PairingParameterName));
            RemovePairCommand          = new RelayCommand(
                () => { if (SelectedPair != null) Pairs.Remove(SelectedPair); },
                () => SelectedPair != null);
            ClearPairsCommand          = new RelayCommand(() => Pairs.Clear());
            AddMappingCommand          = new RelayCommand(() => Mappings.Add(new ParamMapping()));
            RemoveMappingCommand       = new RelayCommand(
                () => { if (SelectedMapping != null) Mappings.Remove(SelectedMapping); },
                () => SelectedMapping != null);
            RunCopyCommand             = new RelayCommand(RunCopy,
                () => Pairs.Count > 0 && Mappings.Count > 0);
            SaveSettingsCommand        = new RelayCommand(SaveSettings);
            LoadSettingsCommand        = new RelayCommand(LoadSettings);

            LoadSettings();
        }

        // ── Reload ────────────────────────────────────────────────────────────

        private void ReloadSource()
        {
            IsLoading  = true;
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
        }

        private void ReloadDest()
        {
            IsLoading  = true;
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
                    Source               = BuildSourceSettings(),
                    Dest                 = BuildDestSettings(),
                    Mappings             = Mappings.ToList(),
                    PairingMode          = PairingMode,
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

                SourceCategoryName = s.Source.CategoryName;
                SourceDisplayParams = string.Join(", ", s.Source.DisplayParameterNames);
                SourceFilterSets.Clear();
                foreach (var fs in s.Source.FilterSets) SourceFilterSets.Add(fs);
                DestCategoryName = s.Dest.CategoryName;
                DestDisplayParams = string.Join(", ", s.Dest.DisplayParameterNames);
                DestFilterSets.Clear();
                foreach (var fs in s.Dest.FilterSets) DestFilterSets.Add(fs);

                Mappings.Clear();
                foreach (var m in s.Mappings) Mappings.Add(m);

                PairingMode          = s.PairingMode;
                PairingParameterName = s.PairingParameterName;

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
            DisplayParameterNames = ParseDisplayParams(SourceDisplayParams)
        };

        private ListSettings BuildDestSettings() => new()
        {
            CategoryName = DestCategoryName,
            FilterSets = DestFilterSets.ToList(),
            DisplayParameterNames = ParseDisplayParams(DestDisplayParams)
        };

        /// <summary>
        /// Splits a comma-separated parameter name string into a clean list.
        /// </summary>
        private static List<string> ParseDisplayParams(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return new List<string>();

            return raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
        }
    }
}

using System;
using System.Collections.ObjectModel;
using System.Linq;
using BA.Core.Export.Infrastructure;
using BA.Core.Export.Models;
using BA.UI.Mvvm;

namespace BA.ViewModels.Export
{
    public class ExportSettingsViewModel : BA.UI.Mvvm.ObservableObject
    {
        private ExportJobEditorViewModel _selectedJob;
        private string _statusMessage = string.Empty;
        private bool _isBusy;

        public ObservableCollection<ExportJobEditorViewModel> Jobs { get; } = new ObservableCollection<ExportJobEditorViewModel>();

        public ExportJobEditorViewModel SelectedJob
        {
            get => _selectedJob;
            set => SetProperty(ref _selectedJob, value);
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

        public BA.Core.Mvvm.RelayCommand AddPdfJobCommand { get; }
        public BA.Core.Mvvm.RelayCommand AddDwgJobCommand { get; }
        public BA.Core.Mvvm.RelayCommand RemoveSelectedJobCommand { get; }
        public BA.Core.Mvvm.RelayCommand SaveAllCommand { get; }

        public Action RequestClose { get; set; }

        public ExportSettingsViewModel()
        {
            AddPdfJobCommand = new BA.Core.Mvvm.RelayCommand(_ => AddJob(ExportFormat.Pdf));
            AddDwgJobCommand = new BA.Core.Mvvm.RelayCommand(_ => AddJob(ExportFormat.Dwg));
            RemoveSelectedJobCommand = new BA.Core.Mvvm.RelayCommand(_ => RemoveSelectedJob(), _ => SelectedJob != null);
            SaveAllCommand = new BA.Core.Mvvm.RelayCommand(_ => SaveAll());

            LoadAll();
        }

        private void LoadAll()
        {
            IsBusy = true;
            StatusMessage = "Loading export settings...";

            var request = new ExportUiRequest { Action = ExportUiAction.LoadSettings };

            ExportUiBridge.Submit(request, response =>
            {
                if (!response.Success || response.LoadedSettings == null)
                {
                    IsBusy = false;
                    StatusMessage = $"Failed to load settings: {response.ErrorMessage}";
                    return;
                }

                Jobs.Clear();

                foreach (var jobModel in response.LoadedSettings.Jobs)
                {
                    Jobs.Add(new ExportJobEditorViewModel(jobModel));
                }

                LoadPickerLists();
            });
        }

        private void LoadPickerLists()
        {
            var sheetSetsRequest = new ExportUiRequest { Action = ExportUiAction.GetSheetSetNames };

            ExportUiBridge.Submit(sheetSetsRequest, sheetSetsResponse =>
            {
                if (sheetSetsResponse.Success)
                {
                    foreach (var job in Jobs)
                    {
                        job.LoadAvailableSheetSets(sheetSetsResponse.StringList);
                    }
                }

                var dwgSetupsRequest = new ExportUiRequest { Action = ExportUiAction.GetDwgExportSetupNames };

                ExportUiBridge.Submit(dwgSetupsRequest, dwgSetupsResponse =>
                {
                    IsBusy = false;
                    StatusMessage = "Ready.";

                    if (dwgSetupsResponse.Success)
                    {
                        foreach (var job in Jobs)
                        {
                            job.LoadAvailableDwgExportSetups(dwgSetupsResponse.StringList);
                        }
                    }
                });
            });
        }

        private void AddJob(ExportFormat format)
        {
            var newModel = new ExportJobSettings
            {
                JobName = format == ExportFormat.Pdf ? "New PDF Job" : "New DWG Job",
                Format = format
            };

            var editor = new ExportJobEditorViewModel(newModel);
            Jobs.Add(editor);
            SelectedJob = editor;
        }

        private void RemoveSelectedJob()
        {
            if (SelectedJob == null)
            {
                return;
            }

            Jobs.Remove(SelectedJob);
            SelectedJob = null;
        }

        private void SaveAll()
        {
            IsBusy = true;
            StatusMessage = "Saving...";

            var settingsRoot = new ExportSettingsRoot
            {
                Jobs = Jobs.Select(j => j.ToModel()).ToList()
            };

            var request = new ExportUiRequest
            {
                Action = ExportUiAction.SaveSettings,
                SettingsToSave = settingsRoot
            };

            ExportUiBridge.Submit(request, response =>
            {
                IsBusy = false;
                StatusMessage = response.Success ? "Saved." : $"Save failed: {response.ErrorMessage}";
            });
        }

    }
}

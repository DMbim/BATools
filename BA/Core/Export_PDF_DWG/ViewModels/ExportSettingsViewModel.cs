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
            set
            {
                if (SetProperty(ref _selectedJob, value))
                {
                    OnPropertyChanged(nameof(HasSelectedJob));
                    OnPropertyChanged(nameof(NoJobSelected));
                }
            }
        }

        /// <summary>
        /// Drives the empty state placeholder in ExportSettingsWindow. With
        /// no job selected, the editor panel's DataContext is null and
        /// every bound control shows stale or blank values that look
        /// broken but are not, this makes the state explicit instead.
        /// </summary>
        public bool HasSelectedJob => SelectedJob != null;

        /// <summary>
        /// Plain inverse of HasSelectedJob, kept as its own property rather
        /// than introducing a generic inverse-boolean-to-visibility
        /// converter for this one binding.
        /// </summary>
        public bool NoJobSelected => SelectedJob == null;

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

        public BA.UI.Mvvm.RelayCommand AddJobCommand { get; }
        public BA.UI.Mvvm.RelayCommand RemoveSelectedJobCommand { get; }
        public BA.UI.Mvvm.RelayCommand SaveAllCommand { get; }
        public BA.UI.Mvvm.RelayCommand CloseCommand { get; }

        public Action RequestClose { get; set; }

        public ExportSettingsViewModel()
        {
            AddJobCommand = new BA.UI.Mvvm.RelayCommand(_ => AddJob());
            RemoveSelectedJobCommand = new BA.UI.Mvvm.RelayCommand(_ => RemoveSelectedJob(), _ => SelectedJob != null);
            SaveAllCommand = new BA.UI.Mvvm.RelayCommand(_ => SaveAll());
            CloseCommand = new BA.UI.Mvvm.RelayCommand(_ => RequestClose?.Invoke());

            LoadAll();
        }

        private void LoadAll()
        {
            IsBusy = true;
            StatusMessage = "Loading export settings...";

            var request = new ExportUiRequest { Action = ExportUiAction.LoadSettings };

            ExportUiBridge.Submit(request, response =>
            {
                IsBusy = false;

                if (!response.Success || response.LoadedSettings == null)
                {
                    StatusMessage = $"Failed to load settings: {response.ErrorMessage}";
                    return;
                }

                StatusMessage = "Ready.";

                Jobs.Clear();

                foreach (var jobModel in response.LoadedSettings.Jobs)
                {
                    Jobs.Add(new ExportJobEditorViewModel(jobModel));
                }
            });
        }

        private void AddJob()
        {
            // A new job defaults to PDF enabled, DWG off, the user ticks
            // additional formats in the editor. No predefined setup lookup
            // needed anymore, DwgSettings/PdfSettings on the new
            // ExportJobSettings already carry sensible defaults.
            var newModel = new ExportJobSettings
            {
                JobName = "New Export Job"
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
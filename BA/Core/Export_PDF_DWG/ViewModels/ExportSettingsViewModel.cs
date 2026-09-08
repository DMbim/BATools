using System;
using System.Collections.ObjectModel;
using System.Linq;
using BA.Core.Export.Infrastructure;
using BA.Core.Export.Models;
using BA.Settings;
using BA.UI.Mvvm;

namespace BA.ViewModels.Export
{
    public class ExportSettingsViewModel : BA.UI.Mvvm.ObservableObject
    {
        private ExportJobEditorViewModel _selectedJob;
        private string _statusMessage = string.Empty;
        private bool _isBusy;

        /// <summary>
        /// Backs GlobalDateParam/GlobalRevParam/GlobalDateFormat. Loaded
        /// once at construction, saved back in SaveAll(). This is the
        /// same DateToolSettings instance and file
        /// (%AppData%\BA\Date\DateSettings.json) Cmd_SheetDateAndRevision_Settings
        /// used to write before being retired, round tripped whole so the
        /// unrelated fields it also carries (parameter copy, room link)
        /// survive untouched.
        /// </summary>
        private readonly DateToolSettings _dateToolSettings;

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

        /// <summary>
        /// One global date parameter name shared by every export job's
        /// date/revision bump, not duplicated per job. Also the parameter
        /// NamingTemplateEngine reads for the {Revision} naming token,
        /// changing it here changes both.
        /// </summary>
        public string GlobalDateParam
        {
            get => _dateToolSettings.SelectedDateParam;
            set
            {
                if (_dateToolSettings.SelectedDateParam != value)
                {
                    _dateToolSettings.SelectedDateParam = value;
                    OnPropertyChanged(nameof(GlobalDateParam));
                }
            }
        }

        public string GlobalRevParam
        {
            get => _dateToolSettings.SelectedRevParam;
            set
            {
                if (_dateToolSettings.SelectedRevParam != value)
                {
                    _dateToolSettings.SelectedRevParam = value;
                    OnPropertyChanged(nameof(GlobalRevParam));
                }
            }
        }

        public string GlobalDateFormat
        {
            get => _dateToolSettings.SelectedFormat;
            set
            {
                if (_dateToolSettings.SelectedFormat != value)
                {
                    _dateToolSettings.SelectedFormat = value;
                    OnPropertyChanged(nameof(GlobalDateFormat));
                }
            }
        }

        /// <summary>
        /// Fixed seed list, matches the retired DateSetupWindow's
        /// FormatComboBox values exactly. Bind the combo box to this as
        /// IsEditable="true", the same way DateSetupWindow's was, so a
        /// firm-specific format not in this list is still a valid entry.
        /// </summary>
        public string[] AvailableDateFormats { get; } =
        {
            "yy/MM/dd",
            "dd/MM/yy",
            "MM/dd/yy",
            "yyyy-MM-dd",
            "dd.MM.yyyy",
        };

        /// <summary>
        /// Populates the dropdown of sheet string parameters available to
        /// pick as GlobalDateParam/GlobalRevParam (matching
        /// DateSetupWindow.CollectSheetStringParams' original behavior).
        /// Not wired yet, this needs a new ExportUiAction and I have not
        /// reviewed ExportUiAction.cs/ExportUiRequest.cs/ExportUiResponse.cs.
        /// Bind GlobalDateParam/GlobalRevParam to an editable combo box
        /// against this collection now regardless, typing a value directly
        /// still works, it will just start empty until that action exists.
        /// </summary>
        public ObservableCollection<string> AvailableDateRevisionParams { get; } = new ObservableCollection<string>();

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

            _dateToolSettings = DateToolSettings.LoadWithMigration();

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
            // BumpDateRevisionOnRun defaults to false, an existing or new
            // job never bumps revision unless someone explicitly turns
            // that on.
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

            // Plain file I/O, no Document dependency, no need to route
            // through ExportUiBridge/the Revit API thread for this part.y
            _dateToolSettings.Save();

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
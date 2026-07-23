// BA_Tools/ScheduleExporter/ViewModels/ExportScheduleViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using BA.UI.Mvvm;
using Microsoft.Win32;

namespace BA_Tools.ScheduleExporter.ViewModels
{
    public class ExportScheduleViewModel : BA.UI.Mvvm.ObservableObject
    {
        private readonly List<ViewSchedule> _allSchedules;

        private bool _useActiveSchedule;
        private ViewSchedule _selectedSchedule;
        private string _outputFilePath;
        private string _statusMessage;

        // ─── Read-only properties ──────────────────────────────────────────────

        public ObservableCollection<ViewSchedule> AvailableSchedules { get; }
        public ViewSchedule ActiveSchedule { get; }
        public bool HasActiveSchedule => ActiveSchedule != null;
        public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

        public string ActiveScheduleLabel => ActiveSchedule != null
            ? $"Use active schedule: {ActiveSchedule.Name}"
            : "No active schedule (open a schedule view first)";

        // ─── Observable properties ─────────────────────────────────────────────

        public bool UseActiveSchedule
        {
            get => _useActiveSchedule;
            set
            {
                if (!SetProperty(ref _useActiveSchedule, value))
                {
                    return;
                }

                OnPropertyChanged(nameof(ActiveScheduleLabel));

                if (value && ActiveSchedule != null)
                {
                    SelectedSchedule = ActiveSchedule;
                }
                else if (!value && _allSchedules.Count > 0 && SelectedSchedule == ActiveSchedule)
                {
                    SelectedSchedule = _allSchedules.FirstOrDefault(s => s != ActiveSchedule)
                                       ?? _allSchedules[0];
                }

                ExportCommand.RaiseCanExecuteChanged();
            }
        }

        public ViewSchedule SelectedSchedule
        {
            get => _selectedSchedule;
            set
            {
                if (SetProperty(ref _selectedSchedule, value))
                {
                    ExportCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string OutputFilePath
        {
            get => _outputFilePath;
            set
            {
                if (SetProperty(ref _outputFilePath, value))
                {
                    ExportCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                if (SetProperty(ref _statusMessage, value))
                {
                    OnPropertyChanged(nameof(HasStatusMessage));
                }
            }
        }

        private bool CanExport() =>
            !string.IsNullOrWhiteSpace(OutputFilePath) &&
            GetEffectiveSchedule() != null;

        public bool UserConfirmed { get; private set; }
        public event Action CloseRequested;

        // ─── Commands ──────────────────────────────────────────────────────────

        public BA.Core.Mvvm.RelayCommand BrowseOutputFileCommand { get; }
        public BA.Core.Mvvm.RelayCommand ExportCommand { get; }
        public BA.Core.Mvvm.RelayCommand CancelCommand { get; }

        // ─── Constructor ───────────────────────────────────────────────────────

        public ExportScheduleViewModel(List<ViewSchedule> allSchedules, ViewSchedule activeSchedule)
        {
            _allSchedules = allSchedules ?? new List<ViewSchedule>();
            ActiveSchedule = activeSchedule;
            AvailableSchedules = new ObservableCollection<ViewSchedule>(_allSchedules);

            // Direct field assignment here, not the property setters, matches the
            // original: construction shouldn't fire the side effects (schedule
            // reassignment, command re-evaluation) that a real user toggling the
            // option later should trigger.
            _useActiveSchedule = activeSchedule != null;

            if (!_useActiveSchedule && _allSchedules.Count > 0)
            {
                _selectedSchedule = _allSchedules[0];
            }
            else if (_useActiveSchedule)
            {
                _selectedSchedule = activeSchedule;
            }

            BrowseOutputFileCommand = new BA.Core.Mvvm.RelayCommand(_ => BrowseOutputFile());
            ExportCommand = new BA.Core.Mvvm.RelayCommand(_ => Export(), _ => CanExport());
            CancelCommand = new BA.Core.Mvvm.RelayCommand(_ => Cancel());
        }

        // ─── Command actions ───────────────────────────────────────────────────

        private void BrowseOutputFile()
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save Schedule Export As",
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx",
                FileName = BuildDefaultFileName()
            };

            if (dialog.ShowDialog() == true)
            {
                OutputFilePath = dialog.FileName;
            }
        }

        private void Export()
        {
            string dir = Path.GetDirectoryName(OutputFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                StatusMessage = $"Output directory does not exist: {dir}";
                return;
            }

            UserConfirmed = true;
            CloseRequested?.Invoke();
        }

        private void Cancel()
        {
            UserConfirmed = false;
            CloseRequested?.Invoke();
        }

        // ─── Public helpers ────────────────────────────────────────────────────

        public ViewSchedule GetEffectiveSchedule()
        {
            if (UseActiveSchedule) return ActiveSchedule;
            return SelectedSchedule;
        }

        // ─── Private ───────────────────────────────────────────────────────────

        private string BuildDefaultFileName()
        {
            ViewSchedule schedule = GetEffectiveSchedule();
            if (schedule == null) return "ScheduleExport";

            string name = schedule.Name;
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
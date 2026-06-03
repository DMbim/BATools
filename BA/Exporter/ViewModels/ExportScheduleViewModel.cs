using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace BA_Tools.ScheduleExporter.ViewModels
{
    public partial class ExportScheduleViewModel : ObservableObject
    {
        private readonly List<ViewSchedule> _allSchedules;

        // ─── Observable properties ─────────────────────────────────────────────

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ActiveScheduleLabel))]
        [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
        private bool _useActiveSchedule;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
        private ViewSchedule _selectedSchedule;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
        private string _outputFilePath;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
        private string _statusMessage;

        // ─── Read-only properties ──────────────────────────────────────────────

        public ObservableCollection<ViewSchedule> AvailableSchedules { get; }
        public ViewSchedule ActiveSchedule { get; }
        public bool HasActiveSchedule => ActiveSchedule != null;
        public bool HasStatusMessage  => !string.IsNullOrEmpty(StatusMessage);

        public string ActiveScheduleLabel => ActiveSchedule != null
            ? $"Use active schedule: {ActiveSchedule.Name}"
            : "No active schedule (open a schedule view first)";

        // Must be a method (not property) for RelayCommand CanExecute source generator
        private bool CanExport() =>
            !string.IsNullOrWhiteSpace(OutputFilePath) &&
            GetEffectiveSchedule() != null;

        public bool UserConfirmed { get; private set; }
        public event Action CloseRequested;

        // ─── Constructor ───────────────────────────────────────────────────────

        public ExportScheduleViewModel(List<ViewSchedule> allSchedules, ViewSchedule activeSchedule)
        {
            _allSchedules      = allSchedules ?? new List<ViewSchedule>();
            ActiveSchedule     = activeSchedule;
            AvailableSchedules = new ObservableCollection<ViewSchedule>(_allSchedules);

            _useActiveSchedule = activeSchedule != null;

            if (!_useActiveSchedule && _allSchedules.Count > 0)
                _selectedSchedule = _allSchedules[0];
            else if (_useActiveSchedule)
                _selectedSchedule = activeSchedule;
        }

        // ─── Commands ──────────────────────────────────────────────────────────

        [RelayCommand]
        private void BrowseOutputFile()
        {
            var dialog = new SaveFileDialog
            {
                Title      = "Save Schedule Export As",
                Filter     = "Excel Workbook (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx",
                FileName   = BuildDefaultFileName()
            };

            if (dialog.ShowDialog() == true)
                OutputFilePath = dialog.FileName;
        }

        [RelayCommand(CanExecute = nameof(CanExport))]
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

        [RelayCommand]
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

        partial void OnUseActiveScheduleChanged(bool value)
        {
            if (value && ActiveSchedule != null)
                SelectedSchedule = ActiveSchedule;
            else if (!value && _allSchedules.Count > 0 && SelectedSchedule == ActiveSchedule)
                SelectedSchedule = _allSchedules.FirstOrDefault(s => s != ActiveSchedule)
                                   ?? _allSchedules[0];

            OnPropertyChanged(nameof(CanExport));
        }

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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BA_Tools.ScheduleExporter.ViewModels
{
    /// <summary>
    /// Used when the schedule name read from the Excel file does not match any
    /// ViewSchedule in the current document. Presents the full schedule list so
    /// the user can manually pick a target schedule for the import.
    /// </summary>
    public partial class SchedulePickerViewModel : ObservableObject
    {
        /// <summary>
        /// The schedule name that was read from the Excel sheet name.
        /// Displayed in the dialog so the user knows what was expected.
        /// </summary>
        public string MissingScheduleName { get; }

        public ObservableCollection<ViewSchedule> AvailableSchedules { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanConfirm))]
        private ViewSchedule _selectedSchedule;

        public bool CanConfirm => SelectedSchedule != null;

        public bool UserConfirmed { get; private set; }
        public event Action CloseRequested;

        public SchedulePickerViewModel(string missingScheduleName, List<ViewSchedule> availableSchedules)
        {
            MissingScheduleName  = missingScheduleName ?? string.Empty;
            AvailableSchedules   = new ObservableCollection<ViewSchedule>(
                availableSchedules ?? new List<ViewSchedule>());

            if (AvailableSchedules.Count > 0)
                SelectedSchedule = AvailableSchedules[0];
        }

        [RelayCommand(CanExecute = nameof(CanConfirm))]
        private void Confirm()
        {
            UserConfirmed = true;
            CloseRequested?.Invoke();
        }

        [RelayCommand]
        private void Cancel()
        {
            UserConfirmed = false;
            CloseRequested?.Invoke();
        }
    }
}

// BA_Tools/ScheduleExporter/ViewModels/SchedulePickerViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Autodesk.Revit.DB;
using BA.UI.Mvvm;

namespace BA_Tools.ScheduleExporter.ViewModels
{
    /// <summary>
    /// Used when the schedule name read from the Excel file does not match any
    /// ViewSchedule in the current document. Presents the full schedule list so
    /// the user can manually pick a target schedule for the import.
    /// </summary>
    public class SchedulePickerViewModel : BA.UI.Mvvm.ObservableObject
    {
        /// <summary>
        /// The schedule name that was read from the Excel sheet name.
        /// Displayed in the dialog so the user knows what was expected.
        /// </summary>
        public string MissingScheduleName { get; }
        public ObservableCollection<ViewSchedule> AvailableSchedules { get; }

        private ViewSchedule _selectedSchedule;
        public ViewSchedule SelectedSchedule
        {
            get => _selectedSchedule;
            set
            {
                if (SetProperty(ref _selectedSchedule, value))
                {
                    OnPropertyChanged(nameof(CanConfirm));
                    ConfirmCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool CanConfirm => SelectedSchedule != null;
        public bool UserConfirmed { get; private set; }
        public event Action CloseRequested;

        public BA.Core.Mvvm.RelayCommand ConfirmCommand { get; }
        public BA.Core.Mvvm.RelayCommand CancelCommand { get; }

        public SchedulePickerViewModel(string missingScheduleName, List<ViewSchedule> availableSchedules)
        {
            MissingScheduleName = missingScheduleName ?? string.Empty;
            AvailableSchedules = new ObservableCollection<ViewSchedule>(
                availableSchedules ?? new List<ViewSchedule>());

            // Commands constructed before the SelectedSchedule assignment below,
            // since that assignment goes through the property setter (matching
            // the original, which also assigned via the property, not the field)
            // and the setter calls ConfirmCommand.RaiseCanExecuteChanged().
            ConfirmCommand = new BA.Core.Mvvm.RelayCommand(_ => Confirm(), _ => CanConfirm);
            CancelCommand = new BA.Core.Mvvm.RelayCommand(_ => Cancel());

            if (AvailableSchedules.Count > 0)
            {
                SelectedSchedule = AvailableSchedules[0];
            }
        }

        private void Confirm()
        {
            UserConfirmed = true;
            CloseRequested?.Invoke();
        }

        private void Cancel()
        {
            UserConfirmed = false;
            CloseRequested?.Invoke();
        }
    }
}
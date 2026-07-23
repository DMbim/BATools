using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using BA.Core.Export.Infrastructure;
using BA.Core.Export.Models;
using BA.UI.Mvvm;

namespace BA.ViewModels.Export
{
    /// <summary>
    /// Wraps one ExportJobSettings for editing in the settings window. Holds
    /// its own copy of scalar values so XAML can bind directly with change
    /// notification; ExportJobSettings itself has none. ToModel() is the
    /// single place that converts back to the persisted shape.
    /// </summary>
    public class ExportJobEditorViewModel : BA.UI.Mvvm.ObservableObject
    {
        private string _jobName;
        private ExportFormat _format;
        private bool _enabled;
        private string _sheetSetName;
        private string _namingTemplate;
        private string _dateFormat;
        private string _outputFolderTemplate;
        private string _exportSetupName;
        private bool _scheduleEnabled;
        private string _scheduledTimeText;
        private int _catchUpMinutes;
        private string _previewFileName = string.Empty;
        private string _previewFolder = string.Empty;
        private string _statusMessage = string.Empty;
        private bool _isBusy;

        public Guid JobId { get; }

        public ObservableCollection<DayToggleViewModel> Days { get; }
        public ObservableCollection<string> AvailableSheetSets { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> AvailableDwgExportSetups { get; } = new ObservableCollection<string>();

        public string JobName
        {
            get => _jobName;
            set => SetProperty(ref _jobName, value);
        }

        public ExportFormat Format
        {
            get => _format;
            set => SetProperty(ref _format, value);
        }

        public bool IsPdf => Format == ExportFormat.Pdf;
        public bool IsDwg => Format == ExportFormat.Dwg;

        public bool Enabled
        {
            get => _enabled;
            set => SetProperty(ref _enabled, value);
        }

        public string SheetSetName
        {
            get => _sheetSetName;
            set => SetProperty(ref _sheetSetName, value);
        }

        public string NamingTemplate
        {
            get => _namingTemplate;
            set => SetProperty(ref _namingTemplate, value);
        }

        public string DateFormat
        {
            get => _dateFormat;
            set => SetProperty(ref _dateFormat, value);
        }

        public string OutputFolderTemplate
        {
            get => _outputFolderTemplate;
            set => SetProperty(ref _outputFolderTemplate, value);
        }

        public string ExportSetupName
        {
            get => _exportSetupName;
            set => SetProperty(ref _exportSetupName, value);
        }

        public bool ScheduleEnabled
        {
            get => _scheduleEnabled;
            set => SetProperty(ref _scheduleEnabled, value);
        }

        /// <summary>
        /// "HH:mm" text bound directly to a textbox, parsed on ToModel().
        /// Invalid text falls back to 18:00 rather than throwing.
        /// </summary>
        public string ScheduledTimeText
        {
            get => _scheduledTimeText;
            set => SetProperty(ref _scheduledTimeText, value);
        }

        public int CatchUpMinutes
        {
            get => _catchUpMinutes;
            set => SetProperty(ref _catchUpMinutes, value);
        }

        public string PreviewFileName
        {
            get => _previewFileName;
            private set => SetProperty(ref _previewFileName, value);
        }

        public string PreviewFolder
        {
            get => _previewFolder;
            private set => SetProperty(ref _previewFolder, value);
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

        public BA.Core.Mvvm.RelayCommand PreviewCommand { get; }
        public BA.Core.Mvvm.RelayCommand RunNowCommand { get; }

        public ExportJobEditorViewModel(ExportJobSettings model)
        {
            JobId = model.JobId;
            _jobName = model.JobName;
            _format = model.Format;
            _enabled = model.Enabled;
            _sheetSetName = model.SheetSetName;
            _namingTemplate = model.NamingTemplate;
            _dateFormat = model.DateFormat;
            _outputFolderTemplate = model.OutputFolderTemplate;
            _exportSetupName = model.ExportSetupName;
            _scheduleEnabled = model.ScheduleEnabled;
            _scheduledTimeText = model.ScheduledTimeOfDay.ToString(@"hh\:mm");
            _catchUpMinutes = (int)model.CatchUpWindow.TotalMinutes;

            Days = new ObservableCollection<DayToggleViewModel>(
                Enum.GetValues(typeof(DayOfWeek))
                    .Cast<DayOfWeek>()
                    .Select(d => new DayToggleViewModel(d) { IsSelected = model.ScheduledDays.Contains(d) }));

            PreviewCommand = new BA.Core.Mvvm.RelayCommand(_ => RunPreview());
            RunNowCommand = new BA.Core.Mvvm.RelayCommand(_ => RunNow());
        }

        public ExportJobSettings ToModel()
        {
            var scheduledTime = TimeSpan.TryParseExact(ScheduledTimeText, @"hh\:mm", CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : new TimeSpan(18, 0, 0);

            return new ExportJobSettings
            {
                JobId = JobId,
                JobName = JobName ?? string.Empty,
                Format = Format,
                Enabled = Enabled,
                SheetSetName = SheetSetName ?? string.Empty,
                NamingTemplate = NamingTemplate ?? string.Empty,
                DateFormat = DateFormat ?? "yyyyMMdd",
                OutputFolderTemplate = OutputFolderTemplate ?? string.Empty,
                ExportSetupName = ExportSetupName ?? string.Empty,
                ScheduleEnabled = ScheduleEnabled,
                ScheduledTimeOfDay = scheduledTime,
                CatchUpWindow = TimeSpan.FromMinutes(Math.Max(0, CatchUpMinutes)),
                ScheduledDays = Days.Where(d => d.IsSelected).Select(d => d.Day).ToList()
            };
        }

        public void LoadAvailableSheetSets(IEnumerable<string> names)
        {
            AvailableSheetSets.Clear();
            foreach (var n in names)
            {
                AvailableSheetSets.Add(n);
            }
        }

        public void LoadAvailableDwgExportSetups(IEnumerable<string> names)
        {
            AvailableDwgExportSetups.Clear();
            foreach (var n in names)
            {
                AvailableDwgExportSetups.Add(n);
            }
        }

        private void RunPreview()
        {
            IsBusy = true;
            StatusMessage = "Resolving preview...";

            var request = new ExportUiRequest
            {
                Action = ExportUiAction.PreviewNaming,
                JobForPreviewOrRun = ToModel()
            };

            ExportUiBridge.Submit(request, response =>
            {
                IsBusy = false;

                if (response.Success)
                {
                    PreviewFileName = response.PreviewFileName;
                    PreviewFolder = response.PreviewFolder;
                    StatusMessage = "Preview updated.";
                }
                else
                {
                    PreviewFileName = string.Empty;
                    PreviewFolder = string.Empty;
                    StatusMessage = $"Preview failed: {response.ErrorMessage}";
                }
            });
        }

        private void RunNow()
        {
            IsBusy = true;
            StatusMessage = "Running export...";

            var request = new ExportUiRequest
            {
                Action = ExportUiAction.RunJobNow,
                JobForPreviewOrRun = ToModel()
            };

            ExportUiBridge.Submit(request, response =>
            {
                IsBusy = false;

                StatusMessage = response.Success
                    ? $"Ran: {response.JobResult.SuccessCount} succeeded, {response.JobResult.FailureCount} failed."
                    : $"Run failed: {response.ErrorMessage}";
            });
        }
    }
}

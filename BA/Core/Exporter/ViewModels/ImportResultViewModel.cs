using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BA_Tools.ScheduleExporter.Models;

namespace BA_Tools.ScheduleExporter.ViewModels
{
    /// <summary>
    /// ViewModel for the post-import result dialog.
    /// Shows success/failure/skipped counts and a scrollable error list.
    /// </summary>
    public partial class ImportResultViewModel : ObservableObject
    {
        public int  SuccessCount { get; }
        public int  FailureCount { get; }
        public int  SkippedCount { get; }
        public bool HasErrors    => FailureCount > 0;

        public string SummaryMessage { get; }

        public ObservableCollection<WriteError> Errors { get; }

        public event Action CloseRequested;

        public ImportResultViewModel(WriteResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            SuccessCount = result.SuccessCount;
            FailureCount = result.FailureCount;
            SkippedCount = result.SkippedCount;
            Errors       = new ObservableCollection<WriteError>(result.Errors);

            SummaryMessage = FailureCount == 0
                ? $"Import complete. {SuccessCount} parameter value(s) written successfully."
                : $"Import finished with {FailureCount} error(s). {SuccessCount} value(s) written. See the list below.";
        }

        [RelayCommand]
        private void Close() => CloseRequested?.Invoke();
    }
}

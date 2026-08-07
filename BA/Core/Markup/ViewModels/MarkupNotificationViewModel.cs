// BA/Markup/ViewModels/MarkupNotificationViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.BAApplication;
using BA.Markup.Models;

namespace BA.Markup.ViewModels
{
    public sealed class MarkupNotificationViewModel : INotifyPropertyChanged
    {
        // ------------------------------------------------------------------ //
        //  INotifyPropertyChanged
        // ------------------------------------------------------------------ //
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }

        // ------------------------------------------------------------------ //
        //  Row, mutable wrapper around the immutable scan DTO
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Mutable per-row wrapper. MarkupNotificationItem itself stays immutable, this class
        /// exists only so the DataGrid has something to reflect live state changes against
        /// (Wip flips, Solved removes the row) without the scan/baseline layer needing to
        /// know anything about mutability at all.
        /// </summary>
        public sealed class Row : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler? PropertyChanged;

            private void OnPropertyChanged([CallerMemberName] string? name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

            public Row(MarkupNotificationItem source)
            {
                ElementId = source.ElementId;
                OwnerViewId = source.OwnerViewId;
                ViewName = source.ViewName;
                Author = source.Author;
                Date = source.Date;
                Comments = source.Comments;
                BaType = source.BaType;
                IsNew = source.IsNew;
                _wip = source.Wip;
                _solved = source.Solved;
            }

            public long ElementId { get; }
            public long OwnerViewId { get; }
            public string ViewName { get; }
            public string Author { get; }
            public string Date { get; }
            public string Comments { get; }
            public string BaType { get; }
            public bool IsNew { get; }

            private bool _wip;
            public bool Wip
            {
                get => _wip;
                set
                {
                    if (_wip == value) return;
                    _wip = value;
                    OnPropertyChanged();
                }
            }

            private bool _solved;
            public bool Solved
            {
                get => _solved;
                set
                {
                    if (_solved == value) return;
                    _solved = value;
                    OnPropertyChanged();
                }
            }
        }

        // ------------------------------------------------------------------ //
        //  Construction
        // ------------------------------------------------------------------ //
        private readonly Document _doc;
        private readonly UIDocument? _uiDoc;

        public MarkupNotificationViewModel(
            Document doc,
            UIDocument? uiDoc,
            IReadOnlyList<MarkupNotificationItem> items)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _uiDoc = uiDoc;

            foreach (var item in items)
                Items.Add(new Row(item));

            GoToViewCommand = new BA.Core.Mvvm.RelayCommand(
                _ => ExecuteGoToView(),
                _ => SelectedRow != null && _uiDoc != null);

            MarkWipCommand = new BA.Core.Mvvm.RelayCommand(
                _ => ExecuteMarkWip(),
                _ => SelectedRow != null && !SelectedRow.Wip);

            MarkSolvedCommand = new BA.Core.Mvvm.RelayCommand(
                _ => ExecuteMarkSolved(),
                _ => SelectedRow != null);

            CloseCommand = new BA.Core.Mvvm.RelayCommand(_ => ExecuteClose());
        }

        // ------------------------------------------------------------------ //
        //  Rows
        // ------------------------------------------------------------------ //
        public ObservableCollection<Row> Items { get; } = new();

        private Row? _selectedRow;
        public Row? SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (Set(ref _selectedRow, value))
                    RaiseCommandsCanExecuteChanged();
            }
        }

        // ------------------------------------------------------------------ //
        //  Status
        // ------------------------------------------------------------------ //
        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            private set => Set(ref _statusMessage, value ?? string.Empty);
        }

        /// <summary>
        /// True if Go To View is unavailable because the synced document was not the
        /// active one at sync time. Bound in XAML to show an explanatory note near the
        /// Go To View button rather than leaving a silently-disabled button unexplained.
        /// </summary>
        public bool GoToViewUnavailable => _uiDoc == null;

        // ------------------------------------------------------------------ //
        //  Commands
        // ------------------------------------------------------------------ //
        public ICommand GoToViewCommand { get; }
        public ICommand MarkWipCommand { get; }
        public ICommand MarkSolvedCommand { get; }
        public ICommand CloseCommand { get; }

        public event EventHandler? CloseRequested;

        private void ExecuteGoToView()
        {
            if (SelectedRow == null || _uiDoc == null) return;

            try
            {
                if (_doc.GetElement(new ElementId(SelectedRow.OwnerViewId)) is View view)
                {
                    _uiDoc.RequestViewChange(view);
                    StatusMessage = string.Empty;
                }
                else
                {
                    StatusMessage = "The view this markup was placed on no longer exists.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                AppLogger.LogError("MarkupNotificationViewModel.ExecuteGoToView", ex);
            }
        }

        private void ExecuteMarkWip()
        {
            if (SelectedRow == null) return;

            try
            {
                SetBoolParameter(SelectedRow.ElementId, "BA.Tls_WIP", true);
                SelectedRow.Wip = true;
                StatusMessage = "Marked as in progress.";
                RaiseCommandsCanExecuteChanged();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                AppLogger.LogError("MarkupNotificationViewModel.ExecuteMarkWip", ex);
            }
        }

        private void ExecuteMarkSolved()
        {
            if (SelectedRow == null) return;

            var row = SelectedRow;

            try
            {
                SetBoolParameter(row.ElementId, "BA.Tls_Solved", true);
                row.Solved = true;
                Items.Remove(row);
                SelectedRow = null;
                StatusMessage = "Marked as solved.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                AppLogger.LogError("MarkupNotificationViewModel.ExecuteMarkSolved", ex);
            }
        }

        private void ExecuteClose()
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void SetBoolParameter(long elementId, string paramName, bool value)
        {
            var element = _doc.GetElement(new ElementId(elementId))
                ?? throw new InvalidOperationException(
                    $"Element {elementId} no longer exists in the document.");

            var param = element.LookupParameter(paramName)
                ?? throw new InvalidOperationException(
                    $"Parameter '{paramName}' not found on element {elementId}.");

            if (param.IsReadOnly)
                throw new InvalidOperationException(
                    $"Parameter '{paramName}' is read-only on element {elementId}.");

            using var tx = new Transaction(_doc, $"BA — Update {paramName}");
            tx.Start();
            param.Set(value ? 1 : 0);
            tx.Commit();
        }

        private void RaiseCommandsCanExecuteChanged()
        {
            ((BA.Core.Mvvm.RelayCommand)GoToViewCommand).RaiseCanExecuteChanged();
            ((BA.Core.Mvvm.RelayCommand)MarkWipCommand).RaiseCanExecuteChanged();
            ((BA.Core.Mvvm.RelayCommand)MarkSolvedCommand).RaiseCanExecuteChanged();
        }
    }
}
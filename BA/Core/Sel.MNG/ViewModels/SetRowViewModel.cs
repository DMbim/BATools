using System;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BATools.SelectionManager.Models;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;

namespace BATools.SelectionManager.ViewModels
{
    public class SetRowViewModel : ObservableObject
    {
        private Guid _id;
        public Guid Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private int _elementCount;
        public int ElementCount
        {
            get => _elementCount;
            set => SetProperty(ref _elementCount, value);
        }

        private SetHealthStatus _healthStatus;
        public SetHealthStatus HealthStatus
        {
            get => _healthStatus;
            set
            {
                SetProperty(ref _healthStatus, value);
                OnPropertyChanged(nameof(HealthBrush));
                OnPropertyChanged(nameof(HealthTooltip));
            }
        }

        private int _staleCount;
        public int StaleCount
        {
            get => _staleCount;
            set => SetProperty(ref _staleCount, value);
        }

        private bool _isRenaming;
        public bool IsRenaming
        {
            get => _isRenaming;
            set => SetProperty(ref _isRenaming, value);
        }

        public Brush HealthBrush => HealthStatus switch
        {
            SetHealthStatus.Healthy => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
            SetHealthStatus.PartiallyStale => new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00)),
            SetHealthStatus.FullyStale => new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)),
            SetHealthStatus.Empty => new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90)),
            _ => new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60))
        };

        public string HealthTooltip => HealthStatus switch
        {
            SetHealthStatus.Healthy => "All elements valid",
            SetHealthStatus.PartiallyStale => $"{StaleCount} element(s) no longer exist in model",
            SetHealthStatus.FullyStale => "All elements have been deleted from the model",
            SetHealthStatus.Empty => "Set is empty",
            _ => "Health unknown"
        };

        // Commands set externally by parent ViewModel
        public ICommand? RecallCommand { get; set; }
        public ICommand? DeleteCommand { get; set; }
        public ICommand? BeginRenameCommand { get; set; }
        public ICommand? CommitRenameCommand { get; set; }
        public ICommand? AddToSetCommand { get; set; }

        public static SetRowViewModel FromModel(SelectionSet set)
        {
            return new SetRowViewModel
            {
                Id = set.Id,
                Name = set.Name,
                ElementCount = set.UniqueIds.Count,
                HealthStatus = set.HealthStatus,
                StaleCount = set.StaleCount
            };
        }

        public void UpdateFromModel(SelectionSet set)
        {
            Name = set.Name;
            ElementCount = set.UniqueIds.Count;
            HealthStatus = set.HealthStatus;
            StaleCount = set.StaleCount;
        }
    }
}
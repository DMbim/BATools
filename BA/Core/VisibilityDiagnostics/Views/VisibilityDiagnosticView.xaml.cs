// File: BA_Tools/VisibilityDiagnostics/Views/VisibilityDiagnosticView.xaml.cs
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using BA.VisibilityDiagnostics.Models;
using BA.VisibilityDiagnostics.ViewModels;
using Color = System.Windows.Media.Color;

namespace BA.VisibilityDiagnostics.Views
{
    public partial class VisibilityDiagnosticView : Window
    {
        public VisibilityDiagnosticView(VisibilityDiagnosticViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        }
    }

    public sealed class CheckStatusToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value switch
            {
                CheckStatus.Visible => new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)),
                CheckStatus.Hidden => new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)),
                CheckStatus.Warning => new SolidColorBrush(Color.FromRgb(0xF9, 0xA8, 0x25)),
                CheckStatus.NotApplicable => new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)),
                CheckStatus.Error => new SolidColorBrush(Color.FromRgb(0x6A, 0x1B, 0x9A)),
                _ => Brushes.Gray,
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public sealed class CheckStatusToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value switch
            {
                CheckStatus.Visible => "OK",
                CheckStatus.Hidden => "HIDDEN",
                CheckStatus.Warning => "WARNING",
                CheckStatus.NotApplicable => "N/A",
                CheckStatus.Error => "ERROR",
                _ => value?.ToString() ?? string.Empty,
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}

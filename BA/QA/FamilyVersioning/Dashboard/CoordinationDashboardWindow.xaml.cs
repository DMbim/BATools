using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using BA.QA.FamilyVersioning.Converters;
using Binding = System.Windows.Data.Binding;
using Color = System.Windows.Media.Color;

namespace BA.QA.FamilyVersioning.Dashboard
{
    public partial class CoordinationDashboardWindow : Window
    {
        private readonly CoordinationDashboardViewModel _viewModel;

        public CoordinationDashboardWindow(CoordinationDashboardViewModel viewModel)
        {
            InitializeComponent();

            _viewModel = viewModel;
            DataContext = viewModel;

            viewModel.ColumnsChanged += RegenerateBuildingColumns;

            // CloseRequested from the Close button command closes the window,
            // which in turn triggers the Closed event below for cleanup.
            viewModel.CloseRequested += () => Close();

            // Window.Closed covers both the Close button and the X button.
            // RequestCleanup is idempotent so it is safe to call from both paths.
            Closed += (s, e) => viewModel.RequestCleanup();

            Loaded += (s, e) => viewModel.Refresh();
        }

        private void RegenerateBuildingColumns()
        {
            while (FamilyGrid.Columns.Count > 3)
            {
                FamilyGrid.Columns.RemoveAt(FamilyGrid.Columns.Count - 1);
            }

            foreach (var building in _viewModel.EnabledBuildings)
            {
                var capturedBuilding = building;

                var column = new DataGridTemplateColumn
                {
                    Header = capturedBuilding.BuildingName,
                    Width = new DataGridLength(100)
                };

                var cellTemplate = new DataTemplate();
                var textBlockFactory = new FrameworkElementFactory(typeof(TextBlock));

                var cellConverter = new BuildingCellDisplayConverter(capturedBuilding.BuildingId);
                textBlockFactory.SetBinding(TextBlock.TextProperty, new Binding(".")
                {
                    Converter = cellConverter
                });

                var bgConverter = new BuildingCellMismatchConverter(capturedBuilding.BuildingId);
                textBlockFactory.SetBinding(TextBlock.BackgroundProperty, new Binding(".")
                {
                    Converter = bgConverter
                });

                var tooltipConverter = new AuditTooltipConverter(
                    capturedBuilding.BuildingId, _viewModel.Factory);
                textBlockFactory.SetBinding(TextBlock.ToolTipProperty, new Binding(".")
                {
                    Converter = tooltipConverter
                });

                textBlockFactory.SetValue(TextBlock.PaddingProperty, new Thickness(8, 4, 8, 4));
                textBlockFactory.SetValue(TextBlock.ForegroundProperty,
                    new SolidColorBrush(Color.FromRgb(224, 224, 224)));

                cellTemplate.VisualTree = textBlockFactory;
                column.CellTemplate = cellTemplate;

                FamilyGrid.Columns.Add(column);
            }
        }
    }

    internal sealed class BuildingCellDisplayConverter : System.Windows.Data.IValueConverter
    {
        private readonly int _buildingId;
        public BuildingCellDisplayConverter(int buildingId) => _buildingId = buildingId;

        public object Convert(object value, System.Type targetType,
            object parameter, System.Globalization.CultureInfo culture)
            => value is FamilyDashboardRow row ? row.GetCellDisplay(_buildingId) : "-";

        public object ConvertBack(object value, System.Type targetType,
            object parameter, System.Globalization.CultureInfo culture)
            => throw new System.NotSupportedException();
    }

    internal sealed class BuildingCellMismatchConverter : System.Windows.Data.IValueConverter
    {
        private readonly int _buildingId;
        private static readonly SolidColorBrush MismatchBrush =
            new SolidColorBrush(Color.FromArgb(80, 155, 35, 53));
        private static readonly SolidColorBrush TransparentBrush =
            new SolidColorBrush(Colors.Transparent);

        public BuildingCellMismatchConverter(int buildingId) => _buildingId = buildingId;

        public object Convert(object value, System.Type targetType,
            object parameter, System.Globalization.CultureInfo culture)
            => value is FamilyDashboardRow row && row.IsCellMismatched(_buildingId)
                ? MismatchBrush
                : TransparentBrush;

        public object ConvertBack(object value, System.Type targetType,
            object parameter, System.Globalization.CultureInfo culture)
            => throw new System.NotSupportedException();
    }
}

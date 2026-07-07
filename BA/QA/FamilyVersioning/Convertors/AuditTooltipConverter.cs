using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Data;
using BA.QA.FamilyVersioning.Dashboard;
using BA.QA.FamilyVersioning.Data;

namespace BA.QA.FamilyVersioning.Converters
{
    /// <summary>
    /// Single-value converter that produces a tooltip string for a version cell.
    /// BuildingId and CatalogConnectionFactory are baked in at construction time,
    /// matching the pattern used by BuildingCellDisplayConverter and
    /// BuildingCellMismatchConverter in the code-behind, since WPF binding
    /// ConverterParameter does not support dynamic binding values.
    ///
    /// Performs a single indexed AuditLog query per tooltip show. This is acceptable
    /// since WPF tooltips are lazy (only evaluated on hover) and the query returns
    /// at most 1 row from an indexed table.
    /// </summary>
    public sealed class AuditTooltipConverter : IValueConverter
    {
        private readonly int _buildingId;
        private readonly CatalogConnectionFactory? _factory;

        /// <summary>
        /// Parameterless constructor required for XAML instantiation as a resource.
        /// When used this way the converter returns empty string since it has no
        /// BuildingId or factory context. The functional constructor below is used
        /// by the code-behind for dynamic column generation.
        /// </summary>
        public AuditTooltipConverter() { }

        public AuditTooltipConverter(int buildingId, CatalogConnectionFactory factory)
        {
            _buildingId = buildingId;
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public object Convert(object value, Type targetType,
            object parameter, CultureInfo culture)
        {
            if (_factory == null || value is not FamilyDashboardRow row)
            {
                return string.Empty;
            }

            try
            {
                var auditRepo = new AuditLogRepository(_factory);
                var history = auditRepo.GetHistoryForFamily(row.FamilyId, maxRows: 10);

                var entry = history.FirstOrDefault(e => e.BuildingId == _buildingId);
                if (entry == null)
                {
                    return "No audit history for this building yet.";
                }

                var localTime = entry.EventUtc.ToLocalTime();
                var sb = new StringBuilder();
                sb.AppendLine($"Last event : {entry.EventType}");
                sb.AppendLine($"By         : {entry.EventUser}");
                sb.AppendLine($"At         : {localTime:dd MMM yyyy HH:mm}");

                if (!string.IsNullOrWhiteSpace(entry.Detail))
                {
                    var detail = entry.Detail.Length > 400
                        ? entry.Detail.Substring(0, 397) + "..."
                        : entry.Detail;

                    sb.AppendLine();
                    sb.Append(detail);
                }

                return sb.ToString().TrimEnd();
            }
            catch
            {
                return "Could not load audit history.";
            }
        }

        public object ConvertBack(object value, Type targetType,
            object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}

using BA.Core.Standards;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Data;

namespace BA.UI.Standards
{
    public sealed class SubcategoryAuditorViewModel : INotifyPropertyChanged
    {
        private readonly ObservableCollection<SubcategoryAuditRow> _rows = new ObservableCollection<SubcategoryAuditRow>();
        private string _searchText = "";
        private bool _showOnlyIssues;
        private bool _showMissingSemanticOnly;
        private bool _strictMode;
        private bool _warnIfNoBaNames = true;
        private bool _isBusy;
        private string _summaryText = "No data.";

        public ObservableCollection<SubcategoryAuditRow> Rows => _rows;

        public ICollectionView RowsView { get; }

        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value ?? "";
                OnPropertyChanged();
                RowsView.Refresh();
            }
        }

        public bool ShowOnlyIssues
        {
            get => _showOnlyIssues;
            set
            {
                _showOnlyIssues = value;
                OnPropertyChanged();
                RowsView.Refresh();
            }
        }

        public bool ShowMissingSemanticOnly
        {
            get => _showMissingSemanticOnly;
            set
            {
                _showMissingSemanticOnly = value;
                OnPropertyChanged();
                RowsView.Refresh();
            }
        }

        public bool StrictMode
        {
            get => _strictMode;
            set
            {
                _strictMode = value;
                OnPropertyChanged();
            }
        }

        public bool WarnIfNoBaNames
        {
            get => _warnIfNoBaNames;
            set
            {
                _warnIfNoBaNames = value;
                OnPropertyChanged();
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                _isBusy = value;
                OnPropertyChanged();
            }
        }

        public string SummaryText
        {
            get => _summaryText;
            set
            {
                _summaryText = value ?? "";
                OnPropertyChanged();
            }
        }

        public SubcategoryAuditOptions BuildOptions()
        {
            return new SubcategoryAuditOptions
            {
                StrictMode = StrictMode,
                WarnIfNoBaNames = WarnIfNoBaNames
            };
        }

        public SubcategoryAuditorViewModel()
        {
            RowsView = CollectionViewSource.GetDefaultView(_rows);
            RowsView.Filter = FilterRow;
        }

        private bool FilterRow(object obj)
        {
            if (obj is not SubcategoryAuditRow row)
                return false;

            if (ShowOnlyIssues && !row.HasIssues)
                return false;

            if (ShowMissingSemanticOnly && !row.HasMissingRequired)
                return false;

            if (string.IsNullOrWhiteSpace(SearchText))
                return true;

            string search = SearchText.Trim();

            return Contains(row.FamilyName, search)
                || Contains(row.CategoryName, search)
                || Contains(row.StatusText, search)
                || Contains(row.ExistingSubcategories, search)
                || Contains(row.ValidBaNames, search)
                || Contains(row.MissingRequired, search)
                || Contains(row.AllowedNonBaNames, search)
                || Contains(row.NonCompliantNames, search)
                || Contains(row.Notes, search);
        }

        private static bool Contains(string source, string search)
        {
            if (string.IsNullOrWhiteSpace(source))
                return false;

            return source.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public void LoadRows(IEnumerable<SubcategoryAuditRow> rows, SubcategoryAuditSummary summary)
        {
            _rows.Clear();

            foreach (SubcategoryAuditRow row in rows ?? Enumerable.Empty<SubcategoryAuditRow>())
                _rows.Add(row);

            SummaryText = BuildSummaryText(summary);
            RowsView.Refresh();
        }

        private static string BuildSummaryText(SubcategoryAuditSummary summary)
        {
            if (summary == null)
                return "No data.";

            return
                $"Total: {summary.TotalRows}   " +
                $"Clean: {summary.CleanCount}   " +
                $"Warnings: {summary.WarningCount}   " +
                $"Errors: {summary.ErrorCount}   " +
                $"Skipped: {summary.SkippedCount}   " +
                $"Missing Required: {summary.MissingRequiredCount}   " +
                $"Non-Compliant Names: {summary.NonCompliantNameCount}   " +
                $"Allowed Non-BA: {summary.AllowedNonBaCount}   " +
                $"Valid BA: {summary.ValidBaCount}   " +
                $"No BA Names: {summary.NoBaNamesCount}";
        }

        public string ExportToCsv()
        {
            SaveFileDialog dlg = new SaveFileDialog
            {
                Title = "Export BA Subcategory Audit",
                Filter = "CSV files (*.csv)|*.csv",
                DefaultExt = ".csv",
                FileName = "BA_SubcategoryAudit.csv",
                AddExtension = true,
                OverwritePrompt = true
            };

            bool? ok = dlg.ShowDialog();
            if (ok != true)
                return null;

            string path = dlg.FileName;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("FamilyId,FamilyName,Category,Status,ExistingSubcategories,ValidBaNames,MissingRequired,AllowedNonBaNames,NonCompliantNames,Notes");

            foreach (SubcategoryAuditRow row in Rows)
            {
                sb.Append(Escape(row.FamilyId.ToString(CultureInfo.InvariantCulture))).Append(",");
                sb.Append(Escape(row.FamilyName)).Append(",");
                sb.Append(Escape(row.CategoryName)).Append(",");
                sb.Append(Escape(row.StatusText)).Append(",");
                sb.Append(Escape(row.ExistingSubcategories)).Append(",");
                sb.Append(Escape(row.ValidBaNames)).Append(",");
                sb.Append(Escape(row.MissingRequired)).Append(",");
                sb.Append(Escape(row.AllowedNonBaNames)).Append(",");
                sb.Append(Escape(row.NonCompliantNames)).Append(",");
                sb.Append(Escape(row.Notes)).AppendLine();
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            return path;
        }

        public string BuildClipboardSummary()
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine(SummaryText);
            sb.AppendLine();

            foreach (SubcategoryAuditRow row in Rows.Where(x => x.HasIssues || x.Status == AuditRowStatus.Skipped))
            {
                sb.AppendLine($"{row.FamilyName} | {row.CategoryName} | {row.StatusText}");

                if (!string.IsNullOrWhiteSpace(row.ValidBaNames))
                    sb.AppendLine($"  Valid BA: {row.ValidBaNames}");

                if (!string.IsNullOrWhiteSpace(row.MissingRequired))
                    sb.AppendLine($"  Missing required: {row.MissingRequired}");

                if (!string.IsNullOrWhiteSpace(row.AllowedNonBaNames))
                    sb.AppendLine($"  Allowed non-BA: {row.AllowedNonBaNames}");

                if (!string.IsNullOrWhiteSpace(row.NonCompliantNames))
                    sb.AppendLine($"  Non-compliant: {row.NonCompliantNames}");

                if (!string.IsNullOrWhiteSpace(row.Notes))
                    sb.AppendLine($"  Notes: {row.Notes}");

                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string Escape(string value)
        {
            string s = value ?? "";
            s = s.Replace("\"", "\"\"");
            return $"\"{s}\"";
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
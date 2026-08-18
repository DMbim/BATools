using Autodesk.Revit.UI;
using BA.Core.Standards;
using System;
using System.Collections.Generic;
using System.Windows;

namespace BA.UI.Standards
{
    public partial class SubcategoryAuditorWindow : Window
    {
        private readonly UIApplication _uiApp;
        private readonly SubcategoryAuditorViewModel _vm;
        private readonly SubcategoryAuditService _auditService;

        private readonly SubcategoryAuditExternalHandler _handler;
        private readonly ExternalEvent _externalEvent;

        public SubcategoryAuditorWindow(UIApplication uiApp)
        {
            InitializeComponent();

            _uiApp = uiApp ?? throw new ArgumentNullException(nameof(uiApp));
            _vm = new SubcategoryAuditorViewModel();
            _auditService = new SubcategoryAuditService();

            DataContext = _vm;

            _handler = new SubcategoryAuditExternalHandler();
            _externalEvent = ExternalEvent.Create(_handler);

            _handler.ExecuteFunc = RunAudit;
            _handler.SuccessAction = OnAuditSuccess;
            _handler.ErrorAction = OnAuditError;
        }

        public void RequestScan()
        {
            if (_vm.IsBusy)
                return;

            _vm.IsBusy = true;
            _externalEvent.Raise();
        }

        private IList<SubcategoryAuditRow> RunAudit(UIApplication app)
        {
            SubcategoryAuditOptions options = _vm.BuildOptions();
            return _auditService.AuditProjectFamilies(app.ActiveUIDocument.Document, options);
        }

        private void OnAuditSuccess(IList<SubcategoryAuditRow> rows)
        {
            Dispatcher.Invoke(() =>
            {
                SubcategoryAuditSummary summary = _auditService.BuildSummary(rows);
                _vm.LoadRows(rows, summary);
                _vm.IsBusy = false;
            });
        }

        private void OnAuditError(Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                _vm.IsBusy = false;
                MessageBox.Show(
                    this,
                    ex.Message,
                    "BA Subcategory Auditor",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            });
        }

        private void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            RequestScan();
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = _vm.ExportToCsv();

                if (!string.IsNullOrWhiteSpace(path))
                {
                    MessageBox.Show(
                        this,
                        $"CSV exported successfully:\n{path}",
                        "BA Subcategory Auditor",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "Export failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void BtnCopySummary_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string text = _vm.BuildClipboardSummary();
                Clipboard.SetText(text ?? "");
                MessageBox.Show(
                    this,
                    "Issue summary copied to clipboard.",
                    "BA Subcategory Auditor",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "Clipboard failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
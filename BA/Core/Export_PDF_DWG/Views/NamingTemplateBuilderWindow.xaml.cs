using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BA.Core.Export.Models;
using BA.ViewModels.Export;

namespace BA.Views.Export
{
    /// <summary>
    /// Builds a {Token:format} naming template. Left side is a read only,
    /// searchable grid of every available token, built-in synthesized
    /// tokens (SheetNumber, Date, Revision, and so on) and real Revit
    /// parameter names both, same searchable picker pattern already used
    /// by ParameterColumnPickerWindow. Right side is the ordered template
    /// being built, edited through Add/Move/Remove controls, not by typing
    /// directly into the row list. The underlying stored template is still
    /// exactly the same {Token:format} string NamingTemplateEngine already
    /// parses, this window is a friendlier way to construct that string.
    ///
    /// Never touches Document or any Revit API type. Available parameter
    /// names and preview resolution are both supplied by the caller
    /// (ExportJobEditorViewModel), which already routes those through
    /// ExportUiBridge, same discipline as SheetPickerWindow.
    /// </summary>
    public partial class NamingTemplateBuilderWindow : Window
    {
        // Classifies incoming names for the Type column only. The
        // authoritative set of synthesized (non-Revit) tokens actually
        // lives in NamingTemplateEngine's switch statement and is mirrored
        // here and in ExportJobEditorViewModel.EditNaming(), three places
        // now carrying the same six names. Low risk since this set changes
        // rarely, but worth consolidating into one shared constant if a
        // fourth synthesized token is ever added.
        private static readonly HashSet<string> BuiltInTokenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SheetNumber", "SheetName", "ProjectNumber", "ProjectName", "Date", "Revision"
        };

        private readonly Action<string, Action<List<NamingPreviewResult>>> _requestPreview;
        private bool _suppressDetailTextChanged;

        private readonly ObservableCollection<AvailableTokenRowViewModel> _allTokens = new ObservableCollection<AvailableTokenRowViewModel>();
        public ObservableCollection<AvailableTokenRowViewModel> FilteredTokens { get; } = new ObservableCollection<AvailableTokenRowViewModel>();

        public ObservableCollection<NamingSegmentRowViewModel> Rows { get; }

        public string ResultTemplate { get; private set; } = string.Empty;

        public NamingTemplateBuilderWindow(
            string initialTemplate,
            IEnumerable<string> availableParameterNames,
            Action<string, Action<List<NamingPreviewResult>>> requestPreview)
        {
            InitializeComponent();

            _requestPreview = requestPreview ?? throw new ArgumentNullException(nameof(requestPreview));

            Rows = NamingTemplateSegmentConverter.Parse(initialTemplate);
            SegmentsList.ItemsSource = Rows;

            foreach (var name in availableParameterNames ?? Enumerable.Empty<string>())
            {
                _allTokens.Add(new AvailableTokenRowViewModel(name, BuiltInTokenNames.Contains(name)));
            }

            ApplyTokenFilter(string.Empty);
            TokensGrid.ItemsSource = FilteredTokens;

            UpdateDetailPanel();
        }

        private void TokenSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyTokenFilter(TokenSearchBox.Text);
        }

        private void ApplyTokenFilter(string filterText)
        {
            FilteredTokens.Clear();

            var matches = string.IsNullOrWhiteSpace(filterText)
                ? _allTokens
                : _allTokens.Where(t => t.TokenName.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0);

            foreach (var token in matches)
            {
                FilteredTokens.Add(token);
            }
        }

        private void AddSelectedToken_Click(object sender, RoutedEventArgs e)
        {
            if (TokensGrid.SelectedItem is AvailableTokenRowViewModel token)
            {
                InsertAfterSelection(NamingSegmentRowViewModel.CreateParameter(token.TokenName));
            }
        }

        private void TokensGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            AddSelectedToken_Click(sender, null);
        }

        private void SeparatorButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string separator)
            {
                InsertAfterSelection(NamingSegmentRowViewModel.CreateLiteral(separator));
            }
        }

        private void AddCustomText_Click(object sender, RoutedEventArgs e)
        {
            var text = CustomTextBox.Text;

            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            InsertAfterSelection(NamingSegmentRowViewModel.CreateLiteral(text));
            CustomTextBox.Clear();
        }

        private void InsertAfterSelection(NamingSegmentRowViewModel row)
        {
            var insertIndex = SegmentsList.SelectedIndex >= 0 ? SegmentsList.SelectedIndex + 1 : Rows.Count;
            Rows.Insert(insertIndex, row);
            SegmentsList.SelectedIndex = insertIndex;
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (SegmentsList.SelectedItem is NamingSegmentRowViewModel row)
            {
                Rows.Remove(row);
            }
        }

        private void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            var index = SegmentsList.SelectedIndex;

            if (index > 0)
            {
                Rows.Move(index, index - 1);
                SegmentsList.SelectedIndex = index - 1;
            }
        }

        private void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            var index = SegmentsList.SelectedIndex;

            if (index >= 0 && index < Rows.Count - 1)
            {
                Rows.Move(index, index + 1);
                SegmentsList.SelectedIndex = index + 1;
            }
        }

        private void SegmentsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateDetailPanel();
        }

        private void UpdateDetailPanel()
        {
            _suppressDetailTextChanged = true;

            if (!(SegmentsList.SelectedItem is NamingSegmentRowViewModel selected))
            {
                RowDetailLabel.Text = "Select a row to edit it.";
                RowDetailBox.Text = string.Empty;
                RowDetailBox.IsEnabled = false;
            }
            else if (selected.Kind == NamingSegmentKind.Literal)
            {
                RowDetailLabel.Text = "Literal text:";
                RowDetailBox.Text = selected.LiteralText ?? string.Empty;
                RowDetailBox.IsEnabled = true;
            }
            else
            {
                RowDetailLabel.Text = $"Format override for {{{selected.ParameterName}}} (optional, e.g. yyyyMMdd or 00):";
                RowDetailBox.Text = selected.FormatOverride ?? string.Empty;
                RowDetailBox.IsEnabled = true;
            }

            _suppressDetailTextChanged = false;
        }

        private void RowDetailBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressDetailTextChanged || !(SegmentsList.SelectedItem is NamingSegmentRowViewModel selected))
            {
                return;
            }

            if (selected.Kind == NamingSegmentKind.Literal)
            {
                selected.LiteralText = RowDetailBox.Text;
            }
            else
            {
                selected.FormatOverride = RowDetailBox.Text;
            }
        }

        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            var template = NamingTemplateSegmentConverter.Build(Rows);

            ResultPreviewBox.Text = "Resolving...";

            _requestPreview(template, results =>
            {
                if (results == null || results.Count == 0)
                {
                    ResultPreviewBox.Text = "No export format is enabled on this job.";
                    return;
                }

                ResultPreviewBox.Text = string.Join("\r\n", results.Select(r =>
                    r.Success ? $"{r.Format}: {r.FileName}\r\n{r.Folder}" : $"{r.Format}: Error - {r.ErrorMessage}"));
            });
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            ResultTemplate = NamingTemplateSegmentConverter.Build(Rows);
            DialogResult = true;
            Close();
        }
    }
}

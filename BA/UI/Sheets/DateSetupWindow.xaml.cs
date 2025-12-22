using System.Windows;
using Autodesk.Revit.UI;
using BA.Settings;

namespace BA.UI.Sheets
{
    public partial class DateSetupWindow : Window
    {
        private readonly DateToolSettings _settings;

        public DateSetupWindow(ExternalCommandData commandData, DateToolSettings settings)
        {
            InitializeComponent();
            _settings = settings;

            if (FormatComboBox.Items.Count == 0)
            {
                FormatComboBox.Items.Add("yy/MM/dd");
                FormatComboBox.Items.Add("dd/MM/yy");
                FormatComboBox.Items.Add("MM/dd/yy");
                FormatComboBox.Items.Add("yyyy-MM-dd");
                FormatComboBox.Items.Add("dd.MM.yyyy");
            }

            FormatComboBox.Text = _settings.SelectedFormat ?? "yy/MM/dd";
            DateParameterTextBox.Text = _settings.SelectedDateParam ?? "Issue Date";
            RevisionParameterTextBox.Text = _settings.SelectedRevParam ?? "Revision";
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            _settings.SelectedFormat = string.IsNullOrWhiteSpace(FormatComboBox.Text) ? "yy/MM/dd" : FormatComboBox.Text.Trim();
            _settings.SelectedDateParam = string.IsNullOrWhiteSpace(DateParameterTextBox.Text) ? "Issue Date" : DateParameterTextBox.Text.Trim();
            _settings.SelectedRevParam = string.IsNullOrWhiteSpace(RevisionParameterTextBox.Text) ? "Revision" : RevisionParameterTextBox.Text.Trim();

            DialogResult = true;
            Close();
        }
    }
}

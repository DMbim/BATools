using System.Windows;
using BA.BIM.Core.ViewScoping;

namespace BA.BIM.Commands.Anno
{
    public sealed class TagAllBatchScopeResult
    {
        public BA_ViewScopeMode Mode { get; set; }
    }

    public partial class TagAllBatchScopeDialog : Window
    {
        private TagAllBatchScopeResult _result;

        private TagAllBatchScopeDialog()
        {
            InitializeComponent();
        }

        public static TagAllBatchScopeResult GetResult()
        {
            var dlg = new TagAllBatchScopeDialog();
            bool? result = dlg.ShowDialog();

            if (result != true || dlg._result == null)
                return null;

            return dlg._result;
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            _result = new TagAllBatchScopeResult
            {
                Mode = RbAllFloorPlans.IsChecked == true
                    ? BA_ViewScopeMode.AllFloorPlans
                    : BA_ViewScopeMode.ActiveViewOnly
            };
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _result = null;
            DialogResult = false;
            Close();
        }
    }
}
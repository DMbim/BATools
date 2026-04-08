using Autodesk.Revit.UI;
using System.Windows;

namespace BA
{
    public partial class SyncWindow : Window
    {
        public SyncWindow(ExternalCommandData data)
        {
            InitializeComponent();
            DataContext = new SyncViewModel(data);
        }
    }
}

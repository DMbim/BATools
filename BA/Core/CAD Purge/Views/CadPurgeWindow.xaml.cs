// File: BA_Tools/CadPurge/Views/CadPurgeWindow.xaml.cs
using System.Windows;
using BA.CadPurge.ViewModels;

namespace BA.CadPurge.Views
{
    public partial class CadPurgeWindow : Window
    {
        public CadPurgeWindow()
        {
            InitializeComponent();
            DataContext = new CadPurgeViewModel();
        }
    }
}
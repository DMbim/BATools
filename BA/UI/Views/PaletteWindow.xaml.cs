using System.Windows;

namespace BA.UI.Views
{
    // Deliberately shares the main window's Vm as its own DataContext rather than
    // owning a separate ViewModel. Palette, AutoAssignCommand, EditPaletteColorCommand,
    // ImportPaletteCommand, and ExportPaletteCommand are all already on
    // BAViewFilterColorManagerVm and operate correctly regardless of which window is
    // showing them, there is nothing palette-specific enough here to justify a second
    // VM class and the sync problems that would come with keeping two in step.
    public partial class PaletteWindow : Window
    {
        public PaletteWindow(BAViewFilterColorManagerVm vm)
        {
            InitializeComponent();
            DataContext = vm;
        }
    }
}
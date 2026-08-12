using System;

namespace BA.UI.LoadedFamilyBrowser
{
    public partial class TypeParameterEditorWindow : System.Windows.Window
    {
        public TypeParameterEditorWindow(TypeParameterEditorViewModel viewModel, IntPtr ownerHandle)
        {
            InitializeComponent();

            DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            viewModel.RequestClose += () => DialogResult = true;

            if (ownerHandle != IntPtr.Zero)
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                helper.Owner = ownerHandle;
            }
        }
    }
}
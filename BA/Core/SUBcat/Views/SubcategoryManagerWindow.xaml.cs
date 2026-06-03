using BA.Subcategories.Models;
using BA.Subcategories.ViewModels;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System;
using Color = System.Windows.Media.Color;



namespace BA.Subcategories.Views
{
    public partial class SubcategoryManagerWindow : Window
    {
        private readonly SubcategoryManagerViewModel _vm;

        public SubcategoryManagerWindow(SubcategoryManagerViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = _vm;

            _vm.RequestClose = () =>
            {
                DialogResult = _vm.Applied;
                Close();
            };

            // Use the inline WPF color picker dialog instead of WinForms
            _vm.RequestColorPick = currentColor =>
                ShowColorPickerDialog(currentColor);
        }

        private System.Windows.Media.Color? ShowColorPickerDialog(System.Windows.Media.Color currentColor)
        {
            throw new NotImplementedException();
        }

        // ── Inline color picker ───────────────────────────────────────────────

        private static Color? ShowColorPickerDialog(Autodesk.Revit.DB.Color current)
        {
            var picker = new ColorPickerWindow(current)
            {
                Owner = Application.Current.MainWindow
            };

            return picker.ShowDialog() != true ? null : picker.SelectedColor;
        }

        // ── Color swatch click ────────────────────────────────────────────────

        private void ColorSwatch_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is SubcategoryRow row)
            {
                _vm.SelectedSubcategory = row;
                _vm.PickColorCommand.Execute(null);
            }
        }

        // ── Delete button ─────────────────────────────────────────────────────

        private void BtnDeleteSubcategory_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is SubcategoryRow row)
            {
                _vm.SelectedSubcategory = row;
                _vm.DeleteSubcategoryCommand.Execute(null);
            }
        }
        // ── Select All / None ─────────────────────────────────────────────────

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _vm.GeometryItems)
                item.IsSelected = true;
        }

        private void BtnSelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _vm.GeometryItems)
                item.IsSelected = false;
        }
    }
}
    


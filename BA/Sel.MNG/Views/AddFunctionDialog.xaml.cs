using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using BATools.SelectionManager.Actions;
using BATools.SelectionManager.Models;
using BATools.SelectionManager.Services;

namespace BATools.SelectionManager.Views
{
    /// <summary>Flat view model for the combined function list.</summary>
    public class AddFunctionDialogItem
    {
        public string DisplayName { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public IQuickAction Action { get; init; } = null!;
    }

    public partial class AddFunctionDialog : Window
    {
        public IQuickAction? SelectedAction { get; private set; }

        private readonly List<AddFunctionDialogItem> _allItems;

        public AddFunctionDialog(List<IQuickAction> pluginActions)
        {
            InitializeComponent();

            // Build combined list: plugin actions first, then full Revit catalog
            _allItems = pluginActions
                .Select(a => new AddFunctionDialogItem
                {
                    DisplayName = a.DefaultLabel,
                    Category = "Plugin",
                    Action = a
                })
                .Concat(RevitCommandCatalog.All.Select(e =>
                    new AddFunctionDialogItem
                    {
                        DisplayName = e.DisplayName,
                        Category = e.Category,
                        Action = new RevitPostableAction(
                                          e.Command, e.DisplayName, e.Category)
                    }))
                .ToList();

            ActionsList.ItemsSource = _allItems;

            if (_allItems.Count > 0)
                ActionsList.SelectedIndex = 0;

            Loaded += (_, _) => TxtSearch.Focus();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void TxtSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            string term = TxtSearch.Text.Trim();
            ActionsList.ItemsSource = string.IsNullOrEmpty(term)
                ? _allItems
                : _allItems.Where(i =>
                    i.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    i.Category.Contains(term, StringComparison.OrdinalIgnoreCase))
                  .ToList();

            ActionsList.SelectedIndex = ActionsList.Items.Count > 0 ? 0 : -1;
        }

        private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down && ActionsList.Items.Count > 0)
            {
                ActionsList.Focus();
                ActionsList.SelectedIndex = 0;
                e.Handled = true;
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var item = ActionsList.SelectedItem as AddFunctionDialogItem;
            if (item == null) return;
            SelectedAction = item.Action;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;

        private void ActionsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var item = ActionsList.SelectedItem as AddFunctionDialogItem;
            if (item != null)
            {
                SelectedAction = item.Action;
                DialogResult = true;
            }
        }
    }
}
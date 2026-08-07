// BA/Markup/Views/RevisionManagerWindow.xaml.cs
using System;
using System.Windows;
using System.Windows.Threading;
using Autodesk.Revit.UI;
using BA.Markup.ViewModels;

namespace BA.Markup.Views
{
    public partial class RevisionManagerWindow : Window
    {
        private readonly RevisionManagerViewModel _viewModel;

        public RevisionManagerWindow(RevisionManagerViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;
            viewModel.CloseRequested += (_, _) => Close();
            Loaded += OnLoaded;

            // <- NEW: diagnostic safety net. Revit's host UI thread does not always
            //    survive an unhandled exception raised from add-in WPF code, this
            //    hooks the dispatcher for THIS window (Revit is single UI thread,
            //    so this is the same dispatcher every command handler runs on) and
            //    converts an otherwise fatal crash into a visible TaskDialog with
            //    the real stack trace. Remove once the underlying cause is fixed
            //    and confirmed stable, this is not meant to stay as production
            //    error handling long term.
            Dispatcher.UnhandledException += OnDispatcherUnhandledException;
            Closed += (_, _) =>
                Dispatcher.UnhandledException -= OnDispatcherUnhandledException;
        }

        private void OnDispatcherUnhandledException(
            object sender,
            DispatcherUnhandledExceptionEventArgs e)
        {
            BA.BAApplication.AppLogger.LogError(
                "RevisionManagerWindow.UnhandledException",
                e.Exception);

            TaskDialog.Show(
                "BA Revision Manager - Unhandled Exception",
                e.Exception.ToString());

            // Prevent Revit's process from going down. This does not fix the
            // underlying bug, it only stops it from taking the host with it
            // while we diagnose.
            e.Handled = true;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _viewModel.Initialize();
        }
    }
}
using System;
using System.Windows.Input;

namespace BA.Core.Mvvm
{
    /// <summary>
    /// Explicit ICommand implementation. This project avoids CommunityToolkit.Mvvm's
    /// [RelayCommand] source generator since it has been unreliable here; use this instead.
    /// If BA.Core.Mvvm.RelayCommand already exists elsewhere in the project, delete this
    /// duplicate and reference the existing one.
    /// </summary>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public RelayCommand(Action execute, Func<bool> canExecute = null)
            : this(_ => execute(), canExecute == null ? (Func<object, bool>)null : _ => canExecute())
        {
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute?.Invoke(parameter) ?? true;
        }

        public void Execute(object parameter)
        {
            _execute(parameter);
        }

        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }
}

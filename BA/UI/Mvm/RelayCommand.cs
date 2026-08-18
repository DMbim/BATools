// File: BA.UI/Mvvm/RelayCommand.cs
using System;
using System.Windows.Input;

namespace BA.UI.Mvvm
{
    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <summary>
        /// Convenience overload for parameterless command handlers (method
        /// groups or lambdas with no CommandParameter usage). Wraps into the
        /// canonical Action&lt;object?&gt; constructor above.
        /// </summary>
        public RelayCommand(Action execute, Func<bool>? canExecute = null)
            : this(
                execute is null ? throw new ArgumentNullException(nameof(execute)) : _ => execute(),
                canExecute is null ? (Func<object?, bool>?)null : _ => canExecute())
        {
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object? parameter) => _execute(parameter);

        public event EventHandler? CanExecuteChanged;

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
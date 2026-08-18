// File: BA.UI/Mvvm/AsyncRelayCommand.cs
using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace BA.UI.Mvvm
{
    /// <summary>
    /// Async counterpart to RelayCommand. Re-entrancy is guarded internally:
    /// while the wrapped Func&lt;Task&gt; is running, CanExecute returns false for
    /// THIS command automatically, so double-invoking Execute (e.g. a fast double
    /// click before the UI disables the button) can't start a second overlapping
    /// run. This does NOT disable sibling commands (e.g. a Place command while
    /// Scan is running) - that's the ViewModel's job via its own IsBusy flag
    /// passed into each command's canExecute delegate, same as RelayCommand's
    /// pattern already expects.
    ///
    /// Execute is `async void` because ICommand.Execute's signature is void -
    /// this is the standard, accepted shape for ICommand implementations, not a
    /// mistake. The exception handling burden shifts to the delegate passed in:
    /// if executeAsync lets an exception escape uncaught, it becomes an
    /// unobserved exception on the UI dispatcher. Callers (e.g.
    /// BA_AutoDimensionViewModel) must catch inside their async method body,
    /// which the dimensioner ViewModel already does.
    /// </summary>
    public sealed class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> _executeAsync;
        private readonly Func<bool>? _canExecute;
        private bool _isExecuting;

        public AsyncRelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null)
        {
            _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke() ?? true);

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter)) return;

            _isExecuting = true;
            RaiseCanExecuteChanged();

            try
            {
                await _executeAsync();
            }
            finally
            {
                _isExecuting = false;
                RaiseCanExecuteChanged();
            }
        }

        public event EventHandler? CanExecuteChanged;
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
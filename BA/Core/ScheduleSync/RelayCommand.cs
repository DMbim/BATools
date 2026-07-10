using System;
using System.Windows.Input;

namespace BA
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private Action ensureCoreSubcategories;

        public Action<object> ExecuteBrowse { get; }

        public RelayCommand(Action<object> execute, Func<bool> value)
        {
            _execute = execute;
        }

        public RelayCommand(Action ensureCoreSubcategories, Func<bool> value)
        {
            this.ensureCoreSubcategories = ensureCoreSubcategories;
        }

        public RelayCommand(Action ensureCoreSubcategories)
        {
            this.ensureCoreSubcategories = ensureCoreSubcategories;
        }

        public RelayCommand(Action<object> executeBrowse, Func<object, bool> canSave)
        {
            ExecuteBrowse = executeBrowse;
        }

        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter) => _execute(parameter);

        internal void NotifyCanExecuteChanged()
        {
            throw new NotImplementedException();
        }

        public event EventHandler CanExecuteChanged;
    }
}

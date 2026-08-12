namespace BA.ViewModels.Export
{
    public class SheetPickerRowViewModel : BA.UI.Mvvm.ObservableObject
    {
        public string SheetNumber { get; }
        public string SheetName { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        /// <summary>
        /// Dynamic parameter column values, keyed by
        /// ParameterColumnDescriptor.ColumnKey. Populated on demand when a
        /// column is added through the header context menu, not eagerly
        /// for every parameter that could theoretically be shown.
        /// </summary>
        public ObservableParameterValueBag ParameterValues { get; } = new ObservableParameterValueBag();

        private string _paperSizeDisplay = "Detecting...";

        /// <summary>
        /// Always shown, fixed column, not a dynamic parameter column.
        /// Populated automatically when the picker window opens, this is
        /// derived from the placed title block's geometry, it is not a
        /// queryable Revit parameter.
        /// </summary>
        public string PaperSizeDisplay
        {
            get => _paperSizeDisplay;
            set => SetProperty(ref _paperSizeDisplay, value);
        }

        public SheetPickerRowViewModel(string sheetNumber, string sheetName, bool isSelected)
        {
            SheetNumber = sheetNumber;
            SheetName = sheetName;
            _isSelected = isSelected;
        }
    }
}
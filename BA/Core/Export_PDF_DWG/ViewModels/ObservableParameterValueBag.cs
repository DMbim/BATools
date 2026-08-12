using System;
using System.Collections.Generic;
using BA.UI.Mvvm;

namespace BA.ViewModels.Export
{
    /// <summary>
    /// Wraps dynamic parameter column values for one sheet row. WPF DataGrid
    /// columns bind to this through an indexer path, e.g.
    /// "ParameterValues[SP:xxxxxxxx]", which requires explicit
    /// PropertyChanged notification using the "Item[key]" naming
    /// convention WPF listens for on indexed properties. A plain
    /// Dictionary&lt;string,string&gt; does not raise that notification on
    /// write, columns would never refresh after population without this.
    /// </summary>
    public class ObservableParameterValueBag : BA.UI.Mvvm.ObservableObject
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);

        public string this[string columnKey]
        {
            get => _values.TryGetValue(columnKey, out var value) ? value : string.Empty;
            set
            {
                _values[columnKey] = value ?? string.Empty;
                OnPropertyChanged($"Item[{columnKey}]");
            }
        }

        public bool ContainsColumn(string columnKey) => _values.ContainsKey(columnKey);

        public void RemoveColumn(string columnKey)
        {
            if (_values.Remove(columnKey))
            {
                OnPropertyChanged($"Item[{columnKey}]");
            }
        }
    }
}

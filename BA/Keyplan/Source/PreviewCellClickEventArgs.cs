using System;

namespace BA.UI.KeyplanGrid
{
    public sealed class PreviewCellClickEventArgs : EventArgs
    {
        public string CellKey { get; }

        public PreviewCellClickEventArgs(string cellKey)
        {
            CellKey = cellKey ?? string.Empty;
        }
    }
}
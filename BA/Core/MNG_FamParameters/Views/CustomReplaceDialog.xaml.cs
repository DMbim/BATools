using Autodesk.Revit.DB;
using BA.Core;
using BA.UI;
using BA.UI.Parameters;
using Nice3point.Revit.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;

namespace BA.UI
{
    public partial class CustomReplaceDialog : Window
    {
        private readonly IList<ParameterPreview> _familyRows;
        private readonly List<SharedRow> _sharedRows;

        public (string FamilyParam, string SharedParam)? Mapping { get; private set; }

        public CustomReplaceDialog(IList<ParameterPreview> familyRows, Dictionary<string, Definition> sharedLookup)
        {
            InitializeComponent();

            _familyRows = familyRows;
            _sharedRows = sharedLookup
            .Select(kv => new SharedRow { Name = kv.Key, Spec = SafeLabel(kv.Value) })
            .OrderBy(x => x.Name)
            .ToList();

            // Ensure lists are not null
            _familyRows ??= new List<ParameterPreview>();
            _sharedRows ??= new List<SharedRow>();

            DgFamily.ItemsSource = _familyRows;
            DgShared.ItemsSource = _sharedRows; // ensure generated names match XAML field names
        }

        private static string SafeLabel(Definition d)
        {
            try { return LabelUtils.GetLabelForSpec(d.GetDataType()); }
            catch { return "<Unknown>"; }
        }

        private void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            var q = (TxtSearch.Text ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(q))
            {
                DgShared.ItemsSource = _sharedRows;
            }
            else
            {
                DgShared.ItemsSource = _sharedRows
                .Where(x => x.Name.ToLowerInvariant().Contains(q))
                .ToList();
            }
        }

        private void BtnMap_Click(object sender, RoutedEventArgs e)
        {
            var fam = DgFamily.SelectedItem as ParameterPreview;
            var sp = DgShared.SelectedItem as SharedRow;

            if (fam == null || sp == null)
            {
                MessageBox.Show("Pick one Family Parameter and one Shared Parameter.");
                return;
            }

            Mapping = (fam.Name, sp.Name);
            DialogResult = true;
        }
    }
}

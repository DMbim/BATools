using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BA.UI;
using BA.Core;
using Newtonsoft.Json;
using Nice3point.Revit.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Xml;
using MessageBox = System.Windows.MessageBox;
using Formatting = Newtonsoft.Json.Formatting;

namespace BA.UI
{
    public partial class AddSharedParamsDialog : Window
    {
        private readonly UIApplication _uiapp;
        private readonly Document _famDoc;
        private readonly FamilyManager _fm;
        private Dictionary<string, Definition> _lookup;

        // Left grid source (from SP file)
        private List<SharedRow> _sharedRows;

        // Right grid source (user’s selection to add)
        public List<SharedParamItem> SelectedItems { get; } = new();

        // Combo options (user-friendly keys; mapped to ForgeTypeId via ParseGroupKey)
        public List<string> Groups { get; } = new()
        {
            "PG_DATA",
            "PG_TEXT",
            "PG_IDENTITY_DATA",
            "PG_GEOMETRY",
            "PG_MATERIALS"
        };

        public AddSharedParamsDialog(UIApplication uiapp, Document famDoc)
        {
            InitializeComponent();


            _uiapp = uiapp ?? throw new ArgumentNullException(nameof(uiapp));
            _famDoc = famDoc ?? throw new ArgumentNullException(nameof(famDoc));
            _fm = famDoc.FamilyManager ?? throw new InvalidOperationException("FamilyManager not available.");

            // Load SP and build lookup
            BA.Core.SharedParamUtils.LoadSharedParameterFile(_uiapp.Application);
            _lookup = BA.Core.SharedParamUtils.BuildExternalDefinitionLookup();

            // Build left list (typed)
            _sharedRows = _lookup
                .Select(kv => new SharedRow { Name = kv.Key, Spec = SafeLabel(kv.Value) })
                .OrderBy(x => x.Name)
                .ToList();

            DgShared.ItemsSource = _sharedRows;

            // Bind the Group combo column items
            var comboCol = DgSelected.Columns.OfType<DataGridComboBoxColumn>().FirstOrDefault();
            if (comboCol != null)
                comboCol.ItemsSource = Groups;

            DgSelected.ItemsSource = SelectedItems;
        }

        private static string SafeLabel(Definition d)
        {
            try { return LabelUtils.GetLabelForSpec(d.GetDataType()); }
            catch { return "<Unknown>"; }
        }

        private void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            var q = (TxtSearch.Text ?? "").Trim().ToLowerInvariant();
            var rows = string.IsNullOrEmpty(q)
                ? _sharedRows
                : _sharedRows.Where(x => x.Name.ToLowerInvariant().Contains(q)).ToList();

            DgShared.ItemsSource = rows;
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            var sel = DgShared.SelectedItems.Cast<SharedRow>().ToList();
            foreach (var s in sel)
            {
                if (!SelectedItems.Any(i => i.SharedName.Equals(s.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    SelectedItems.Add(new SharedParamItem
                    {
                        SharedName = s.Name,
                        Group = "PG_DATA", // sensible default
                        IsInstance = true
                    });
                }
            }
            DgSelected.Items.Refresh();
        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            var sel = DgSelected.SelectedItems.Cast<SharedParamItem>().ToList();
            foreach (var s in sel) SelectedItems.Remove(s);
            DgSelected.Items.Refresh();
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            using (var tx = new Transaction(_famDoc, "Add Shared Parameter Set"))
            {
                tx.Start();

                foreach (var item in SelectedItems)
                {
                    if (!_lookup.TryGetValue(item.SharedName, out var def)) continue;
                    if (def is not ExternalDefinition ext) continue;

                    // Skip if already exists in family
                    if (_fm.GetParameters().Any(p => p.Definition.Name.Equals(item.SharedName, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    // Map Group key (string) -> ForgeTypeId
                    ForgeTypeId groupId = ParseGroupKey(item.Group);

                    // RVT2026 API: AddParameter(ExternalDefinition, ForgeTypeId group, bool isInstance)
                    _fm.AddParameter(ext, groupId, item.IsInstance);
                }

                tx.Commit();
            }

            MessageBox.Show("Shared parameters added.");
        }

        private void BtnLoadSet_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "JSON (*.json)|*.json" };
            if (dlg.ShowDialog() == true)
            {
                var json = File.ReadAllText(dlg.FileName);
                var set = JsonConvert.DeserializeObject<SharedParamSet>(json);

                SelectedItems.Clear();
                if (set?.Items != null)
                    SelectedItems.AddRange(set.Items);

                // Validate/migrate any legacy group values
                foreach (var it in SelectedItems)
                {
                    if (string.IsNullOrWhiteSpace(it.Group)) it.Group = "PG_DATA";
                }

                DgSelected.Items.Refresh();
            }
        }

        private void BtnSaveSet_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = "SharedSet.json" };
            if (dlg.ShowDialog() == true)
            {
                var set = new SharedParamSet
                {
                    Name = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName),
                    Items = SelectedItems.ToList()
                };
                var json = JsonConvert.SerializeObject(set, Formatting.Indented);
                File.WriteAllText(dlg.FileName, json);
                MessageBox.Show("Set saved.");
            }
        }

        // --- Helpers ---

        private static ForgeTypeId ParseGroupKey(string key)
        {
            // Accepts legacy "PG_..." tokens and maps to modern ForgeTypeId groups.
            // Extend as needed; fallback to Data.
            if (string.IsNullOrWhiteSpace(key)) return GroupTypeId.Data;

            switch (key.Trim().ToUpperInvariant())
            {
                case "PG_DATA": return GroupTypeId.Data;
                case "PG_TEXT": return GroupTypeId.Text;
                case "PG_IDENTITY_DATA": return GroupTypeId.IdentityData;
                case "PG_GEOMETRY": return GroupTypeId.Geometry;
                case "PG_MATERIALS": return GroupTypeId.Materials;
                default: return GroupTypeId.Data;
            }
        }
    }

    internal static class ListExt2
    {
        public static void AddRange<T>(this List<T> list, IEnumerable<T> items)
        {
            foreach (var i in items) list.Add(i);
        }
    }

    // FamilyManager LINQ convenience (if not already in your project)
    internal static class FamilyManagerExtensions
    {
        public static IEnumerable<FamilyParameter> GetParameters(this FamilyManager fm)
        {
            if (fm == null) yield break;
            var set = fm.Parameters;
            if (set == null) yield break;
            foreach (FamilyParameter fp in set) yield return fp;
        }
    }
}

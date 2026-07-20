// BA/UI/AddParameterDialog.xaml.cs
using Autodesk.Revit.DB;
using BA.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;

namespace BA.UI
{
    public partial class AddParameterDialog : Window, INotifyPropertyChanged
    {
        // ===================== Backing fields =====================
        private bool _isShared;
        private string _paramName = "";
        private SpecCatalogEntry _selectedSpec;
        private ParamGroupPick _selectedGroup;
        private bool _isInstance = true;
        private SharedParamEntry _selectedShared;

        // ===================== Collections bound to ComboBoxes =====================
        public IReadOnlyList<SpecCatalogEntry> Specs { get; }
        public IReadOnlyList<ParamGroupPick> Groups { get; }
        public IReadOnlyList<SharedParamEntry> SharedParams { get; }

        // ===================== Result properties (read by caller after DialogResult=true) =====================
        public bool ResultIsShared { get; private set; }
        public string ResultParamName { get; private set; }
        public ForgeTypeId ResultSpecTypeId { get; private set; }
        public ForgeTypeId ResultGroupTypeId { get; private set; }
        public bool ResultIsInstance { get; private set; }
        public ExternalDefinition ResultSharedDefinition { get; private set; }

        // ===================== Bindable properties =====================

        public bool IsShared
        {
            get => _isShared;
            set
            {
                _isShared = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsSharedVisible));
                OnPropertyChanged(nameof(IsNonSharedVisible));
            }
        }

        public System.Windows.Visibility IsSharedVisible => _isShared ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        public System.Windows.Visibility IsNonSharedVisible => !_isShared ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        public string ParamName
        {
            get => _paramName;
            set { _paramName = value ?? ""; OnPropertyChanged(); }
        }

        public SpecCatalogEntry SelectedSpec
        {
            get => _selectedSpec;
            set { _selectedSpec = value; OnPropertyChanged(); }
        }

        public ParamGroupPick SelectedGroup
        {
            get => _selectedGroup;
            set { _selectedGroup = value; OnPropertyChanged(); }
        }

        public bool IsInstance
        {
            get => _isInstance;
            set { _isInstance = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsType)); }
        }

        public bool IsType
        {
            get => !_isInstance;
            set { _isInstance = !value; OnPropertyChanged(); OnPropertyChanged(nameof(IsInstance)); }
        }

        public SharedParamEntry SelectedShared
        {
            get => _selectedShared;
            set { _selectedShared = value; OnPropertyChanged(); }
        }

        // ===================== Constructor =====================

        /// <param name="sharedParamLookup">
        /// Pre-built lookup from SharedParamUtils.BuildExternalDefinitionLookup().
        /// Pass an empty dictionary when no shared param file is configured.
        /// </param>
        public AddParameterDialog(Dictionary<string, Definition> sharedParamLookup)
        {
            DataContext = this;
            InitializeComponent();

            Specs = SpecCatalog.GetAvailable();
            Groups = ParamGroupCatalog.GetAvailable();

            SelectedSpec = Specs.FirstOrDefault(
                               s => s.SpecTypeId?.Equals(SpecTypeId.Boolean.YesNo) == true)
                        ?? Specs.FirstOrDefault();

            SelectedGroup = Groups.FirstOrDefault(
                                g => g.GroupId?.Equals(GroupTypeId.Data) == true)
                         ?? Groups.FirstOrDefault();

            var sharedList = new List<SharedParamEntry>();
            if (sharedParamLookup != null)
            {
                foreach (var kvp in sharedParamLookup)
                {
                    if (kvp.Value is ExternalDefinition ext)
                        sharedList.Add(new SharedParamEntry(ext));
                }
            }

            SharedParams = sharedList
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            SelectedShared = SharedParams.FirstOrDefault();
        }

        // ===================== Button handlers =====================

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            if (_isShared)
            {
                if (SelectedShared == null)
                {
                    MessageBox.Show(
                        "Select a shared parameter from the list.\n\n" +
                        "If the list is empty, set the shared parameter file path " +
                        "in the main dialog and try again.",
                        "Add Parameter", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                ResultIsShared = true;
                ResultParamName = SelectedShared.Name;
                ResultSharedDefinition = SelectedShared.Definition;
                ResultSpecTypeId = null;
            }
            else
            {
                var name = (_paramName ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    MessageBox.Show(
                        "Enter a parameter name.",
                        "Add Parameter", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (SelectedSpec == null)
                {
                    MessageBox.Show(
                        "Select a spec type.",
                        "Add Parameter", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                ResultIsShared = false;
                ResultParamName = name;
                ResultSpecTypeId = SelectedSpec.SpecTypeId;
                ResultSharedDefinition = null;
            }

            ResultGroupTypeId = SelectedGroup?.GroupId ?? GroupTypeId.Data;
            ResultIsInstance = _isInstance;

            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;

        // ===================== INotifyPropertyChanged =====================

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string prop = "")
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    // ===================== Shared param entry wrapper =====================

    public sealed class SharedParamEntry
    {
        public string Name { get; }
        public ExternalDefinition Definition { get; }

        public SharedParamEntry(ExternalDefinition def)
        {
            Definition = def ?? throw new ArgumentNullException(nameof(def));
            Name = def.Name;
        }

        public override string ToString() => Name;
    }
}
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using RC = CommunityToolkit.Mvvm.Input.RelayCommand;
using System.Windows.Input;

namespace BA
{
    public class SyncViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        private readonly Document _doc;
        private readonly Dictionary<string, List<ScheduleMappingRow>> _store;

        public ObservableCollection<ViewSchedule> Schedules { get; }
        public ObservableCollection<string> Columns { get; }
        public ObservableCollection<string> Parameters { get; }
        public ObservableCollection<ScheduleMappingRow> Mappings { get; }

        public ICommand AddCommand { get; }
        public ICommand RemoveCommand { get; }
        public ICommand SyncCommand { get; }

        public ScheduleMappingRow SelectedRow { get; set; }

        private ViewSchedule _selectedSchedule;
        public ViewSchedule SelectedSchedule
        {
            get => _selectedSchedule;
            set
            {
                SaveCurrent();
                _selectedSchedule = value;
                LoadColumnsForSchedule();
                LoadParametersForSchedule();
                LoadMappingsForSchedule();
            }
        }

        private readonly SyncExternalEventHandler _handler;
        private readonly Autodesk.Revit.UI.ExternalEvent _exEvent;
        private readonly System.Windows.Threading.Dispatcher _dispatcher;

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
        }
        private string _statusText = "";

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        public SyncViewModel(ExternalCommandData data)
        {
            _doc = data.Application.ActiveUIDocument.Document;
            _store = ScheduleSyncStore.Load();
            _dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;

            _handler = new SyncExternalEventHandler();
            _exEvent = Autodesk.Revit.UI.ExternalEvent.Create(_handler);

            Schedules = new ObservableCollection<ViewSchedule>(
                new FilteredElementCollector(_doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .OrderBy(s => s.Name));

            Columns = new ObservableCollection<string>();
            Parameters = new ObservableCollection<string>();
            Mappings = new ObservableCollection<ScheduleMappingRow>();

            Mappings.CollectionChanged += OnMappingsChanged;

            AddCommand = new RC(() => Mappings.Add(new ScheduleMappingRow()));
            RemoveCommand = new RC(() => { if (SelectedRow != null) Mappings.Remove(SelectedRow); });
            SyncCommand = new RC(() => RunSync());
        }

        private void LoadColumnsForSchedule()
        {
            Columns.Clear();
            if (_selectedSchedule == null) return;

            var def = _selectedSchedule.Definition;
            for (int i = 0; i < def.GetFieldCount(); i++)
                Columns.Add(def.GetField(i).GetName());
        }

        private void LoadParametersForSchedule()
        {
            Parameters.Clear();
            if (_selectedSchedule == null) return;

            var def = _selectedSchedule.Definition;

            // Get a sample element so we can check read-only state per parameter.
            Element sampleEl = new FilteredElementCollector(_doc)
                .WhereElementIsNotElementType()
                .WherePasses(new ElementCategoryFilter(def.CategoryId))
                .FirstOrDefault();

            for (int i = 0; i < def.GetFieldCount(); i++)
            {
                ScheduleField field = def.GetField(i);

                // Calculated fields and grand-total fields have no backing parameter.
                if (field.IsCalculatedField) continue;
                if (field.ParameterId == ElementId.InvalidElementId) continue;

                // Bonus: skip parameters that are read-only on a real element of this category.
                if (sampleEl != null)
                {
                    Parameter p = sampleEl.Parameters.Cast<Parameter>()
                                           .FirstOrDefault(x => x.Id == field.ParameterId);
                    if (p != null && p.IsReadOnly) continue;
                }

                Parameters.Add(field.GetName());
            }
        }

        private void LoadMappingsForSchedule()
        {
            Mappings.CollectionChanged -= OnMappingsChanged;
            Mappings.Clear();

            if (_selectedSchedule != null
                && _store.TryGetValue(_selectedSchedule.Name, out var saved))
            {
                foreach (var row in saved)
                    Mappings.Add(row);
            }

            Mappings.CollectionChanged += OnMappingsChanged;
        }

        private void SaveCurrent()
        {
            if (_selectedSchedule == null) return;
            _store[_selectedSchedule.Name] = Mappings.ToList();
            ScheduleSyncStore.Save(_store);
        }

        private void OnMappingsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            SaveCurrent();
        }

        private void RunSync()
        {
            if (_selectedSchedule == null)
            {
                StatusText = "No schedule selected.";
                return;
            }

            SaveCurrent();
            StatusText = "Syncing...";

            var req = new SyncRequest(
                _selectedSchedule,
                Mappings.ToList(),
                result => _dispatcher.Invoke(() => StatusText = result));

            _handler.SetRequest(req);
            _exEvent.Raise();
        }

    }
}

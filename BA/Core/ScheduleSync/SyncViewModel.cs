using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Input;

namespace BA
{
    public class SyncViewModel
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

        public SyncViewModel(ExternalCommandData data)
        {
            _doc = data.Application.ActiveUIDocument.Document;
            _store = ScheduleSyncStore.Load();

            Schedules = new ObservableCollection<ViewSchedule>(
                new FilteredElementCollector(_doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .OrderBy(s => s.Name));

            Columns = new ObservableCollection<string>();
            Parameters = new ObservableCollection<string>();
            Mappings = new ObservableCollection<ScheduleMappingRow>();

            Mappings.CollectionChanged += OnMappingsChanged;

            AddCommand = new RelayCommand(_ => Mappings.Add(new ScheduleMappingRow()));
            RemoveCommand = new RelayCommand(_ => { if (SelectedRow != null) Mappings.Remove(SelectedRow); });
            SyncCommand = new RelayCommand(_ => RunSync());
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
            if (_selectedSchedule == null) return;
            SaveCurrent();
            ScheduleSyncEngine.Execute(_doc, _selectedSchedule, Mappings.ToList());
        }

    }
}

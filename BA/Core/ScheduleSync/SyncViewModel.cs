using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace BA
{
    public class SyncViewModel
    {
        private Document _doc;

        public ObservableCollection<ViewSchedule> Schedules { get; set; }
        public ObservableCollection<string> Columns { get; set; }
        public ObservableCollection<string> Parameters { get; set; }

        public ObservableCollection<ScheduleMappingRow> Mappings { get; set; }

        public ICommand AddCommand { get; }
        public ICommand RemoveCommand { get; }
        public ICommand SyncCommand { get; }
        public ICommand LoadColumnsCommand { get; }

        public ScheduleMappingRow SelectedRow { get; set; }

        public SyncViewModel(ExternalCommandData data)
        {
            _doc = data.Application.ActiveUIDocument.Document;

            Schedules = new ObservableCollection<ViewSchedule>(
                new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>());

            Columns = new ObservableCollection<string>();
            Parameters = new ObservableCollection<string>();

            Mappings = new ObservableCollection<ScheduleMappingRow>();

            AddCommand = new RelayCommand(Add);
            RemoveCommand = new RelayCommand(Remove);
            SyncCommand = new RelayCommand(Sync);
            LoadColumnsCommand = new RelayCommand(LoadColumns);

            LoadParameters();
        }

        private void Add(object obj)
        {
            Mappings.Add(new ScheduleMappingRow());
        }

        private void Remove(object obj)
        {
            if (SelectedRow != null)
                Mappings.Remove(SelectedRow);
        }

        private void Sync(object obj)
        {
            ScheduleSyncEngine.Execute(_doc, Mappings.ToList());
        }

        private void LoadColumns(object obj)
        {
            Columns.Clear();

            if (SelectedRow?.Schedule == null) return;

            var def = SelectedRow.Schedule.Definition;

            for (int i = 0; i < def.GetFieldCount(); i++)
                Columns.Add(def.GetField(i).GetName());
        }

        private void LoadParameters()
        {
            var el = new FilteredElementCollector(_doc)
                .WhereElementIsNotElementType()
                .FirstOrDefault();

            if (el == null) return;

            foreach (Parameter p in el.Parameters)
                Parameters.Add(p.Definition.Name);
        }
    }
}

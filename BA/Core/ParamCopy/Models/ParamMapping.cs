using CommunityToolkit.Mvvm.ComponentModel;

namespace BATools.ParamCopy.Models
{
    public class ParamMapping : ObservableObject
    {
        private string _sourceParameterName = string.Empty;
        private string _destParameterName = string.Empty;
        private bool _writeOnlyIfEmpty;

        public string SourceParameterName
        {
            get => _sourceParameterName;
            set => SetProperty(ref _sourceParameterName, value);
        }

        public string DestParameterName
        {
            get => _destParameterName;
            set => SetProperty(ref _destParameterName, value);
        }

        public bool WriteOnlyIfEmpty
        {
            get => _writeOnlyIfEmpty;
            set => SetProperty(ref _writeOnlyIfEmpty, value);
        }
    }
}

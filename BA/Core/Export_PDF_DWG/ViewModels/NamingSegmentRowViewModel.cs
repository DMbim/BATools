using BA.UI.Mvvm;

namespace BA.ViewModels.Export
{
    public enum NamingSegmentKind
    {
        Parameter,
        Literal
    }

    /// <summary>
    /// One row in the naming template builder: either a real or pseudo
    /// parameter reference (SheetNumber, Date, Revision, or any arbitrary
    /// parameter name fetched from the sample sheet) or a literal text
    /// segment. DisplayText mirrors exactly the {Token} / {Token:format}
    /// syntax NamingTemplateEngine parses, this is the same string, just
    /// edited through rows instead of typed by hand.
    /// </summary>
    public class NamingSegmentRowViewModel : BA.UI.Mvvm.ObservableObject
    {
        public NamingSegmentKind Kind { get; }

        private string _parameterName;
        public string ParameterName
        {
            get => _parameterName;
            set
            {
                if (SetProperty(ref _parameterName, value))
                {
                    OnPropertyChanged(nameof(DisplayText));
                }
            }
        }

        private string _formatOverride;
        public string FormatOverride
        {
            get => _formatOverride;
            set
            {
                if (SetProperty(ref _formatOverride, value))
                {
                    OnPropertyChanged(nameof(DisplayText));
                }
            }
        }

        private string _literalText;
        public string LiteralText
        {
            get => _literalText;
            set
            {
                if (SetProperty(ref _literalText, value))
                {
                    OnPropertyChanged(nameof(DisplayText));
                }
            }
        }

        public string DisplayText => Kind == NamingSegmentKind.Parameter
            ? (string.IsNullOrEmpty(FormatOverride) ? $"{{{ParameterName}}}" : $"{{{ParameterName}:{FormatOverride}}}")
            : (LiteralText ?? string.Empty);

        private NamingSegmentRowViewModel(NamingSegmentKind kind)
        {
            Kind = kind;
        }

        public static NamingSegmentRowViewModel CreateParameter(string parameterName, string formatOverride = null)
            => new NamingSegmentRowViewModel(NamingSegmentKind.Parameter) { ParameterName = parameterName, FormatOverride = formatOverride };

        public static NamingSegmentRowViewModel CreateLiteral(string literalText)
            => new NamingSegmentRowViewModel(NamingSegmentKind.Literal) { LiteralText = literalText };
    }
}

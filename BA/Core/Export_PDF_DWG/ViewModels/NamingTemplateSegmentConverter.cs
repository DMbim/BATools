using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;

namespace BA.ViewModels.Export
{
    /// <summary>
    /// Converts between the flat {Token:format} template string
    /// NamingTemplateEngine consumes and the ordered row list the builder
    /// dialog edits. Uses the same token pattern NamingTemplateEngine uses,
    /// kept as an independent Regex instance rather than shared, one parses
    /// (Matches) and the other replaces (Replace), different enough
    /// operations that sharing wasn't worth coupling the two classes
    /// together across the Core/ViewModels boundary.
    /// </summary>
    public static class NamingTemplateSegmentConverter
    {
        private static readonly Regex TokenPattern =
            new Regex(@"\{([A-Za-z0-9_]+)(?::([^{}]+))?\}", RegexOptions.Compiled);

        public static ObservableCollection<NamingSegmentRowViewModel> Parse(string template)
        {
            var rows = new ObservableCollection<NamingSegmentRowViewModel>();

            if (string.IsNullOrEmpty(template))
            {
                return rows;
            }

            var lastIndex = 0;

            foreach (Match m in TokenPattern.Matches(template))
            {
                if (m.Index > lastIndex)
                {
                    rows.Add(NamingSegmentRowViewModel.CreateLiteral(template.Substring(lastIndex, m.Index - lastIndex)));
                }

                rows.Add(NamingSegmentRowViewModel.CreateParameter(
                    m.Groups[1].Value,
                    m.Groups[2].Success ? m.Groups[2].Value : null));

                lastIndex = m.Index + m.Length;
            }

            if (lastIndex < template.Length)
            {
                rows.Add(NamingSegmentRowViewModel.CreateLiteral(template.Substring(lastIndex)));
            }

            return rows;
        }

        public static string Build(IEnumerable<NamingSegmentRowViewModel> rows)
        {
            var sb = new StringBuilder();

            foreach (var row in rows)
            {
                if (row.Kind == NamingSegmentKind.Literal)
                {
                    sb.Append(row.LiteralText ?? string.Empty);
                }
                else
                {
                    sb.Append('{').Append(row.ParameterName);

                    if (!string.IsNullOrEmpty(row.FormatOverride))
                    {
                        sb.Append(':').Append(row.FormatOverride);
                    }

                    sb.Append('}');
                }
            }

            return sb.ToString();
        }
    }
}

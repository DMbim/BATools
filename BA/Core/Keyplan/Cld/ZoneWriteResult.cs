using System.Collections.Generic;

namespace BA.UI.KeyplanGrid
{
    public sealed class ZoneWriteResult
    {
        public int Written { get; set; }
        public int Skipped { get; set; }

        public List<string> MissingParameters { get; } = new List<string>();
        public List<string> ReadOnlyParameters { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();

        public bool HasWarnings =>
            MissingParameters.Count > 0 ||
            ReadOnlyParameters.Count > 0 ||
            Errors.Count > 0;

        public string Summary =>
            $"Written: {Written}, Skipped: {Skipped}" +
            (MissingParameters.Count > 0 ? $", Missing params: {MissingParameters.Count}" : "") +
            (ReadOnlyParameters.Count > 0 ? $", Read-only: {ReadOnlyParameters.Count}" : "") +
            (Errors.Count > 0 ? $", Errors: {Errors.Count}" : "");
    }
}

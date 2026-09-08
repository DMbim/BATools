using System.Collections.Generic;
using System.Text;

namespace BA.RoomClassification.Models
{
    internal sealed class RoomClassificationValidationResult
    {
        public List<string> Errors { get; } = new List<string>();
        public bool IsValid => Errors.Count == 0;

        public string BuildMessage()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Excel validation failed.");
            sb.AppendLine();
            foreach (string error in Errors)
                sb.AppendLine("• " + error);
            return sb.ToString();
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BA.RoomClassification.Models
{
    internal sealed class RoomClassificationSyncResult
    {
        public int Updated { get; set; }
        public int Created { get; set; }
        public int ExistingExtraRows { get; set; }
        public List<string> ExtraCodes { get; } = new List<string>();
        public List<string> UnresolvedFinishRoomCodes { get; } = new List<string>();

        public string BuildMessage()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Room classification sync completed.");
            sb.AppendLine();
            sb.AppendLine($"Created: {Created}");
            sb.AppendLine($"Updated: {Updated}");
            sb.AppendLine($"Extra rows already in Revit (not deleted): {ExistingExtraRows}");

            if (ExtraCodes.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Extra BA.Tls_RoomCode values found only in Revit:");
                foreach (string code in ExtraCodes.OrderBy(x => x))
                    sb.AppendLine("• " + code);
            }

            if (UnresolvedFinishRoomCodes.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Rooms imported with at least one blank finish field, no matching " +
                               $"FinishPresets entry or an incomplete preset ({UnresolvedFinishRoomCodes.Count}):");
                foreach (string code in UnresolvedFinishRoomCodes.OrderBy(x => x))
                    sb.AppendLine("• " + code);
            }

            return sb.ToString();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BA.Core.Ledger
{
    /// <summary>
    /// Root ledger document persisted to Data_Ledger.json.
    /// Families dictionary key = "{FamilyName}::{TypeName}" (see LedgerFileService.BuildKey).
    /// </summary>
    public class TypeDataLedger
    {
        [JsonPropertyName("Version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("Families")]
        public Dictionary<string, LedgerFamilyNode> Families { get; set; }
            = new Dictionary<string, LedgerFamilyNode>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// System.Text.Json.Deserialize does NOT preserve the OrdinalIgnoreCase comparer set in
        /// the field initializer above -- it constructs a plain case-sensitive Dictionary when
        /// populating the property from JSON. Without calling this after every deserialize,
        /// family/type key matching silently becomes case-sensitive, which will eventually
        /// cause a real key to fail to match with no error anywhere. Call this immediately
        /// after every JsonSerializer.Deserialize&lt;TypeDataLedger&gt; call, in every service
        /// that reads this file (LedgerFileService, PersonalLedgerService).
        /// </summary>
        public void NormalizeComparers()
        {
            Families = new Dictionary<string, LedgerFamilyNode>(Families, StringComparer.OrdinalIgnoreCase);

            foreach (LedgerFamilyNode node in Families.Values)
            {
                node.Parameters = new Dictionary<string, LedgerParameterEntry>(node.Parameters, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public class LedgerFamilyNode
    {
        /// <summary>
        /// Key = shared parameter GUID as string ("D" format, e.g. "2c9a4e1b-3f3b-4c7f-9c0e-6d2c1b7e9f41").
        /// Matching is always done by GUID, never by parameter name.
        /// </summary>
        [JsonPropertyName("Parameters")]
        public Dictionary<string, LedgerParameterEntry> Parameters { get; set; }
            = new Dictionary<string, LedgerParameterEntry>(StringComparer.OrdinalIgnoreCase);
    }

    public class LedgerParameterEntry
    {
        /// <summary>
        /// Human-readable only. Never used for matching, since parameter display names
        /// are not guaranteed identical across the 5 projects even for a shared GUID.
        /// </summary>
        [JsonPropertyName("ParameterName")]
        public string ParameterName { get; set; } = string.Empty;

        [JsonPropertyName("Value")]
        public string Value { get; set; } = string.Empty;

        /// <summary>
        /// "String" | "Integer" | "Double" only. ElementId is never written here by design;
        /// ElementId values are document-local and cannot be meaningfully round-tripped
        /// across 5 independent models.
        /// </summary>
        [JsonPropertyName("StorageType")]
        public string StorageType { get; set; } = string.Empty;

        [JsonPropertyName("TimestampUtc")]
        public DateTime TimestampUtc { get; set; }

        [JsonPropertyName("LastEditedBy")]
        public string LastEditedBy { get; set; } = string.Empty;
    }
}

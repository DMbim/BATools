// File: BA_Tools/CadPurge/Services/Json/CaseInsensitiveEnumJsonConverter.cs
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BA.CadPurge.Services.Json
{
    /// <summary>
    /// Deserializes/serializes any enum as its string name, matching case-insensitively on read
    /// via Enum.TryParse(ignoreCase: true) — documented, version-stable .NET behavior. Used instead
    /// of the built-in JsonStringEnumConverter so corporate_standards.json, which is hand-edited by
    /// a BIM manager rather than a developer, tolerates casing mistakes like "linepattern" instead
    /// of failing deserialization outright.
    /// </summary>
    public sealed class CaseInsensitiveEnumJsonConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
    {
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException(
                    $"Expected a string value for enum '{typeToConvert.Name}' but found {reader.TokenType}.");

            string raw = reader.GetString();

            if (!Enum.TryParse(raw, ignoreCase: true, out TEnum result) || !Enum.IsDefined(typeof(TEnum), result))
            {
                string validValues = string.Join(", ", Enum.GetNames(typeof(TEnum)));
                throw new JsonException(
                    $"'{raw}' is not a valid {typeToConvert.Name}. Valid values are: {validValues}.");
            }

            return result;
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
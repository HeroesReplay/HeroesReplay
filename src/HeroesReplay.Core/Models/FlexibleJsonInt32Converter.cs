using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HeroesReplay.Core.Models;

/// <summary>
/// Heroes Profile docs still show region as "US"; live Min_id returns 1/2/3/5.
/// </summary>
public sealed class FlexibleJsonInt32Converter : JsonConverter<int?>
{
    public override int? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.Number:
                return reader.TryGetInt32(out int n) ? n : (int)reader.GetInt64();
            case JsonTokenType.String:
                string s = reader.GetString();
                if (string.IsNullOrWhiteSpace(s))
                {
                    return null;
                }

                if (
                    int.TryParse(
                        s,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int parsed
                    )
                )
                {
                    return parsed;
                }

                return s.Trim().ToUpperInvariant() switch
                {
                    "NA" or "US" or "AMERICAS" => 1,
                    "EU" or "EUROPE" => 2,
                    "KR" or "KOREA" or "ASIA" => 3,
                    "CN" or "CHINA" => 5,
                    _ => null,
                };
            default:
                throw new JsonException($"Cannot convert {reader.TokenType} to int.");
        }
    }

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteNumberValue(value.Value);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}

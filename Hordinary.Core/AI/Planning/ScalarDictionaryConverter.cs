using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hordinary.AI.Planning;

/// <summary>
/// Lit un dictionnaire de parametres dont les valeurs sont ecrites au naturel en JSON —
/// <c>{ "condition": "door_open(?door)", "value": true, "amount": 5 }</c> — et les ramene toutes a
/// des chaines. Les parametres de verbe sont interpretes par le verbe lui-meme, pas par le
/// desserialiseur : lui imposer un type par cle obligerait a une classe par verbe, exactement ce que
/// la specification demande d'eviter.
/// </summary>
public sealed class ScalarDictionaryConverter : JsonConverter<Dictionary<string, string>>
{
    public override Dictionary<string, string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Un bloc de parametres doit etre un objet JSON.");

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return values;
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Cle de parametre attendue.");

            string key = reader.GetString()!;
            reader.Read();

            values[key] = reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString()!,
                JsonTokenType.True => "true",
                JsonTokenType.False => "false",
                JsonTokenType.Number => reader.GetDouble().ToString(CultureInfo.InvariantCulture),
                JsonTokenType.Null => string.Empty,
                _ => throw new JsonException($"Valeur de parametre non scalaire pour « {key} »."),
            };
        }

        throw new JsonException("Bloc de parametres non termine.");
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, string> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var pair in value) writer.WriteString(pair.Key, pair.Value);
        writer.WriteEndObject();
    }
}

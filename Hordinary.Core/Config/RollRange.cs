using System.Text.Json;

namespace Hordinary.Config;

/// <summary>
/// Un intervalle de tirage, ecrit <c>[2, 6]</c> en configuration. Deux bornes incluses ; une seule
/// valeur ecrite <c>[3, 3]</c> ne tire rien.
///
/// Il vit dans <c>Config</c> et non dans <c>Game</c> parce que deux fichiers s'en servent — la table
/// de butin d'une zone et celle d'une expedition — et qu'une primitive de configuration partagee par
/// deux domaines n'appartient a aucun des deux.
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(RollRangeConverter))]
public sealed record RollRange(float Min, float Max)
{
    public float Roll(Func<float> next) => Min >= Max ? Min : Min + next() * (Max - Min);

    public override string ToString() => Min >= Max ? $"{Min:0.#}" : $"{Min:0.#}-{Max:0.#}";
}

/// <summary>
/// Lit <c>[2, 6]</c>, ou <c>4</c> pour un intervalle degenere. Le tableau plutot que
/// <c>{ "min": 2, "max": 6 }</c> parce qu'une table de butin en aligne des dizaines, et qu'a cette
/// densite la forme courte est ce qui rend le fichier lisible.
/// </summary>
public sealed class RollRangeConverter : System.Text.Json.Serialization.JsonConverter<RollRange>
{
    public override RollRange Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            float single = reader.GetSingle();
            return new RollRange(single, single);
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Un intervalle s'ecrit [min, max] ou par un nombre seul.");
        }

        var bounds = new List<float>();

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            bounds.Add(reader.GetSingle());
        }

        return bounds.Count switch
        {
            1 => new RollRange(bounds[0], bounds[0]),
            2 => new RollRange(bounds[0], bounds[1]),
            _ => throw new JsonException($"Un intervalle compte une ou deux bornes, pas {bounds.Count}."),
        };
    }

    public override void Write(Utf8JsonWriter writer, RollRange value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.Min);
        writer.WriteNumberValue(value.Max);
        writer.WriteEndArray();
    }
}

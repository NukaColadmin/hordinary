using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hordinary.World;

/// <summary>
/// Un lieu vise, ecrit d'une seule facon en configuration et lu de deux : <c>"Pharmacy"</c> designe
/// une categorie structurelle de <see cref="ZoneKind"/>, <c>"station_service"</c> un profil de
/// <c>config/zones.json</c>.
///
/// Les deux vocabulaires coexistent parce qu'ils ne disent pas la meme chose. <see cref="ZoneKind"/>
/// est ce que le planificateur connait — <c>at(agent, pharmacy)</c> est ecrit dans le domaine, et on
/// ne casse pas les livrables 5 a 8 pour du rangement. Le profil est ce qu'une zone <b>contient</b>,
/// et il ne coute aucune recompilation. « Rapporter de l'essence » devient une mission en une ligne.
/// </summary>
[JsonConverter(typeof(ZoneTargetConverter))]
public sealed record ZoneTarget(ZoneKind Kind, string? Profile = null)
{
    public bool ByProfile => Profile is { Length: > 0 };

    public static implicit operator ZoneTarget(ZoneKind kind) => new(kind);

    public override string ToString() => Profile ?? Kind.ToString();
}

/// <summary>
/// Lit une chaine unique. Si elle nomme un <see cref="ZoneKind"/>, c'est un type ; sinon c'est un
/// identifiant de profil, et sa categorie structurelle sera renseignee au chargement du catalogue de
/// missions — un convertisseur JSON n'a pas a connaitre un second fichier.
/// </summary>
public sealed class ZoneTargetConverter : JsonConverter<ZoneTarget>
{
    public override ZoneTarget Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        string value = reader.GetString() ?? string.Empty;

        return Enum.TryParse<ZoneKind>(value, ignoreCase: true, out var kind)
            ? new ZoneTarget(kind)
            : new ZoneTarget(ZoneKind.Other, value);
    }

    public override void Write(Utf8JsonWriter writer, ZoneTarget value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}

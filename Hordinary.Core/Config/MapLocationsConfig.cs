using System.Text.Json;
using Hordinary.World;

namespace Hordinary.Config;

/// <summary>
/// Lieux nommes de la carte (specification section 7.3), declares en coordonnees de tuiles
/// — les memes que celles affichees par l'editeur Godot.
///
/// Les tuiles peintes decrivent la geometrie (murs, portes, sols) ; ce fichier decrit la semantique
/// (ou est le depart, ou est la pharmacie...) sans exiger de tuiles supplementaires dans l'atlas.
/// </summary>
public sealed class MapLocationsConfig
{
    public List<ZoneDefinition> Zones { get; init; } = [];

    /// <summary>
    /// Si vrai, les cellules praticables des zones <c>pharmacy</c> et <c>extraction</c> prennent
    /// respectivement le type <see cref="CellType.Pharmacy"/> et <see cref="CellType.Extraction"/>.
    /// </summary>
    public bool StampCellTypes { get; init; } = true;

    public static MapLocationsConfig Empty { get; } = new();

    public static MapLocationsConfig FromJson(string json)
        => JsonSerializer.Deserialize<MapLocationsConfig>(json, GameConfig.JsonOptions) ?? Empty;

    /// <summary>Enregistre les zones dans la carte et applique le marquage des types de cellules.</summary>
    public void ApplyTo(GridMap map)
    {
        foreach (var definition in Zones)
        {
            var zone = definition.ToZone();
            map.AddZone(zone);

            if (!StampCellTypes) continue;

            CellType? stamp = zone.Kind switch
            {
                ZoneKind.Pharmacy => CellType.Pharmacy,
                ZoneKind.Extraction => CellType.Extraction,
                _ => null,
            };
            if (stamp is null) continue;

            foreach (var position in zone.Positions())
            {
                // On ne marque que le sol : murs, portes et fenetres gardent leur type.
                if (map.GetCell(position) == CellType.Floor) map.SetCell(position, stamp.Value);
            }
        }
    }
}

public sealed class ZoneDefinition
{
    public string Name { get; init; } = string.Empty;

    public ZoneKind Kind { get; init; } = ZoneKind.Other;

    /// <summary>Coin superieur gauche, inclusif : <c>[x, y]</c>.</summary>
    public int[] Min { get; init; } = [0, 0];

    /// <summary>Coin inferieur droit, inclusif : <c>[x, y]</c>.</summary>
    public int[] Max { get; init; } = [0, 0];

    public MapZone ToZone()
    {
        if (Min.Length < 2 || Max.Length < 2)
            throw new InvalidOperationException($"Zone '{Name}' : min et max doivent contenir deux entiers [x, y].");

        return new MapZone(Name, Kind, new GridPosition(Min[0], Min[1]), new GridPosition(Max[0], Max[1]));
    }
}

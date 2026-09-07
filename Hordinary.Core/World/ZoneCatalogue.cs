using System.Text.Json;
using Hordinary.Config;

namespace Hordinary.World;

/// <summary>
/// Les quatre agencements primitifs. C'est le seul champ d'un profil de zone qui demande du code :
/// tout le reste est de la donnee.
///
/// Cinq zones, quatre agencements — c'est la ou se trouve le rendement, et pas dans les tables de
/// butin. Une zone qui ne fait que changer ce qu'on ramasse est une ligne de configuration deguisee
/// en contenu : elle coute des tuiles et ne produit aucune decision.
/// </summary>
public enum ZoneLayout
{
    /// <summary>Volume libre : longues lignes de vue, aucun couvert. Le gymnase, le hall.</summary>
    Open = 0,

    /// <summary>Allees paralleles : la vue porte <b>le long</b> d'une allee, jamais <b>a travers</b>.</summary>
    Aisles,

    /// <summary>Couloirs et petites salles : on ne voit rien, et il faut fouiller partout.</summary>
    Cells,

    /// <summary>Esplanade et cabane : tout est a decouvert sauf un petit interieur.</summary>
    Forecourt,
}

/// <summary>Comment on entre, et ce qui reste ferme une fois qu'on est entre.</summary>
public sealed record ZoneAccess
{
    /// <summary>Nombre de portes d'entree a percer dans le mur d'enceinte.</summary>
    public RollRange EntryDoors { get; init; } = new(1f, 1f);

    /// <summary>Probabilite qu'une porte d'entree soit verrouillee. 1 : elle l'est toujours.</summary>
    public float LockedChance { get; init; }

    /// <summary>
    /// La piece du fond, quand il y en a une : la reserve du supermarche, l'armurerie du poste. Elle a
    /// son propre verrouillage, et c'est ce qui met le seul <c>ForceDoor</c> de la zone a l'endroit le
    /// plus profond, au bruit maximal, avec le plus long chemin de retour.
    /// </summary>
    public ZoneAccess? InnerRoom { get; init; }
}

/// <summary>
/// Un profil de zone, charge depuis <c>config/zones.json</c>. Une zone est une donnee, comme une
/// mission ou un survivant.
///
/// <see cref="ZoneKind"/> reste ce qu'il est — la categorie <b>structurelle</b>, celle que la
/// navigation et le planificateur connaissent — et le profil est ce qui s'y ajoute : y ajouter cinq
/// valeurs d'enumeration serait cinq recompilations, contre toute la culture du projet.
/// </summary>
public sealed record ZoneProfile
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>La categorie structurelle. C'est elle, et non le profil, que le planificateur nomme.</summary>
    public ZoneKind Kind { get; init; } = ZoneKind.Building;

    public string? Description { get; init; }

    /// <summary>Empreinte conseillee, <c>[[x1, y1], [x2, y2]]</c>. Le generateur de cartes s'en sert.</summary>
    public int[][] Footprint { get; init; } = [];

    public ZoneLayout Layout { get; init; } = ZoneLayout.Open;

    /// <summary>Espacement des allees, quand l'agencement en a. Trois : une allee, deux cases, une allee.</summary>
    public int AisleSpacing { get; init; } = 3;

    public ZoneAccess Access { get; init; } = new();

    /// <summary>De 0 a 1. Densite de zombies relative a celle du reste de la carte.</summary>
    public float ZombieDensity { get; init; } = 0.5f;

    /// <summary>
    /// Ce que la zone contient, par identifiant du catalogue d'objets. C'est le rendement de la zone,
    /// et non ce qui la definit : deux zones peuvent tres bien partager une table.
    /// </summary>
    public Dictionary<string, RollRange> Loot { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Nombre de fouilles a mener pour tout trouver. Deux et plus : il faut chercher partout.</summary>
    public RollRange SearchRooms { get; init; } = new(1f, 1f);

    public MapZone? ZoneIn(GridMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return map.Zones.FirstOrDefault(zone => Id.Equals(zone.Profile, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>L'empreinte conseillee, si elle est ecrite et bien formee.</summary>
    public (GridPosition Min, GridPosition Max)? Rectangle()
    {
        if (Footprint.Length != 2 || Footprint[0].Length < 2 || Footprint[1].Length < 2) return null;

        return (new GridPosition(Footprint[0][0], Footprint[0][1]),
                new GridPosition(Footprint[1][0], Footprint[1][1]));
    }

    public override string ToString() => $"{Name} [{Id}] ({Layout}, danger {ZombieDensity:P0})";
}

/// <summary>
/// Les cinq zones livrees, chargees depuis <c>config/zones.json</c>.
///
/// Comme pour les missions et les profils de survivants, le fichier est valide des le chargement : un
/// agencement inconnu ou une table de butin qui nomme un objet inexistant doivent se voir tout de
/// suite, et non produire une zone qu'on ne peut pas fouiller.
/// </summary>
public sealed class ZoneCatalogue
{
    public List<ZoneProfile> Zones { get; init; } = [];

    public static ZoneCatalogue Empty { get; } = new();

    public ZoneProfile? Find(string? id)
        => id is null
            ? null
            : Zones.FirstOrDefault(zone => zone.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public bool Declares(string? id) => Find(id) is not null;

    public static ZoneCatalogue FromJson(string json)
    {
        var catalogue = JsonSerializer.Deserialize<ZoneCatalogue>(json, GameConfig.JsonOptions) ?? Empty;
        catalogue.Validate();
        return catalogue;
    }

    /// <summary>
    /// Verifie ce qui peut l'etre sans le catalogue d'objets. Le butin se verifie a part, par
    /// <see cref="ValidateLoot"/> : la validation d'un fichier ne doit pas exiger d'en charger un autre.
    /// </summary>
    public void Validate()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var zone in Zones)
        {
            if (string.IsNullOrWhiteSpace(zone.Id))
                throw new InvalidOperationException("Une zone sans identifiant a ete declaree.");

            if (!seen.Add(zone.Id))
                throw new InvalidOperationException($"La zone « {zone.Id} » est declaree deux fois.");

            if (string.IsNullOrWhiteSpace(zone.Name))
                throw new InvalidOperationException($"La zone « {zone.Id} » n'a pas de nom.");

            if (!Enum.IsDefined(zone.Layout))
            {
                throw new InvalidOperationException(
                    $"La zone « {zone.Id} » demande un agencement qui n'est pas implemente.");
            }

            if (zone.Layout == ZoneLayout.Aisles && zone.AisleSpacing < 2)
            {
                throw new InvalidOperationException(
                    $"La zone « {zone.Id} » a des allees espacees de {zone.AisleSpacing} : on n'y passerait pas.");
            }

            if (zone.Footprint.Length is not (0 or 2))
                throw new InvalidOperationException($"L'empreinte de « {zone.Id} » s'ecrit [[x1, y1], [x2, y2]].");

            if (zone.ZombieDensity is < 0f or > 1f)
                throw new InvalidOperationException($"La densite de « {zone.Id} » sort de l'intervalle 0-1.");
        }
    }

    /// <summary>
    /// Chaque table de butin ne doit nommer que des objets du catalogue livre. Une ligne fautive ne se
    /// verrait autrement qu'a la fouille — c'est-a-dire jamais, puisqu'elle ne produirait rien.
    /// </summary>
    public void ValidateLoot(ItemCatalogue items)
    {
        ArgumentNullException.ThrowIfNull(items);

        foreach (var zone in Zones)
        {
            foreach (string id in zone.Loot.Keys)
            {
                if (items.Find(id) is null)
                {
                    throw new InvalidOperationException(
                        $"La zone « {zone.Id} » contient « {id} », qui n'existe dans aucun catalogue d'objets.");
                }
            }
        }
    }
}

using System.Text.Json;
using Hordinary.Agents;
using Hordinary.Config;
using Hordinary.World;

namespace Hordinary.Game;

/// <summary>
/// Une mission proposee au joueur avant le depart, chargee depuis <c>config/missions.json</c>.
///
/// Comme les survivants, les objectifs et les actions, une mission est une <b>donnee</b>. Le code ne
/// nomme ni « ravitaillement pharmacie » ni « fouille de l'entrepot » : il sait seulement qu'il faut
/// aller chercher un objet quelque part et le ramener au camp. Ajouter une mission, en durcir une
/// autre ou echanger les lieux se fait dans le fichier, sans recompilation.
///
/// Le camp est a la fois le point de depart et le point de retour : une mission commence et finit au
/// meme endroit. C'est ce qui donne un sens au rappel du livrable — « rentrez au camp » designe un
/// lieu que tout le monde connait depuis la premiere seconde, parce que tout le monde en vient.
/// </summary>
public sealed record MissionDefinition
{
    public string Id { get; init; } = string.Empty;

    /// <summary>Nom affiche dans l'ecran de preparation.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Phrase de briefing, destinee au joueur. Vraie propriete JSON, pas un commentaire.</summary>
    public string? Description { get; init; }

    /// <summary>Etiquette de difficulte, purement lisible : elle ne regle rien par elle-meme.</summary>
    public string? Difficulty { get; init; }

    /// <summary>Ou se trouve ce qu'il faut rapporter.</summary>
    public ZoneKind Objective { get; init; } = ZoneKind.Pharmacy;

    /// <summary>
    /// Nom exact du lieu vise, quand plusieurs zones partagent le meme type — la carte livree compte
    /// deux batiments. Absent, c'est la premiere zone du type <see cref="Objective"/> qui sert.
    /// </summary>
    public string? ObjectiveZone { get; init; }

    /// <summary>Ce qu'il faut rapporter.</summary>
    public ItemKind Item { get; init; } = ItemKind.Medicine;

    /// <summary>
    /// Le camp : point de depart <b>et</b> point de retour. Une mission est terminee quand l'objet
    /// y revient, et la partie quand tout le monde y est rentre.
    /// </summary>
    public ZoneKind Camp { get; init; } = ZoneKind.Start;

    /// <summary>Nombre de caisses posees dans la zone d'objectif. Une suffit a gagner.</summary>
    public int Supplies { get; init; } = 1;

    /// <summary>
    /// Taille de horde propre a la mission. Absente, c'est <c>zombies.count</c> de
    /// <c>config/game_config.json</c> qui s'applique : une mission ne redit que ce qui la distingue.
    /// </summary>
    public int? ZombieCount { get; init; }

    /// <summary>Nombre d'agents conseille. Indicatif : l'ecran de preparation n'en fait pas une regle.</summary>
    public int RecommendedTeam { get; init; } = 3;

    /// <summary>Le lieu vise sur une carte donnee : par son nom s'il est precise, par son type sinon.</summary>
    public MapZone? ObjectiveIn(GridMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        if (ObjectiveZone is not { Length: > 0 } name) return map.GetZone(Objective);

        return map.Zones.FirstOrDefault(zone => zone.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? map.GetZone(Objective);
    }

    /// <summary>Le camp sur une carte donnee : le point de depart, et le point de retour.</summary>
    public MapZone? CampIn(GridMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return map.GetZone(Camp);
    }

    /// <summary>Lieux annonces au groupe avant le depart : les endroits, jamais leur contenu.</summary>
    public IEnumerable<ZoneKind> BriefedZones()
    {
        yield return Camp;
        yield return Objective;

        // Le camp sert aussi de point d'extraction : le domaine de planification le connait sous ce
        // nom, et le briefing doit donc le nommer deux fois pour que « rejoindre l'extraction » et
        // « rentrer au camp » designent bien le meme endroit dans la memoire de chacun.
        yield return ZoneKind.Extraction;
    }

    public override string ToString()
        => $"{Name}{(Difficulty is { } level ? $" ({level})" : string.Empty)}";
}

/// <summary>
/// Les missions livrees, chargees depuis <c>config/missions.json</c>.
///
/// Comme <see cref="SurvivorRoster"/> pour les profils, le fichier est valide des le chargement :
/// une mission sans identifiant, un doublon ou une zone d'objectif confondue avec le camp doivent se
/// voir tout de suite, et non produire une partie qui se gagne a la premiere seconde.
/// </summary>
public sealed class MissionCatalogue
{
    public List<MissionDefinition> Missions { get; init; } = [];

    public static MissionCatalogue Empty { get; } = new();

    public MissionDefinition? Find(string? id)
        => id is null
            ? null
            : Missions.FirstOrDefault(mission => mission.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>La premiere mission du fichier, celle que l'ecran de preparation propose d'emblee.</summary>
    public MissionDefinition? Default => Missions.Count == 0 ? null : Missions[0];

    public static MissionCatalogue FromJson(string json)
    {
        var catalogue = JsonSerializer.Deserialize<MissionCatalogue>(json, GameConfig.JsonOptions) ?? Empty;
        catalogue.Validate();
        return catalogue;
    }

    public void Validate()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mission in Missions)
        {
            if (string.IsNullOrWhiteSpace(mission.Id))
                throw new InvalidOperationException("Une mission sans identifiant a ete declaree.");

            if (!seen.Add(mission.Id))
                throw new InvalidOperationException($"La mission « {mission.Id} » est declaree deux fois.");

            if (string.IsNullOrWhiteSpace(mission.Name))
                throw new InvalidOperationException($"La mission « {mission.Id} » n'a pas de nom.");

            if (mission.Objective == mission.Camp)
            {
                throw new InvalidOperationException(
                    $"La mission « {mission.Id} » va chercher son objectif dans le camp lui-meme : "
                  + "il n'y aurait rien a jouer.");
            }

            if (mission.Supplies <= 0)
                throw new InvalidOperationException($"La mission « {mission.Id} » ne pose aucune caisse.");

            if (mission.ZombieCount is < 0)
                throw new InvalidOperationException($"La mission « {mission.Id} » a une horde negative.");
        }
    }
}

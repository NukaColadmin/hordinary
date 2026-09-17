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

    /// <summary>
    /// Ou se trouve ce qu'il faut rapporter. Le champ accepte deux vocabulaires dans la meme chaine :
    /// un <see cref="ZoneKind"/> (<c>"Pharmacy"</c>) ou un identifiant de profil de
    /// <c>config/zones.json</c> (<c>"station_service"</c>). « Rapporter de l'essence » devient une
    /// mission en une ligne, sans que le code cesse de ne nommer aucun lieu.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("objective")]
    public ZoneTarget Target { get; init; } = new(ZoneKind.Pharmacy);

    /// <summary>La categorie structurelle visee, celle que le briefing et le planificateur emploient.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public ZoneKind Objective => Target.Kind;

    /// <summary>Le profil vise, quand la mission en nomme un plutot qu'une categorie.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ObjectiveProfile => Target.Profile;

    /// <summary>
    /// Nom exact du lieu vise, quand plusieurs zones partagent le meme type — la carte livree compte
    /// deux batiments. Absent, c'est la premiere zone du type <see cref="Objective"/> qui sert.
    /// </summary>
    public string? ObjectiveZone { get; init; }

    /// <summary>
    /// Ce qu'il faut rapporter, par identifiant de <c>config/items.json</c>. C'est cette ligne, et
    /// elle seule, qui dit au groupe ce qu'il va chercher : le code ne nomme aucun objet.
    /// </summary>
    public string Item { get; init; } = Survivor.DefaultObjectiveItem;

    /// <summary>
    /// Le camp : point de depart <b>et</b> point de retour. La partie est terminee quand tout le
    /// monde y est rentre — avec l'objet c'est une victoire, sans lui et le temps ecoule c'est une
    /// defaite. Le retour d'un seul ne conclut rien.
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

    /// <summary>
    /// Temps imparti a cette mission-la, en secondes. Absent, c'est <c>mission.time_limit</c> de
    /// <c>config/game_config.json</c> qui s'applique : une mission ne redit que ce qui la distingue.
    ///
    /// Zero desamorce le chronometre pour cette mission : le van attend indefiniment. Ce n'est pas
    /// un reglage de confort mais une vraie variante de regle — une mission d'installation, ou une
    /// mission de demonstration qu'on veut laisser tourner, n'a pas de van qui part.
    /// </summary>
    public double? TimeLimit { get; init; }

    /// <summary>
    /// L'ambiance du quartier a generer, par identifiant de <c>config/mapgen.json</c> (livrable 13).
    ///
    /// Absent, la mission se joue sur la carte dessinee a la main, et c'est ce qui garde la carte de
    /// demonstration du livrable 8 jouable et testable. Present, un quartier est genere a chaque
    /// depart : c'est la meme mission, sur un terrain qu'on n'a jamais vu.
    /// </summary>
    public string? MapProfile { get; init; }

    /// <summary>
    /// La graine du quartier, quand le joueur en impose une. Absente, c'est celle de la partie qui
    /// sert — l'ecran de preparation l'affiche et laisse la saisir, pour qu'un quartier interessant
    /// se retrouve et qu'un bug de carte se reproduise.
    /// </summary>
    public int? MapSeed { get; init; }

    /// <summary>Vrai si cette mission veut un quartier genere plutot que la carte dessinee.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsGenerated => !string.IsNullOrWhiteSpace(MapProfile);

    // --- Missions generees (livrable 16) ---------------------------------------------------------
    // Ces champs sont absents des missions ecrites a la main, et c'est leur absence qui distingue
    // les deux. Une mission manuelle garde donc exactement le comportement des livrables 8 a 13 :
    // rapporter Item au camp. Une mission generee remplace cette condition par ses Requirements,
    // sans qu'aucune des deux n'ait besoin d'une seconde classe — le suivi de partie, le
    // planificateur et l'ecran de camp ne voient qu'une MissionDefinition.

    /// <summary>
    /// Le type d'objectif, pour une mission generee. <c>null</c> : mission ecrite a la main, dont
    /// l'objectif reste « rapporter <see cref="Item"/> au camp ».
    /// </summary>
    public MissionObjectiveKind? Type { get; init; }

    /// <summary>Ce qu'il faut atteindre. Lu seulement quand <see cref="Type"/> est renseigne.</summary>
    public MissionRequirements? Requirements { get; init; }

    /// <summary>
    /// Ce que la victoire rapporte, connu avant le depart. Une mission manuelle n'en promet aucune :
    /// le livrable 16 reste retirable sans que le MVP change de comportement.
    /// </summary>
    public List<MissionReward> Rewards { get; init; } = [];

    /// <summary>
    /// La graine dont cette offre est sortie. Elle n'est pas la graine du quartier
    /// (<see cref="MapSeed"/>) mais celle du <b>contenu</b> de la mission : deux offres tirees a la
    /// meme graine et a la meme version du generateur sont identiques.
    /// </summary>
    public int? Seed { get; init; }

    /// <summary>Essence debitee au camp au lancement, une seule fois. Affichee avant l'acceptation.</summary>
    public int EntryFuelCost { get; init; }

    /// <summary>Vrai si la mission vient du generateur du livrable 16.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsOffer => Type is not null;

    /// <summary>
    /// L'etiquette de difficulte lue comme une valeur. Une mission generee la porte par
    /// construction ; une mission manuelle l'ecrit en clair et on la relit, ce qui suffit a l'ecran
    /// de camp pour la trier ou la colorer.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public MissionDifficulty? Level => MissionDifficultyNames.Parse(Difficulty);

    /// <summary>
    /// Ce qu'il faut poser au sol pour que la mission soit realisable. Une mission manuelle pose ses
    /// <see cref="Supplies"/> caisses d'<see cref="Item"/> ; une mission generee pose ce que ses
    /// exigences reclament, et une mission d'elimination ne pose rien.
    ///
    /// C'est la seule reponse a la question « qu'est-ce qu'on met dans la zone ? », et elle est ici
    /// pour que <c>GameRoot</c> et le harnais de test posent la meme chose — deux ecritures
    /// divergeraient, et le test cesserait de dire quoi que ce soit du jeu.
    /// </summary>
    public IReadOnlyList<ItemRequirement> SuppliesFor(ItemCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        if (Type is not { } kind) return [new ItemRequirement(Item, Math.Max(1, Supplies))];

        // Une mission d'elimination ne demande rien a rapporter — et pourtant on pose un objet dans
        // sa zone. C'est ce qui donne au groupe une raison d'y aller : « tuer huit zombies au
        // supermarche » n'a aucun demandeur dans le domaine de planification, alors que « il y a
        // quelque chose a chercher la-bas » en a un depuis le livrable 5. Le combat, lui, reste un
        // reflexe (FightZombie) et se declenche tout seul une fois sur place. Faire de
        // l'elimination un but symbolique est une instruction tactique, donc du ressort du
        // livrable 15 — c'est au joueur de dire « nettoyez cet endroit », pas a l'IA de le decider.
        if (kind == MissionObjectiveKind.KillZombies) return [new ItemRequirement(Item, 1)];

        return Requirements?.Payload(kind, catalogue) ?? [];
    }

    /// <summary>
    /// Le lieu vise sur une carte donnee. Trois lectures, de la plus precise a la plus generale : le
    /// profil quand la mission en nomme un, puis le nom exact quand il est ecrit, puis le type. Une
    /// mission dont l'objectif est un profil se resout donc exactement comme celle dont l'objectif est
    /// un <see cref="ZoneKind"/> — elle ne trouve simplement pas sa zone par le meme chemin.
    /// </summary>
    public MapZone? ObjectiveIn(GridMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        if (ObjectiveProfile is { Length: > 0 } profile)
        {
            var found = map.Zones.FirstOrDefault(
                zone => profile.Equals(zone.Profile, StringComparison.OrdinalIgnoreCase));

            if (found is not null) return found;
        }

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

        // Un objectif designe par profil dont le catalogue de zones n'a pas ete charge n'a pas de
        // categorie a annoncer. Mieux vaut un briefing court qu'un briefing qui nomme « Other ».
        if (Objective != ZoneKind.Other) yield return Objective;

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

    /// <summary>
    /// Charge le catalogue. Le catalogue de zones est facultatif : sans lui, une mission qui vise un
    /// profil se charge quand meme et trouve sa zone par le profil, mais sa categorie structurelle
    /// reste inconnue. Avec lui, elle est renseignee et la mission redevient une mission comme les
    /// autres — c'est tout ce que « accepter un profil comme objectif » veut dire.
    /// </summary>
    public static MissionCatalogue FromJson(string json, ZoneCatalogue? zones = null)
    {
        var catalogue = JsonSerializer.Deserialize<MissionCatalogue>(json, GameConfig.JsonOptions) ?? Empty;
        if (zones is not null) catalogue.Resolve(zones);
        catalogue.Validate();
        return catalogue;
    }

    /// <summary>Donne sa categorie structurelle a chaque objectif designe par profil.</summary>
    public void Resolve(ZoneCatalogue zones)
    {
        ArgumentNullException.ThrowIfNull(zones);

        for (int i = 0; i < Missions.Count; i++)
        {
            if (Missions[i].Target is not { ByProfile: true } target) continue;

            if (zones.Find(target.Profile) is not { } profile)
            {
                throw new InvalidOperationException(
                    $"La mission « {Missions[i].Id} » vise le profil « {target.Profile} », "
                  + "qui n'existe pas dans config/zones.json.");
            }

            Missions[i] = Missions[i] with { Target = target with { Kind = profile.Kind } };
        }
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

            if (mission.TimeLimit is < 0d)
                throw new InvalidOperationException($"La mission « {mission.Id} » a un temps imparti negatif.");
        }
    }
}

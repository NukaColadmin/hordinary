using Hordinary.Config;
using Hordinary.World;

namespace Hordinary.Game;

/// <summary>
/// Construit les offres de mission d'une journee (livrable 16), depuis une graine et les catalogues
/// livres.
///
/// <b>Ce qu'il ne fait pas.</b> Il n'ecrit aucun texte de briefing et n'invente aucun recit : une
/// offre est faite d'une zone, d'exigences, d'une recompense et d'un prix, et l'interface affiche
/// exactement cela. Une phrase generee serait la seule partie de l'offre que le joueur ne pourrait
/// pas comparer a une autre.
///
/// <b>Ce qu'il ne nomme pas.</b> Ni zone, ni objet, ni arme, ni ressource. Les zones viennent de
/// <c>config/zones.json</c>, les objets et les armes des catalogues, les regles de tirage de
/// <c>mission_generation</c>. Ajouter une zone eligible ou retirer une arme des recompenses est une
/// ligne de configuration.
///
/// <b>Le hasard.</b> Chaque tirage a une adresse (<see cref="Deterministic"/>) plutot qu'un rang
/// dans une suite : ajouter un tirage au milieu du generateur ne decale pas les suivants. Et tout ce
/// qui se lit dans un dictionnaire est trie avant d'etre parcouru — l'ordre d'ecriture d'un fichier
/// de configuration ne doit rien decider, sans quoi deplacer une ligne changerait les missions du
/// jour.
/// </summary>
public sealed class MissionGenerator
{
    private readonly MissionGenerationSettings _settings;
    private readonly ZoneCatalogue _zones;
    private readonly ItemCatalogue _items;

    public MissionGenerator(
        MissionGenerationSettings settings,
        ZoneCatalogue zones,
        ItemCatalogue items)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _zones = zones ?? throw new ArgumentNullException(nameof(zones));
        _items = items ?? throw new ArgumentNullException(nameof(items));
    }

    /// <summary>
    /// Ce que la generation a eu a dire — un rejet, un repli sur le profil de secours, une zone
    /// ecartee. La couche moteur le deverse dans le canal <c>[Mission]</c> du journal de debug.
    ///
    /// C'est une liste et non un evenement parce qu'une generation est instantanee : il n'y a
    /// personne pour ecouter pendant qu'elle se produit, et une liste se relit dans un test.
    /// </summary>
    public List<string> Journal { get; } = [];

    /// <summary>
    /// La graine d'une offre, derivee des quatre valeurs du livrable 16 : graine de partie, jour de
    /// campagne, rang de l'offre et version du generateur.
    ///
    /// Le melange est un FNV suivi d'un xorshift, comme <see cref="Deterministic"/> : une somme
    /// donnerait la meme graine a (jour 2, offre 0) et (jour 1, offre 1), et les offres du lendemain
    /// seraient celles de la veille decalees d'un cran.
    /// </summary>
    public static int SeedOf(int missionSeed, int day, int index, int version)
    {
        unchecked
        {
            uint hash = 2166136261u;

            foreach (int value in (int[])[missionSeed, day, index, version])
            {
                hash = (hash ^ (uint)value) * 16777619u;
            }

            hash ^= hash >> 15;
            hash *= 2246822519u;
            hash ^= hash >> 13;
            hash *= 3266489917u;
            hash ^= hash >> 16;

            return (int)(hash & 0x7fffffff);
        }
    }

    /// <summary>
    /// Les offres du jour. Elles sont figees des qu'elles sont rendues : changer de selection,
    /// ouvrir un detail ou relancer l'interface repasse par la meme graine et rend les memes.
    /// </summary>
    public IReadOnlyList<MissionDefinition> Offer(int missionSeed, int day = 0)
    {
        Journal.Clear();

        var offers = new List<MissionDefinition>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < _settings.OfferCount; index++)
        {
            int seed = SeedOf(missionSeed, day, index, _settings.GeneratorVersion);
            var offer = Build(seed, index, taken);

            offers.Add(offer);
            if (offer.ObjectiveProfile is { Length: > 0 } profile) taken.Add(profile);
        }

        return offers;
    }

    // --- Une offre --------------------------------------------------------------------------------

    /// <summary>
    /// Une offre, avec autant de tentatives que la configuration en accorde. Chaque tentative repart
    /// d'un sous-seed derive de la graine de l'offre : deux tentatives ne se ressemblent pas, et la
    /// suite reste entierement determinee par la graine de depart.
    /// </summary>
    private MissionDefinition Build(int seed, int index, IReadOnlySet<string> taken)
    {
        string? lastFailure = null;

        for (int attempt = 0; attempt < _settings.MaxAttemptsPerOffer; attempt++)
        {
            int sub = SeedOf(seed, attempt, index, _settings.GeneratorVersion);

            if (Attempt(seed, sub, index, taken, attempt) is { } offer)
            {
                if (Invalid(offer) is not { } reason) return offer;

                lastFailure = reason;
                continue;
            }

            lastFailure ??= "aucune zone eligible pour le type tire";
        }

        Journal.Add(
            $"Offre {index + 1} (graine {seed}) : {_settings.MaxAttemptsPerOffer} tentatives sans "
          + $"resultat ({lastFailure ?? "raison inconnue"}) — profil de secours employe.");

        return Fallback(seed, index);
    }

    private MissionDefinition? Attempt(int seed, int sub, int index, IReadOnlySet<string> taken, int attempt)
    {
        if (ChooseType(sub) is not { } kind) return null;
        if (ChooseZone(sub, kind, taken) is not { } zone) return null;

        var requirements = Requirements(sub, kind, zone);
        if (requirements is null) return null;

        float load = Load(kind, requirements, zone);
        var difficulty = _settings.Difficulty.Of(Score(load, zone));

        int zombies = HordeFor(kind, requirements, zone);
        string lure = kind == MissionObjectiveKind.KillZombies ? Lure(sub, zone) : string.Empty;

        var rewards = Rewards(sub, difficulty);
        if (rewards.Count == 0)
        {
            Journal.Add($"Offre {index + 1}, tentative {attempt + 1} : aucune recompense valide tiree.");
            return null;
        }

        return new MissionDefinition
        {
            Id = $"offre-{index + 1}-{seed:x8}",
            Name = $"{MissionObjectiveNames.Of(kind)} — {zone.Name}",

            // Une mission generee n'a pas de briefing : l'ecran affiche des donnees comparables, et
            // pas une phrase. Le champ reste null, et un test le verifie.
            Description = null,

            Difficulty = MissionDifficultyNames.Of(difficulty),
            Type = kind,
            Requirements = requirements,
            Rewards = [.. rewards],
            Seed = seed,
            EntryFuelCost = _settings.EntryFuelCost(difficulty),

            Target = new ZoneTarget(zone.Kind, zone.Id),
            ObjectiveZone = zone.Name,
            Camp = ZoneKind.Start,

            Item = kind == MissionObjectiveKind.KillZombies
                ? lure
                : requirements.Payload(kind, _items).FirstOrDefault()?.Item ?? DefaultItem,

            Supplies = 1,
            ZombieCount = zombies,
            RecommendedTeam = _settings.RecommendedTeam(difficulty),
            TimeLimit = _settings.TimeLimit(difficulty),

            MapProfile = _settings.MapProfile(difficulty),
            MapSeed = seed,
        };
    }

    /// <summary>Objet de repli quand le catalogue ne propose rien : celui du MVP.</summary>
    private static string DefaultItem => Agents.Survivor.DefaultObjectiveItem;

    // --- Etape 1 : le type ------------------------------------------------------------------------

    private MissionObjectiveKind? ChooseType(int seed)
    {
        var kinds = _settings.EnabledTypes;
        if (kinds.Count == 0) return null;

        float total = kinds.Sum(_settings.WeightOf);
        if (total <= 0f) return null;

        float roll = Deterministic.Unit(seed, "mission.type", 0) * total;

        foreach (var kind in kinds)
        {
            roll -= _settings.WeightOf(kind);
            if (roll <= 0f) return kind;
        }

        return kinds[^1];
    }

    // --- Etape 2 : la zone ------------------------------------------------------------------------

    /// <summary>
    /// Une zone eligible, differente de celles deja retenues quand c'est possible. « Quand c'est
    /// possible » et non « toujours » : avec cinq zones et trois offres la contrainte tient, mais un
    /// catalogue plus court ne doit pas faire echouer la generation pour du confort d'affichage.
    /// </summary>
    private ZoneProfile? ChooseZone(int seed, MissionObjectiveKind kind, IReadOnlySet<string> taken)
    {
        var eligible = _zones.Zones
            .Where(zone => Eligible(zone, kind))
            .OrderBy(zone => zone.Id, StringComparer.Ordinal)
            .ToList();

        if (eligible.Count == 0) return null;

        var fresh = eligible.Where(zone => !taken.Contains(zone.Id)).ToList();
        var pool = fresh.Count > 0 ? fresh : eligible;

        return pool[Deterministic.Between(seed, "mission.zone", 0, 0, pool.Count - 1)];
    }

    /// <summary>
    /// Une zone peut porter ce type d'objectif. Trois exclusions d'abord, qui valent pour tous les
    /// types : le camp, la sortie, et une empreinte trop petite pour qu'on y joue quoi que ce soit.
    /// Puis la declaration explicite de la zone, si elle en fait une. Puis les regles generales.
    /// </summary>
    private bool Eligible(ZoneProfile zone, MissionObjectiveKind kind)
    {
        if (zone.Kind is ZoneKind.Start or ZoneKind.Extraction or ZoneKind.Other) return false;
        if (zone.Rectangle() is not { } rect) return false;

        int area = (rect.Max.X - rect.Min.X + 1) * (rect.Max.Y - rect.Min.Y + 1);
        if (area < 4) return false;

        if (zone.Accepts(kind) is { } declared) return declared;

        return kind switch
        {
            MissionObjectiveKind.KillZombies => zone.ZombieDensity > 0f,
            MissionObjectiveKind.CollectObjects => Lootable(zone).Count > 0,
            _ => ResourcesOf(zone).Count > 0,
        };
    }

    /// <summary>Le butin de la zone que le catalogue connait, trie : l'ordre du fichier ne decide rien.</summary>
    private IReadOnlyList<string> Lootable(ZoneProfile zone)
        => [.. zone.Loot.Keys.Where(id => _items.Find(id) is not null).OrderBy(id => id, StringComparer.Ordinal)];

    /// <summary>Les ressources vitales que cette zone peut fournir, dans l'ordre de reference.</summary>
    private IReadOnlyList<string> ResourcesOf(ZoneProfile zone)
        => [.. MissionRequirements.PriorityResources.Where(resource => StockerIn(zone, resource) is not null)];

    /// <summary>L'objet de cette zone qui alimente cette reserve, s'il y en a un dans son butin.</summary>
    private ItemDefinition? StockerIn(ZoneProfile zone, string resource)
    {
        foreach (string id in Lootable(zone))
        {
            if (_items.Find(id) is not { } item) continue;

            if (item.Effects.Any(effect =>
                    string.Equals(CampStock.ResourceOf(effect), resource, StringComparison.OrdinalIgnoreCase)))
            {
                return item;
            }
        }

        return null;
    }

    // --- Etape 3 : les exigences ------------------------------------------------------------------

    private MissionRequirements? Requirements(int seed, MissionObjectiveKind kind, ZoneProfile zone) => kind switch
    {
        MissionObjectiveKind.KillZombies => KillRequirements(seed, zone),
        MissionObjectiveKind.CollectObjects => ObjectRequirements(seed, zone),
        _ => ResourceRequirements(seed, zone),
    };

    private MissionRequirements KillRequirements(int seed, ZoneProfile zone)
    {
        int min = (int)Math.Max(1f, _settings.Zombies.Min);
        int max = (int)Math.Max(min, _settings.Zombies.Max);

        // La densite de la zone module la demande : on ne demande pas huit morts dans la
        // quincaillerie, ou il n'y a presque personne.
        int span = min + (int)MathF.Round((max - min) * Math.Clamp(zone.ZombieDensity, 0f, 1f));

        return new MissionRequirements
        {
            Zombies = Deterministic.Between(seed, "mission.zombies", 0, min, Math.Max(min, span)),
        };
    }

    private MissionRequirements? ObjectRequirements(int seed, ZoneProfile zone)
    {
        var pool = Lootable(zone);
        if (pool.Count == 0) return null;

        var bounds = _settings.Objects;

        int wanted = Deterministic.Between(
            seed, "mission.items.count", 0,
            Math.Max(1, bounds.MinDistinctItems),
            Math.Max(1, Math.Min(bounds.MaxDistinctItems, pool.Count)));

        var chosen = Pick(pool, wanted, seed, "mission.items.pick");
        var lines = new List<ItemRequirement>();

        foreach (string id in chosen)
        {
            // Jamais plus que ce que la table de butin de la zone laisse esperer : une mission qui
            // demanderait six pieds-de-biche a la quincaillerie serait realisable — on pose ce qu'on
            // demande — mais elle mentirait sur l'endroit ou elle envoie.
            int ceiling = Math.Max(1, (int)MathF.Floor(zone.Loot[id].Max));

            int quantity = Deterministic.Between(
                seed, "mission.items.quantity", chosen.IndexOf(id),
                Math.Max(1, bounds.MinQuantity),
                Math.Max(1, Math.Min(bounds.MaxQuantity, ceiling)));

            lines.Add(new ItemRequirement(id, quantity));
        }

        return lines.Count == 0 ? null : new MissionRequirements { Items = lines };
    }

    private MissionRequirements? ResourceRequirements(int seed, ZoneProfile zone)
    {
        var pool = ResourcesOf(zone);
        if (pool.Count == 0) return null;

        var bounds = _settings.Resources;

        int wanted = Deterministic.Between(
            seed, "mission.resources.count", 0,
            Math.Max(1, bounds.MinDistinctResources),
            Math.Max(1, Math.Min(bounds.MaxDistinctResources, pool.Count)));

        var chosen = Pick(pool, wanted, seed, "mission.resources.pick");
        var lines = new List<ResourceRequirement>();

        foreach (string resource in chosen)
        {
            if (StockerIn(zone, resource) is not { } stocker) continue;

            int ceiling = Math.Max(1, (int)MathF.Floor(zone.Loot[stocker.Id].Max));

            int quantity = Deterministic.Between(
                seed, "mission.resources.quantity", chosen.IndexOf(resource),
                Math.Max(1, bounds.MinQuantity),
                Math.Max(1, Math.Min(bounds.MaxQuantity, ceiling)));

            lines.Add(new ResourceRequirement(resource, quantity));
        }

        return lines.Count == 0 ? null : new MissionRequirements { Resources = lines };
    }

    /// <summary>
    /// Tire <paramref name="wanted"/> elements distincts d'une liste deja triee, sans repasser deux
    /// fois au meme endroit. Le resultat garde l'ordre de la liste : deux offres qui tirent les
    /// memes objets les enumerent pareil.
    /// </summary>
    private static List<string> Pick(IReadOnlyList<string> pool, int wanted, int seed, string channel)
    {
        var remaining = new List<string>(pool);
        var chosen = new List<string>();

        for (int step = 0; step < wanted && remaining.Count > 0; step++)
        {
            int at = Deterministic.Between(seed, channel, step, 0, remaining.Count - 1);
            chosen.Add(remaining[at]);
            remaining.RemoveAt(at);
        }

        chosen.Sort(StringComparer.Ordinal);
        return chosen;
    }

    /// <summary>L'objet pose dans la zone d'une mission d'elimination. Voir <see cref="MissionDefinition.SuppliesFor"/>.</summary>
    private string Lure(int seed, ZoneProfile zone)
    {
        var pool = Lootable(zone);
        if (pool.Count == 0) return DefaultItem;

        return pool[Deterministic.Between(seed, "mission.lure", 0, 0, pool.Count - 1)];
    }

    // --- Etape 4 : la difficulte ------------------------------------------------------------------

    /// <summary>Ce que la mission demande, ramene entre 0 et 1.</summary>
    private float Load(MissionObjectiveKind kind, MissionRequirements requirements, ZoneProfile zone)
    {
        if (kind == MissionObjectiveKind.KillZombies)
        {
            float min = Math.Max(1f, _settings.Zombies.Min);
            float max = Math.Max(min + 1f, _settings.Zombies.Max);

            return Math.Clamp((requirements.Zombies - min) / (max - min), 0f, 1f);
        }

        var payload = requirements.Payload(kind, _items);
        if (payload.Count == 0) return 0f;

        int total = payload.Sum(line => line.Quantity);

        var bounds = kind == MissionObjectiveKind.CollectObjects
            ? (Distinct: _settings.Objects.MaxDistinctItems, Quantity: _settings.Objects.MaxQuantity)
            : (Distinct: _settings.Resources.MaxDistinctResources, Quantity: _settings.Resources.MaxQuantity);

        int ceiling = Math.Max(1, bounds.Distinct * bounds.Quantity);

        // Une zone verrouillee ou a piece du fond coute une effraction de plus : elle compte dans la
        // charge, pas seulement dans le danger.
        float locked = zone.Access.LockedChance * 0.5f + (zone.Access.InnerRoom is null ? 0f : 0.25f);

        return Math.Clamp(((float)total / ceiling) + locked, 0f, 1f);
    }

    private float Score(float load, ZoneProfile zone)
    {
        float weight = Math.Clamp(_settings.Difficulty.ZoneWeight, 0f, 1f);

        return Math.Clamp(((1f - weight) * load) + (weight * Math.Clamp(zone.ZombieDensity, 0f, 1f)), 0f, 1f);
    }

    /// <summary>
    /// La taille de horde de la mission. Pour une elimination, elle est <b>calculee</b> et non
    /// tiree : le livrable exige qu'on ne demande jamais plus de morts que la zone n'en recevra. La
    /// part de la horde qui echoit a la zone est sa densite, celle-la meme que
    /// <c>config/zones.json</c> declare — la faire croitre jusqu'a ce que le compte y soit, c'est
    /// exactement « augmenter la densite de la zone » du livrable 16.
    /// </summary>
    private static int HordeFor(MissionObjectiveKind kind, MissionRequirements requirements, ZoneProfile zone)
    {
        if (kind != MissionObjectiveKind.KillZombies) return Math.Max(4, (int)MathF.Round(zone.ZombieDensity * 12f));

        float density = Math.Clamp(zone.ZombieDensity, 0.05f, 1f);
        int horde = Math.Max(requirements.Zombies, (int)MathF.Ceiling(requirements.Zombies / density));

        while (Assigned(horde, density) < requirements.Zombies) horde++;

        return horde;
    }

    /// <summary>Combien de zombies de la horde reviennent a la zone cible.</summary>
    private static int Assigned(int horde, float density) => (int)MathF.Floor(horde * density);

    // --- Etape 5 : les recompenses ----------------------------------------------------------------

    private List<MissionReward> Rewards(int seed, MissionDifficulty difficulty)
    {
        var table = _settings.Rewards(difficulty);
        var rewards = new List<MissionReward>();

        if (table.Guaranteed.Count > 0)
        {
            int at = Deterministic.Between(seed, "mission.reward.guaranteed", 0, 0, table.Guaranteed.Count - 1);
            if (Draw(seed, table.Guaranteed[at], 0) is { } granted) rewards.Add(granted);
        }

        for (int index = 0; index < table.Bonus.Count; index++)
        {
            if (Deterministic.Unit(seed, "mission.reward.bonus", index) >= table.BonusChance) continue;
            if (Draw(seed, table.Bonus[index], index + 1) is { } bonus) rewards.Add(bonus);
        }

        return rewards;
    }

    private MissionReward? Draw(int seed, RewardCategory category, int step)
    {
        var pool = _settings.PoolOf(category);
        if (pool.Count == 0) return null;

        string item = pool[Deterministic.Between(seed, $"mission.reward.{category}", step, 0, pool.Count - 1)];
        if (_items.Find(item) is null) return null;

        var range = _settings.QuantityOf(category);

        int quantity = Deterministic.Between(
            seed, $"mission.reward.{category}.quantity", step,
            Math.Max(1, (int)range.Min), Math.Max(1, (int)range.Max));

        return new MissionReward(category, item, quantity);
    }

    // --- Etape 6 : la validation ------------------------------------------------------------------

    /// <summary>
    /// Pourquoi cette offre est a jeter, ou <c>null</c> si elle tient. Le generateur ne doit jamais
    /// afficher une mission impossible : mieux vaut trente-neuf rejets et un profil de secours
    /// qu'une seule offre qu'on ne peut pas remplir.
    /// </summary>
    private string? Invalid(MissionDefinition offer)
    {
        if (offer.Type is not { } kind) return "offre sans type";
        if (offer.Requirements is not { } requirements) return "offre sans exigence";
        if (offer.ObjectiveProfile is not { Length: > 0 }) return "offre sans zone cible";
        if (offer.Objective == offer.Camp) return "la zone cible est le camp";
        if (offer.EntryFuelCost < 0) return "cout d'entree negatif";
        if (offer.TimeLimit is not > 0d) return "temps imparti nul";
        if (offer.RecommendedTeam <= 0) return "equipe conseillee vide";
        if (offer.Rewards.Count == 0) return "offre sans recompense";

        foreach (var reward in offer.Rewards)
        {
            if (reward.Quantity <= 0) return $"recompense « {reward.Item} » sans quantite";
            if (_items.Find(reward.Item) is null) return $"recompense inconnue : « {reward.Item} »";
        }

        if (kind == MissionObjectiveKind.KillZombies)
        {
            if (requirements.Zombies <= 0) return "elimination sans zombie a abattre";

            var zone = _zones.Find(offer.ObjectiveProfile);
            float density = Math.Clamp(zone?.ZombieDensity ?? 0f, 0.05f, 1f);

            if (Assigned(offer.ZombieCount ?? 0, density) < requirements.Zombies)
                return "plus de morts demandees que la zone n'en recevra";

            return _items.Find(offer.Item) is null ? "objet d'appel inconnu du catalogue" : null;
        }

        var payload = requirements.Payload(kind, _items);
        if (payload.Count == 0) return "recuperation sans objet a rapporter";

        foreach (var line in payload)
        {
            if (line.Quantity <= 0) return $"exigence « {line.Item} » sans quantite";
            if (_items.Find(line.Item) is null) return $"exigence inconnue du catalogue : « {line.Item} »";
        }

        return null;
    }

    // --- Etape 7 : le repli -----------------------------------------------------------------------

    /// <summary>
    /// Le profil de secours, tel que la configuration l'ecrit. Il passe par la meme validation que
    /// les autres : un profil de secours fautif doit se voir, et non produire l'unique mission
    /// impossible que tout le reste du generateur s'emploie a eviter.
    /// </summary>
    private MissionDefinition Fallback(int seed, int index)
    {
        var profile = _settings.Fallback;
        var zone = _zones.Find(profile.TargetZone);

        var requirements = profile.ToRequirements();
        var rewards = Rewards(seed, profile.Difficulty);

        var offer = new MissionDefinition
        {
            Id = $"secours-{index + 1}-{seed:x8}",
            Name = $"{MissionObjectiveNames.Of(profile.Type)} — {zone?.Name ?? profile.TargetZone}",
            Description = null,
            Difficulty = MissionDifficultyNames.Of(profile.Difficulty),
            Type = profile.Type,
            Requirements = requirements,
            Rewards = [.. rewards],
            Seed = seed,
            EntryFuelCost = _settings.EntryFuelCost(profile.Difficulty),

            Target = new ZoneTarget(zone?.Kind ?? ZoneKind.Building, profile.TargetZone),
            ObjectiveZone = zone?.Name ?? profile.TargetZone,
            Camp = ZoneKind.Start,

            Item = profile.Type == MissionObjectiveKind.KillZombies
                ? Lure(seed, zone ?? new ZoneProfile())
                : requirements.Payload(profile.Type, _items).FirstOrDefault()?.Item ?? DefaultItem,

            Supplies = 1,
            ZombieCount = zone is null
                ? Math.Max(4, requirements.Zombies)
                : HordeFor(profile.Type, requirements, zone),

            RecommendedTeam = _settings.RecommendedTeam(profile.Difficulty),
            TimeLimit = _settings.TimeLimit(profile.Difficulty),
            MapProfile = _settings.MapProfile(profile.Difficulty),
            MapSeed = seed,
        };

        if (Invalid(offer) is { } reason)
        {
            throw new InvalidOperationException(
                $"Le profil de secours de mission_generation est lui-meme invalide : {reason}. "
              + "Aucune offre ne peut plus etre produite.");
        }

        return offer;
    }
}

/// <summary>
/// Comment un type d'objectif se dit au joueur. Le titre d'une offre est fait de ce mot et du nom de
/// la zone, et de rien d'autre : « Elimination — Supermarche ». Pas de briefing, pas de recit.
/// </summary>
public static class MissionObjectiveNames
{
    public static string Of(MissionObjectiveKind kind) => kind switch
    {
        MissionObjectiveKind.KillZombies => "Elimination",
        MissionObjectiveKind.CollectObjects => "Recuperation",
        _ => "Ravitaillement",
    };

    /// <summary>Comment une ressource vitale se dit au joueur.</summary>
    public static string Resource(string resource) => resource.ToLowerInvariant() switch
    {
        "food" => "Nourriture",
        "water" => "Eau",
        "fuel" => "Essence",
        _ => resource,
    };
}

using Hordinary.Config;
using Hordinary.World;

namespace Hordinary.Game;

/// <summary>
/// Reglages de la generation de missions (livrable 16), section <c>mission_generation</c> de
/// <c>config/game_config.json</c>.
///
/// Tout ce qui decide d'une offre vit ici : combien on en propose, avec quelles probabilites de
/// type, dans quelles bornes de quantite, pour quelle recompense et a quel prix en essence. Le
/// generateur, lui, ne nomme ni zone, ni objet, ni arme — il lit ce fichier et les catalogues.
///
/// <see cref="GeneratorVersion"/> merite un mot. Une graine ne promet un contenu identique que
/// <b>pour un jeu de regles donne</b> : le jour ou l'on change une borne ou un poids, l'ancienne
/// graine ne reproduit plus rien, et pretendre le contraire serait le pire des deux mondes. La
/// version entre donc dans le calcul de la graine d'offre, et l'incrementer dit honnetement « ce
/// sont d'autres missions ».
/// </summary>
public sealed class MissionGenerationSettings
{
    /// <summary>
    /// A <c>false</c>, l'ecran de camp ne propose que les missions ecrites a la main dans
    /// <c>config/missions.json</c>. C'est le mode de la carte de demonstration et des tests cibles :
    /// le livrable 16 reste retirable sans casser le MVP, et cette ligne est ce qui le retire.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Nombre d'offres proposees a l'ouverture de l'ecran de preparation.</summary>
    public int OfferCount { get; init; } = 3;

    /// <summary>Version des regles de generation. Voir le commentaire de la classe.</summary>
    public int GeneratorVersion { get; init; } = 1;

    /// <summary>Tentatives avant de se rabattre sur <see cref="Fallback"/> pour une offre.</summary>
    public int MaxAttemptsPerOffer { get; init; } = 40;

    /// <summary>
    /// Poids relatifs des trois types d'objectif. Un poids nul retire le type de la generation sans
    /// qu'il faille retirer la ligne — c'est ce qui permet de jouer une campagne sans elimination.
    /// </summary>
    public Dictionary<MissionObjectiveKind, float> Types { get; init; } = new()
    {
        [MissionObjectiveKind.KillZombies] = 0.35f,
        [MissionObjectiveKind.CollectObjects] = 0.35f,
        [MissionObjectiveKind.CollectPriorityResources] = 0.30f,
    };

    /// <summary>Bornes du nombre de zombies demande par une mission d'elimination.</summary>
    public RollRange Zombies { get; init; } = new(3f, 12f);

    public ObjectRequirementBounds Objects { get; init; } = new();

    public ResourceRequirementBounds Resources { get; init; } = new();

    /// <summary>Ou passent les frontieres entre Facile, Moyenne et Difficile.</summary>
    public DifficultyThresholds Difficulty { get; init; } = new();

    public Dictionary<MissionDifficulty, int> EntryFuelCostByDifficulty { get; init; } = new()
    {
        [MissionDifficulty.Easy] = 1,
        [MissionDifficulty.Medium] = 2,
        [MissionDifficulty.Hard] = 3,
    };

    public Dictionary<MissionDifficulty, double> TimeLimitByDifficulty { get; init; } = new()
    {
        [MissionDifficulty.Easy] = 180d,
        [MissionDifficulty.Medium] = 240d,
        [MissionDifficulty.Hard] = 300d,
    };

    public Dictionary<MissionDifficulty, int> RecommendedTeamByDifficulty { get; init; } = new()
    {
        [MissionDifficulty.Easy] = 1,
        [MissionDifficulty.Medium] = 2,
        [MissionDifficulty.Hard] = 3,
    };

    /// <summary>
    /// Ambiance de quartier (livrable 13) par difficulte. Une mission generee se joue toujours sur un
    /// quartier genere : elle vise un profil de zone que la carte dessinee ne porte pas.
    /// </summary>
    public Dictionary<MissionDifficulty, string> MapProfileByDifficulty { get; init; } = new()
    {
        [MissionDifficulty.Easy] = "faubourg",
        [MissionDifficulty.Medium] = "banlieue",
        [MissionDifficulty.Hard] = "centre_ville",
    };

    /// <summary>La table du livrable 16 : ce qu'une difficulte garantit, et ce qu'elle peut ajouter.</summary>
    public Dictionary<MissionDifficulty, RewardTable> RewardsByDifficulty { get; init; } = new()
    {
        [MissionDifficulty.Easy] = new RewardTable
        {
            Guaranteed = [RewardCategory.Medikit, RewardCategory.Rare],
        },

        [MissionDifficulty.Medium] = new RewardTable
        {
            Guaranteed = [RewardCategory.Medikit, RewardCategory.Weapon],
            Bonus = [RewardCategory.Rare],
        },

        [MissionDifficulty.Hard] = new RewardTable
        {
            Guaranteed = [RewardCategory.Weapon],
            Bonus = [RewardCategory.Medikit, RewardCategory.Rare],
        },
    };

    /// <summary>
    /// Ce que chaque categorie de recompense peut donner, par identifiant de catalogue. Aucun code ne
    /// nomme un fusil : retirer le fusil des recompenses est une ligne de ce fichier.
    /// </summary>
    public Dictionary<RewardCategory, List<string>> RewardPools { get; init; } = [];

    /// <summary>Quantite tiree pour chaque categorie. Absente, c'est une unite.</summary>
    public Dictionary<RewardCategory, RollRange> RewardQuantities { get; init; } = [];

    /// <summary>
    /// L'offre de secours, employee quand <see cref="MaxAttemptsPerOffer"/> tentatives n'ont rien
    /// donne. Elle est <b>ecrite</b>, jamais devinee : le generateur ne doit jamais afficher une
    /// mission impossible, et une derniere tentative relachee en produirait justement une.
    /// </summary>
    public MissionFallbackProfile Fallback { get; init; } = new();

    public static MissionGenerationSettings Default { get; } = new();

    /// <summary>Le poids d'un type, jamais negatif. Un type absent du fichier ne sort pas.</summary>
    public float WeightOf(MissionObjectiveKind kind)
        => Types.TryGetValue(kind, out float weight) ? Math.Max(0f, weight) : 0f;

    /// <summary>
    /// Les types reellement tirables, dans l'ordre de l'enumeration et non celui du fichier. C'est
    /// la premiere des deux precautions qui rendent la generation independante de l'ordre
    /// d'ecriture de la configuration ; la seconde est le tri des tables de butin.
    /// </summary>
    public IReadOnlyList<MissionObjectiveKind> EnabledTypes
        => [.. Enum.GetValues<MissionObjectiveKind>().Where(kind => WeightOf(kind) > 0f)];

    public int EntryFuelCost(MissionDifficulty difficulty)
        => EntryFuelCostByDifficulty.TryGetValue(difficulty, out int cost) ? cost : 0;

    public double TimeLimit(MissionDifficulty difficulty)
        => TimeLimitByDifficulty.TryGetValue(difficulty, out double limit) ? limit : 300d;

    public int RecommendedTeam(MissionDifficulty difficulty)
        => RecommendedTeamByDifficulty.TryGetValue(difficulty, out int team) ? Math.Max(1, team) : 1;

    public string? MapProfile(MissionDifficulty difficulty)
        => MapProfileByDifficulty.TryGetValue(difficulty, out string? profile) ? profile : null;

    public RewardTable Rewards(MissionDifficulty difficulty)
        => RewardsByDifficulty.TryGetValue(difficulty, out var table) ? table : new RewardTable();

    /// <summary>Le vivier d'une categorie, trie : l'ordre du fichier ne doit rien decider.</summary>
    public IReadOnlyList<string> PoolOf(RewardCategory category)
        => RewardPools.TryGetValue(category, out var pool)
            ? [.. pool.OrderBy(id => id, StringComparer.Ordinal)]
            : [];

    public RollRange QuantityOf(RewardCategory category)
        => RewardQuantities.TryGetValue(category, out var range) ? range : new RollRange(1f, 1f);

    /// <summary>
    /// Verifie ce qui doit se voir au chargement plutot qu'a l'ecran de camp : un vivier vide, une
    /// arme qui n'en est pas, une recompense inconnue du catalogue, une difficulte sans table.
    ///
    /// Les deux catalogues sont facultatifs, sur le modele de <see cref="ZoneCatalogue.ValidateLoot"/> :
    /// valider un fichier ne doit pas exiger d'en charger deux autres.
    /// </summary>
    public void Validate(ItemCatalogue? items = null, ZoneCatalogue? zones = null)
    {
        if (OfferCount <= 0)
            throw new InvalidOperationException("mission_generation.offer_count doit proposer au moins une offre.");

        if (MaxAttemptsPerOffer <= 0)
            throw new InvalidOperationException("mission_generation.max_attempts_per_offer doit valoir au moins 1.");

        if (EnabledTypes.Count == 0)
            throw new InvalidOperationException("mission_generation.types n'active aucun type d'objectif.");

        foreach (var difficulty in Enum.GetValues<MissionDifficulty>())
        {
            if (!EntryFuelCostByDifficulty.ContainsKey(difficulty))
            {
                throw new InvalidOperationException(
                    $"mission_generation.entry_fuel_cost_by_difficulty ne dit rien de « {difficulty} » : "
                  + "le cout affiche et le cout debite doivent venir de la meme ligne.");
            }

            if (EntryFuelCost(difficulty) < 0)
                throw new InvalidOperationException($"Le cout d'entree de « {difficulty} » est negatif.");

            if (!RewardsByDifficulty.ContainsKey(difficulty))
                throw new InvalidOperationException($"Aucune table de recompenses pour « {difficulty} ».");

            var table = Rewards(difficulty);

            if (table.Guaranteed.Count == 0)
            {
                throw new InvalidOperationException(
                    $"La table de recompenses de « {difficulty} » ne garantit rien : une mission "
                  + "rapporte toujours quelque chose, sinon elle ne se choisit pas.");
            }

            if (table.BonusChance is < 0f or > 1f)
                throw new InvalidOperationException($"bonus_chance de « {difficulty} » sort de l'intervalle 0-1.");

            foreach (var category in table.Guaranteed.Concat(table.Bonus))
            {
                if (PoolOf(category).Count == 0)
                {
                    throw new InvalidOperationException(
                        $"La difficulte « {difficulty} » promet une recompense « {category} », "
                      + "dont mission_generation.reward_pools ne declare aucun objet.");
                }
            }
        }

        if (items is not null) ValidatePools(items);
        if (zones is not null) ValidateFallback(zones);
    }

    /// <summary>
    /// Une arme doit venir de <c>config/weapons.json</c>, un medikit et un objet rare de
    /// <c>config/items.json</c>. Le catalogue livre reunit les deux fichiers : ce qui les distingue
    /// n'est donc pas leur provenance mais leur categorie, et c'est elle qu'on verifie.
    /// </summary>
    private void ValidatePools(ItemCatalogue items)
    {
        foreach (var (category, pool) in RewardPools)
        {
            foreach (string id in pool)
            {
                if (items.Find(id) is not { } item)
                {
                    throw new InvalidOperationException(
                        $"La recompense « {id} » de la categorie « {category} » n'existe dans aucun catalogue.");
                }

                bool weapon = item.Category == ItemCategory.Weapon;

                if (category == RewardCategory.Weapon && !weapon)
                {
                    throw new InvalidOperationException(
                        $"« {id} » est promis comme arme, mais le catalogue en fait un {item.Category}.");
                }

                if (category != RewardCategory.Weapon && weapon)
                {
                    throw new InvalidOperationException(
                        $"« {id} » est une arme, et ne peut pas etre promis comme « {category} ».");
                }
            }
        }
    }

    private void ValidateFallback(ZoneCatalogue zones)
    {
        if (string.IsNullOrWhiteSpace(Fallback.TargetZone))
            throw new InvalidOperationException("Le profil de secours ne vise aucune zone.");

        if (!zones.Declares(Fallback.TargetZone))
        {
            throw new InvalidOperationException(
                $"Le profil de secours vise « {Fallback.TargetZone} », absent de config/zones.json.");
        }
    }
}

/// <summary>
/// Comment une offre recoit son etiquette. La difficulte n'est pas tiree au sort : elle se calcule
/// a partir de ce que la mission demande (la charge) et de l'endroit ou elle l'envoie (la densite de
/// zombies de la zone). C'est ce qui garantit que l'etiquette et le contenu ne peuvent pas diverger
/// — et donc que le cout d'entree, le temps imparti et la recompense, tous trois lus par elle,
/// restent d'accord avec ce que le joueur va vivre.
/// </summary>
public sealed class DifficultyThresholds
{
    /// <summary>Score a partir duquel une offre cesse d'etre Facile.</summary>
    public float Medium { get; init; } = 0.34f;

    /// <summary>Score a partir duquel une offre devient Difficile.</summary>
    public float Hard { get; init; } = 0.67f;

    /// <summary>Part de la densite de zombies de la zone dans le score. Le reste vient de la charge.</summary>
    public float ZoneWeight { get; init; } = 0.5f;

    public MissionDifficulty Of(float score)
    {
        if (score >= Hard) return MissionDifficulty.Hard;
        return score >= Medium ? MissionDifficulty.Medium : MissionDifficulty.Easy;
    }
}

/// <summary>Bornes d'une exigence <see cref="MissionObjectiveKind.CollectObjects"/>.</summary>
public sealed class ObjectRequirementBounds
{
    public int MinDistinctItems { get; init; } = 1;
    public int MaxDistinctItems { get; init; } = 3;
    public int MinQuantity { get; init; } = 1;
    public int MaxQuantity { get; init; } = 2;
}

/// <summary>Bornes d'une exigence <see cref="MissionObjectiveKind.CollectPriorityResources"/>.</summary>
public sealed class ResourceRequirementBounds
{
    public int MinDistinctResources { get; init; } = 1;
    public int MaxDistinctResources { get; init; } = 2;
    public int MinQuantity { get; init; } = 1;
    public int MaxQuantity { get; init; } = 3;
}

/// <summary>
/// Ce qu'une difficulte rapporte. <see cref="Guaranteed"/> est une liste d'<b>alternatives</b> — on
/// en tire une —, <see cref="Bonus"/> une liste de <b>suppléments</b> tires chacun a
/// <see cref="BonusChance"/>. C'est exactement la lecture de la table du livrable 16 : « 1 medikit
/// ou 1 objet rare » d'un cote, « 1 medikit et 1 objet rare » de l'autre.
/// </summary>
public sealed class RewardTable
{
    public List<RewardCategory> Guaranteed { get; init; } = [];

    public List<RewardCategory> Bonus { get; init; } = [];

    public float BonusChance { get; init; } = 0.5f;
}

/// <summary>
/// L'offre de secours. Elle est ecrite en entier — type, zone, exigences, difficulte — parce qu'une
/// offre de secours calculee serait exposee aux memes echecs que celles qu'elle remplace.
/// </summary>
public sealed class MissionFallbackProfile
{
    public MissionObjectiveKind Type { get; init; } = MissionObjectiveKind.CollectPriorityResources;

    public string TargetZone { get; init; } = string.Empty;

    public MissionDifficulty Difficulty { get; init; } = MissionDifficulty.Easy;

    public int Zombies { get; init; }

    public List<ItemRequirement> Items { get; init; } = [];

    public List<ResourceRequirement> Resources { get; init; } = [];

    public MissionRequirements ToRequirements()
        => new() { Zombies = Zombies, Items = [.. Items], Resources = [.. Resources] };
}

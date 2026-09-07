using Hordinary.Game;

namespace Hordinary.World.MapGeneration;

/// <summary>
/// Le quartier en cours de construction : ce qu'une passe recoit, et ce qu'elle laisse a la suivante.
///
/// Il est mutable et les passes se le transmettent en l'etat. C'est ce qui rend chacune testable
/// isolement : on peut poser une trame a la main, appeler <see cref="RoadPass"/> seule, et regarder
/// ce qu'elle a fait — sans avoir a rejouer les cinq autres.
/// </summary>
public sealed class DistrictDraft
{
    public DistrictDraft(MapGenConfig config, MapProfile profile, DistrictRequest request, int seed)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Seed = seed;

        var rng = new DistrictRandom(seed, "border");

        BeltThickness = Math.Max(1, rng.Pick(config.Border.Thickness, "thickness"));
        Border = config.Border.Find(rng.Choose(profile.BorderKinds, "kind"))
            ?? throw new InvalidOperationException(
                $"Le profil « {profile.Id} » n'a aucune ceinture connue de config/mapgen.json.");

        int side = profile.Size + 2 * BeltThickness;

        // La grille entiere, ceinture comprise : c'est elle que la navigation, la perception et la
        // camera connaissent. Le quartier jouable est le rectangle qu'elle contient.
        Map = new GridMap(side, side, GridPosition.Zero, CellType.Border);
        Playable = Rect.FromBounds(
            BeltThickness, BeltThickness,
            BeltThickness + profile.Size - 1, BeltThickness + profile.Size - 1);
    }

    public MapGenConfig Config { get; }
    public MapProfile Profile { get; }
    public DistrictRequest Request { get; }
    public int Seed { get; }

    /// <summary>La mission que ce quartier doit rendre jouable. Seule la passe 4 la lit.</summary>
    public MissionDefinition Mission => Request.Mission;

    /// <summary>Taille de horde demandee. Elle vient de la mission, ou a defaut de la configuration.</summary>
    public int ZombieCount => Request.ZombieCount;

    /// <summary>Distance minimale d'apparition d'un zombie, en cases. Regle de <c>game_config.json</c>.</summary>
    public int MinimumSpawnDistance => Request.MinimumSpawnDistance;

    public GridMap Map { get; }

    /// <summary>Le quartier jouable : tout s'y passe, et rien ne se passe ailleurs.</summary>
    public Rect Playable { get; }

    public int BeltThickness { get; }

    public BorderKind Border { get; }

    /// <summary>Le bord qui porte la route d'arrivee. Pose en premier : tout le reste en depend.</summary>
    public Side DropSide { get; set; }

    /// <summary>La case ou le groupe met pied a terre. C'est le centre praticable du van.</summary>
    public GridPosition Drop { get; set; }

    /// <summary>L'empreinte de 2 x 3 cases du van (livrable 12), sur la voie d'arrivee.</summary>
    public Rect Van { get; set; }

    public List<Rect> Blocks { get; } = [];

    public List<RoadStrip> Roads { get; } = [];

    public List<Building> Buildings { get; } = [];

    /// <summary>Le batiment que la passe 4 a designe pour porter l'objectif de la mission.</summary>
    public Building? Objective { get; set; }

    public List<GridPosition> ZombieSpawns { get; } = [];

    public List<DistrictItem> Items { get; } = [];

    public List<HeavyObject> Heavy { get; } = [];

    /// <summary>Les cases de ceinture eclaircies, par ou la horde se renforce. Jamais praticables.</summary>
    public List<GridPosition> Breach { get; } = [];

    /// <summary>Vrai si la case est dans le quartier jouable, ceinture exclue.</summary>
    public bool IsPlayable(GridPosition p) => Playable.Contains(p);

    /// <summary>Fige le brouillon. Ce qui sort d'ici ne bouge plus.</summary>
    public GeneratedDistrict Freeze(int attempts) => new()
    {
        Map = Map,
        Seed = Seed,
        ProfileId = Profile.Id,
        Border = Border,
        BeltThickness = BeltThickness,
        Playable = Playable,
        Drop = Drop,
        Van = Van,
        Objective = Objective?.Zone,
        ZombieSpawns = [.. ZombieSpawns],
        Items = [.. Items],
        Heavy = [.. Heavy],
        Breach = [.. Breach],
        Attempts = attempts,
    };
}

/// <summary>
/// Un quartier accepte. Il produit exactement les donnees que <c>MapLoader</c> charge aujourd'hui —
/// une <see cref="GridMap"/> de <see cref="CellType"/>, des <see cref="MapZone"/>, des portes, des
/// fenetres, des points d'apparition — et rien en aval ne doit savoir si la carte a ete dessinee ou
/// generee.
/// </summary>
public sealed record GeneratedDistrict
{
    public required GridMap Map { get; init; }

    /// <summary>La graine qui a produit ce quartier : celle du premier essai accepte, pas celle demandee.</summary>
    public required int Seed { get; init; }

    public required string ProfileId { get; init; }

    public required BorderKind Border { get; init; }

    public required int BeltThickness { get; init; }

    public required Rect Playable { get; init; }

    public required GridPosition Drop { get; init; }

    public required Rect Van { get; init; }

    public MapZone? Objective { get; init; }

    public required IReadOnlyList<GridPosition> ZombieSpawns { get; init; }

    public required IReadOnlyList<DistrictItem> Items { get; init; }

    public required IReadOnlyList<HeavyObject> Heavy { get; init; }

    public required IReadOnlyList<GridPosition> Breach { get; init; }

    /// <summary>Nombre de quartiers essayes, celui-ci compris. Au-dela de 1, des cartes ont ete rejetees.</summary>
    public required int Attempts { get; init; }

    /// <summary>Les cases de ceinture. Elles sont connues des survivants des le depart.</summary>
    public IEnumerable<GridPosition> BorderCells
        => Map.AllPositions().Where(cell => Map.GetCell(cell) == CellType.Border);

    public override string ToString()
        => $"Quartier {ProfileId} graine {Seed} — {Map.Width} x {Map.Height}, "
         + $"ceinture {Border.Id} de {BeltThickness} cases, {Items.Count} objet(s), "
         + $"{ZombieSpawns.Count} zombie(s)";
}

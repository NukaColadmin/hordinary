using System.Text.Json;
using Hordinary.Config;

namespace Hordinary.World.MapGeneration;

/// <summary>
/// Reglages de generation de quartiers, charges depuis <c>config/mapgen.json</c> (livrable 13).
///
/// Un profil ne contient que des intervalles et des probabilites : ajouter une ambiance est une
/// entree de plus dans le fichier, jamais du code. C'est la meme regle que pour les missions, les
/// survivants et les zones — le generateur ne nomme aucun quartier.
/// </summary>
public sealed record MapGenConfig
{
    /// <summary>Nombre de quartiers essayes avant d'abandonner. Chaque echec repart a <c>seed + 1</c>.</summary>
    public int MaxGenerationAttempts { get; init; } = 8;

    public BorderSettings Border { get; init; } = new();

    public NamingSettings Naming { get; init; } = new();

    public List<MapProfile> Profiles { get; init; } = [];

    public static MapGenConfig Empty { get; } = new();

    public MapProfile? Find(string? id)
        => id is null
            ? null
            : Profiles.FirstOrDefault(profile => profile.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static MapGenConfig FromJson(string json)
    {
        var config = JsonSerializer.Deserialize<MapGenConfig>(json, GameConfig.JsonOptions) ?? Empty;
        config.Validate();
        return config;
    }

    /// <summary>
    /// Un fichier fautif doit se voir au chargement. Un profil qui demande un quartier de douze cases
    /// ou une ceinture plus epaisse que le quartier ne produirait pas une carte laide : il produirait
    /// huit rejets puis une exception, loin de sa cause.
    /// </summary>
    public void Validate()
    {
        if (MaxGenerationAttempts <= 0)
            throw new InvalidOperationException("max_generation_attempts doit valoir au moins 1.");

        Border.Validate();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Id))
                throw new InvalidOperationException("Un profil de carte sans identifiant a ete declare.");

            if (!seen.Add(profile.Id))
                throw new InvalidOperationException($"Le profil de carte « {profile.Id} » est declare deux fois.");

            if (profile.Size < MapProfile.MinimumSize)
            {
                throw new InvalidOperationException(
                    $"Le profil « {profile.Id} » demande un quartier de {profile.Size} cases de cote : "
                  + $"il en faut au moins {MapProfile.MinimumSize} pour y poser une rue et deux batiments.");
            }

            if (profile.BorderKinds.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Le profil « {profile.Id} » n'autorise aucune ceinture : son quartier n'aurait pas de bord.");
            }

            foreach (string kind in profile.BorderKinds)
            {
                if (Border.Find(kind) is null)
                {
                    throw new InvalidOperationException(
                        $"Le profil « {profile.Id} » demande la ceinture « {kind} », "
                      + "qui n'est declaree dans aucune entree de border.kinds.");
                }
            }

            if (profile.WalkableRatio.Min <= 0f || profile.WalkableRatio.Max > 1f)
            {
                throw new InvalidOperationException(
                    $"Le profil « {profile.Id} » attend une proportion praticable hors de l'intervalle 0-1.");
            }

            if (profile.MinimumFootprint is not { } min || profile.MaximumFootprint is not { } max)
            {
                throw new InvalidOperationException(
                    $"L'empreinte de batiment de « {profile.Id} » s'ecrit [[x1, y1], [x2, y2]].");
            }

            if (min.Width > max.Width || min.Height > max.Height)
            {
                throw new InvalidOperationException(
                    $"L'empreinte de batiment de « {profile.Id} » a un minimum plus grand que son maximum.");
            }
        }
    }
}

/// <summary>
/// La ceinture : la ou le quartier s'arrete.
///
/// Une seule couche, et c'est tout le propos. La specification en decrivait quatre — ceinture,
/// decor, fondu, et six familles de tuiles raccordables avec leurs terminaisons de rue — pour
/// resoudre un probleme que le brouillard resout d'un coup : il est visible, il est infranchissable,
/// il dit de lui-meme pourquoi on ne va pas plus loin, et il <b>remplace</b> ce qu'il y a derriere au
/// lieu de le cacher. Il n'y a donc ni bande de decor a rendre, ni fondu a calculer, ni terminaison
/// de rue a raccorder sur deux voisins differents.
/// </summary>
public sealed record BorderSettings
{
    /// <summary>Epaisseur du brouillard, en cases. C'est de la carte : elle entre dans la grille.</summary>
    public RollRange Thickness { get; init; } = new(4f, 8f);

    /// <summary>
    /// Probabilite qu'un quartier ait une breche : un endroit ou le brouillard s'eclaircit. Elle
    /// n'est pas franchissable par les survivants — c'est par la que les renforts de horde entrent.
    /// Le bord cesse d'etre une limite pour devenir une menace, ce qui est la meilleure facon de
    /// faire oublier que c'en est une.
    /// </summary>
    public float BreachChance { get; init; } = 0.5f;

    /// <summary>Largeur de la breche, en cases.</summary>
    public RollRange BreachWidth { get; init; } = new(2f, 4f);

    public List<BorderKind> Kinds { get; init; } = [];

    public BorderKind? Find(string? id)
        => id is null
            ? null
            : Kinds.FirstOrDefault(kind => kind.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public void Validate()
    {
        if (Thickness.Min < 1f)
            throw new InvalidOperationException("Une ceinture de moins d'une case ne ferme rien.");

        if (BreachChance is < 0f or > 1f)
            throw new InvalidOperationException("border.breach_chance sort de l'intervalle 0-1.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kind in Kinds)
        {
            if (string.IsNullOrWhiteSpace(kind.Id))
                throw new InvalidOperationException("Une ceinture sans identifiant a ete declaree.");

            if (!seen.Add(kind.Id))
                throw new InvalidOperationException($"La ceinture « {kind.Id} » est declaree deux fois.");

            if (!TileLegend.TryParseKey(kind.Tile, out _))
            {
                throw new InvalidOperationException(
                    $"La ceinture « {kind.Id} » designe la tuile « {kind.Tile} » : "
                  + "une cle de legende s'ecrit « source:x,y ».");
            }
        }
    }
}

/// <summary>
/// Une facon de dessiner la ceinture. Les deux livrees sont deux images d'une meme regle, et c'est
/// exactement ce qu'on veut : changer le brouillard ordinaire pour le brouillard radioactif ne
/// change rien au jeu, seulement ce que le joueur comprend de l'endroit ou il est.
/// </summary>
public sealed record BorderKind
{
    public string Id { get; init; } = string.Empty;

    /// <summary>Nom lisible, affiche dans l'ecran de preparation avec la graine.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Cle de <c>config/tile_legend.json</c>, au format <c>"source:x,y"</c>.</summary>
    public string Tile { get; init; } = string.Empty;

    /// <summary>Faux pour une ceinture qu'on ne perce jamais.</summary>
    public bool AllowsBreach { get; init; } = true;

    public override string ToString() => $"{Name} [{Id}]";
}

/// <summary>
/// Comment le generateur nomme ce qu'il pose. Aucun nom de lieu n'est ecrit dans le code : ce sont
/// des gabarits, et <c>{0}</c> y recoit un point cardinal ou un numero.
/// </summary>
public sealed record NamingSettings
{
    public string Drop { get; init; } = "Point de depose";
    public string Extraction { get; init; } = "Point de depose (retour)";
    public string Building { get; init; } = "Batiment {0}";
    public string MainRoad { get; init; } = "Rue principale";
    public string Street { get; init; } = "Rue {0}";
    public string Alley { get; init; } = "Ruelle {0}";

    /// <summary>Les qualificatifs employes par les gabarits, dans l'ordre ou ils sont consommes.</summary>
    public List<string> Cardinals { get; init; } = ["nord", "est", "sud", "ouest", "centre"];

    /// <summary>Le n-ieme qualificatif, puis des numeros quand la liste est epuisee.</summary>
    public string Qualifier(int index)
        => index < Cardinals.Count ? Cardinals[index] : (index - Cardinals.Count + 2).ToString();
}

/// <summary>
/// Une ambiance de quartier. Trois formats sont livres, choisis par la mission via
/// <c>map_profile</c> : le petit tient une rue et deux batiments, le grand en aligne plusieurs.
/// </summary>
public sealed record MapProfile
{
    /// <summary>En dessous, il n'y a pas la place d'une rue, de deux batiments et d'un van.</summary>
    public const int MinimumSize = 40;

    public string Id { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    /// <summary>Cote du <b>quartier jouable</b>, en cases. La ceinture s'ajoute autour.</summary>
    public int Size { get; init; } = 120;

    /// <summary>Nombre d'ilots decoupes par la voirie.</summary>
    public RollRange Blocks { get; init; } = new(4f, 6f);

    public RoadSettings Roads { get; init; } = new();

    public BuildingSettings Buildings { get; init; } = new();

    public DoorSettings Doors { get; init; } = new();

    /// <summary>Ceintures credibles pour cette ambiance, par identifiant de <c>border.kinds</c>.</summary>
    public List<string> BorderKinds { get; init; } = [];

    /// <summary>Distance depose -> objectif acceptee : ni collee au van, ni au bord oppose.</summary>
    public RollRange ObjectiveDistance { get; init; } = new(35f, 80f);

    /// <summary>Proportion de cellules praticables du quartier jouable : ni labyrinthe, ni terrain vague.</summary>
    public RollRange WalkableRatio { get; init; } = new(0.35f, 0.6f);

    public PropSettings Props { get; init; } = new();

    public override string ToString() => $"{Id} ({Size} x {Size})";

    /// <summary>Empreinte minimale d'un batiment, <c>[[x1, y1], ...]</c>.</summary>
    public (int Width, int Height)? MinimumFootprint => Corner(0);

    /// <summary>Empreinte maximale d'un batiment, <c>[..., [x2, y2]]</c>.</summary>
    public (int Width, int Height)? MaximumFootprint => Corner(1);

    private (int Width, int Height)? Corner(int index)
    {
        var footprint = Buildings.Footprint;
        if (footprint.Length != 2 || footprint[index].Length < 2) return null;

        return (footprint[index][0], footprint[index][1]);
    }
}

/// <summary>La voirie : ce qui separe les ilots, et ce qui garantit qu'on passe partout.</summary>
public sealed record RoadSettings
{
    /// <summary>Largeur de la rue traversante, celle qui porte le van.</summary>
    public RollRange MainWidth { get; init; } = new(4f, 6f);

    /// <summary>Largeur des rues secondaires, celles que produit le decoupage en ilots.</summary>
    public RollRange StreetWidth { get; init; } = new(2f, 3f);

    /// <summary>Probabilite qu'un ilot soit fendu par une ruelle d'une case.</summary>
    public float AlleyChance { get; init; } = 0.35f;
}

/// <summary>Les batiments : un par ilot, et ce qu'il y a dedans.</summary>
public sealed record BuildingSettings
{
    /// <summary>Part de l'ilot que le batiment occupe. Le reste est de la rue et du trottoir.</summary>
    public RollRange Density { get; init; } = new(0.45f, 0.65f);

    /// <summary>Empreinte minimale et maximale, <c>[[x1, y1], [x2, y2]]</c>.</summary>
    public int[][] Footprint { get; init; } = [[6, 8], [18, 22]];

    /// <summary>Nombre de pieces obtenues par decoupage binaire recursif.</summary>
    public RollRange Rooms { get; init; } = new(2f, 6f);
}

/// <summary>Ce par quoi on entre, et ce qui reste ferme une fois qu'on est entre.</summary>
public sealed record DoorSettings
{
    public RollRange PerBuilding { get; init; } = new(1f, 3f);

    public float LockedChance { get; init; } = 0.35f;

    /// <summary>Probabilite qu'un pan de facade porte une fenetre. Une fenetre est un second acces.</summary>
    public float WindowChance { get; init; } = 0.6f;
}

/// <summary>Ce qu'on seme : les meubles poussables du livrable 11, et les objets au sol.</summary>
public sealed record PropSettings
{
    public RollRange HeavyPerBlock { get; init; } = new(2f, 6f);

    public RollRange GroundItemsPerBuilding { get; init; } = new(0f, 3f);

    /// <summary>Ce qu'on trouve par terre, par identifiant du catalogue d'objets.</summary>
    public List<string> GroundItems { get; init; } = [];

    /// <summary>Les meubles poussables disponibles. Vide, la passe 5 n'en pose aucun.</summary>
    public List<HeavyKind> HeavyKinds { get; init; } = [];
}

/// <summary>Un meuble poussable disponible a la generation : sa categorie, sa masse et sa taille.</summary>
public sealed record HeavyKind
{
    public string Kind { get; init; } = string.Empty;

    /// <summary>De 1 a 5. Au-dela de <see cref="HeavyObjects.SoloMassLimit"/>, il faut etre deux.</summary>
    public float Mass { get; init; } = 2f;

    /// <summary>Vrai s'il a sa place dans la rue plutot que dans un batiment.</summary>
    public bool Outdoor { get; init; }

    /// <summary>
    /// Encombrement, en cases : la plus grande dimension d'abord. Une epave de voiture fait
    /// <c>2 x 2</c> — quatre cases, l'echelle d'une voiture au milieu d'une rue large de quatre a
    /// six. Un rayonnage garde le <c>1 x 1</c> par defaut.
    ///
    /// L'orientation n'est pas ecrite ici : la passe de peuplement essaie les deux et retient celle
    /// qui tient dans la rue. Une voiture en travers d'une ruelle n'a rien d'absurde ; une voiture
    /// qui deborde sur un mur, si.
    /// </summary>
    public int Length { get; init; } = 1;

    public int Breadth { get; init; } = 1;
}

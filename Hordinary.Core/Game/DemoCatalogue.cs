using System.Text.Json;
using Hordinary.Agents;
using Hordinary.Config;
using Hordinary.World;
using Hordinary.World.MapGeneration;

namespace Hordinary.Game;

/// <summary>
/// Le mode demonstration, section <c>demo</c> de <c>config/game_config.json</c>.
///
/// A <c>true</c>, l'ecran de camp cede la place au choix d'une grille et d'un scenario d'essai : ni
/// reserves, ni offres, ni journee qui passe. Le mode sert a filmer, et une prise ne doit dependre de
/// rien de ce que la precedente a consomme.
/// </summary>
public sealed record DemoSettings
{
    public bool Enabled { get; init; }

    /// <summary>
    /// Scenario lance sans passer par l'ecran de choix. C'est ce qui rend un scenario jouable en
    /// headless, ou personne ne clique — et c'est la seule facon d'en verifier un dans le moteur.
    /// </summary>
    public string? Autostart { get; init; }

    /// <summary>
    /// Le plafond de temps du planificateur pendant une prise, en millisecondes ; 0 garde celui du
    /// jeu (<c>planner.max_time_ms</c>). Le harnais (<c>Tests/DemoRun.cs</c>) lit la meme valeur.
    ///
    /// <c>planner.max_time_ms</c> est une horloge : il protege la cadence d'une partie, et c'est le
    /// livrable 5 qui le fixe a 50 ms. Une prise n'est pas une partie mais une scene tournee, qui doit
    /// se rejouer a l'identique — et sous trois consignes (<c>grande_manoeuvre</c>, depuis retiree), une
    /// recherche sur la grille la plus simple depassait deja ces 50 ms : le plan dependait alors de la vitesse de la
    /// machine. Le harnais desserrait son horloge a lui seul, en supposant que le jeu ne toucherait
    /// jamais ce plafond ; il donnait raison a une prise que le moteur ratait. Le plafond de noeuds
    /// (<c>planner.max_nodes</c>) reste la borne, et elle est deterministe.
    /// </summary>
    public int PlannerMaxTimeMs { get; init; }

    /// <summary>Les limites du planificateur pendant une prise : celles du jeu, horloge desserree si demande.</summary>
    public PlannerSettings PlannerFor(PlannerSettings game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return PlannerMaxTimeMs > game.MaxTimeMs ? game with { MaxTimeMs = PlannerMaxTimeMs } : game;
    }
}

/// <summary>
/// Ce qu'un scenario de demonstration met en scene : une consigne du joueur (livrable 15), ou un
/// geste que le survivant fait de lui-meme.
/// </summary>
public enum DemoFocus
{
    /// <summary>Une contrainte PDDL : le clip se regarde avec et sans elle.</summary>
    Constraint = 0,

    /// <summary>Un comportement du survivant : combat, partage, fouille, soin, fuite.</summary>
    Action,

    /// <summary>
    /// Ce qu'un survivant fait pour un autre, ou grace a lui : preter, soigner, faire diversion, se
    /// parler de loin, rentrer ensemble. Ajoute en dernier pour ne pas renumeroter les deux autres.
    /// Un scenario de cooperation peut porter une consigne — la plupart de ces gestes n'ont pas
    /// d'autre demandeur — et se regarde alors avec et sans elle, comme un scenario de consigne.
    /// </summary>
    Cooperation,

    /// <summary>
    /// Carte blanche : le terrain, le groupe et la horde sont ecrits, les consignes non. C'est le
    /// joueur qui les donne a l'ecran de demonstration — en francais quand le service de langage est
    /// actif, dans la grammaire fermee sinon — et elles passent les memes portillons qu'au camp.
    ///
    /// Le fichier ne peut donc rien promettre de ce que la prise montrera : l'attente ecrite est celle
    /// du groupe <b>livre a lui-meme</b>, et c'est elle que la suite verifie. Une prise jouee sous les
    /// consignes du joueur n'a pas d'attente — son bilan dit ce qui s'est passe, pas s'il fallait que
    /// cela se passe. Ajoute en dernier pour ne pas renumeroter les trois autres.
    /// </summary>
    Strategy,

    /// <summary>
    /// Plusieurs consignes a la fois, et ce qu'elles font ensemble : s'armer <b>puis</b> faire
    /// diversion, soigner <b>et</b> armer, fouiller <b>avant</b> de rappeler. Chacune a deja sa prise
    /// seule ; celle-ci montre que le planificateur les tient toutes dans un meme plan, dans l'ordre
    /// que le groupe trouve, et non l'une apres l'autre. C'est la forme qu'a une vraie strategie de
    /// joueur — une consigne seule est un clip, pas un plan. Ajoute en dernier pour ne pas renumeroter
    /// les quatre autres.
    /// </summary>
    Combination,
}

/// <summary>Une lettre de la couche des lieux d'une grille : le nom et la categorie du lieu.</summary>
public sealed record DemoZone
{
    public string Name { get; init; } = string.Empty;

    public ZoneKind Kind { get; init; } = ZoneKind.Other;

    public string? Profile { get; init; }
}

/// <summary>
/// Une grille de demonstration : un petit terrain ecrit a la main, a l'oeil, pour qu'une situation
/// precise s'y produise a coup sur.
///
/// <para>
/// Deux couches de meme taille. <see cref="Terrain"/> reprend les lettres des cartes de test et de
/// <see cref="DistrictExport"/> (<c>.</c> sol, <c>#</c> mur, <c>D</c> porte, <c>L</c> verrouillee,
/// <c>W</c> fenetre, <c>O</c> obstacle…), plus des <b>reperes</b> — un chiffre ou une minuscule autre
/// que <c>o</c> — qui sont du sol et que les scenarios citent pour poser un survivant, un zombie ou un
/// objet. <see cref="Zones"/> ecrit une lettre par case, que <see cref="Legend"/> traduit en lieu ; un
/// point n'appartient a aucun lieu.
/// </para>
///
/// <para>
/// Les lieux sont dessines plutot que donnes en coordonnees parce que c'est la ou une grille ecrite a
/// la main se trompe : un rectangle decale d'une case fait sortir la porte du batiment, et rien ne le
/// montre. Dessine, il se voit — et <see cref="DemoCatalogue.Validate"/> refuse un lieu qui n'est pas
/// un rectangle plein, ce qui attrape la faute de frappe.
/// </para>
/// </summary>
public sealed class DemoGrid
{
    /// <summary>
    /// Epaisseur de la ceinture de brouillard ajoutee autour de la grille. Elle n'est pas ecrite dans
    /// le fichier : c'est la meme pour toutes, et la dessiner a la main ne ferait qu'ajouter des lignes
    /// a recompter.
    /// </summary>
    public const int Belt = 2;

    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public List<string> Terrain { get; init; } = [];

    public List<string> Zones { get; init; } = [];

    public Dictionary<string, DemoZone> Legend { get; init; } = [];

    public int Width => Terrain.Count == 0 ? 0 : Terrain[0].Length;

    public int Height => Terrain.Count;

    /// <summary>Vrai si le caractere est un repere, et non un type de case.</summary>
    public static bool IsAnchor(char glyph) => char.IsAsciiDigit(glyph) || (char.IsAsciiLetterLower(glyph) && glyph != 'o');

    /// <summary>Les reperes de la grille, en coordonnees de la carte construite (ceinture comprise).</summary>
    public IReadOnlyDictionary<string, GridPosition> Anchors()
    {
        var anchors = new Dictionary<string, GridPosition>(StringComparer.Ordinal);

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Terrain[y].Length; x++)
            {
                if (IsAnchor(Terrain[y][x])) anchors[Terrain[y][x].ToString()] = Cell(x, y);
            }
        }

        return anchors;
    }

    /// <summary>
    /// Les lieux, en coordonnees de la carte construite : le rectangle qui englobe chaque lettre de la
    /// couche des lieux.
    /// </summary>
    public IReadOnlyList<MapZone> Places()
    {
        var places = new List<MapZone>();

        foreach (var (letter, zone) in Legend.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (Bounds(letter) is not { } bounds) continue;

            places.Add(new MapZone(zone.Name, zone.Kind, bounds.Min, bounds.Max, zone.Profile));
        }

        return places;
    }

    /// <summary>
    /// Construit le terrain. Le resultat est un <see cref="GeneratedDistrict"/>, comme un quartier du
    /// livrable 13 — ce qui fait que le jeu le peint, le peuple et le joue par le chemin qui existe
    /// deja, sans apprendre qu'une grille de demonstration existe.
    /// </summary>
    public GeneratedDistrict Build(BorderKind border, int seed)
    {
        ArgumentNullException.ThrowIfNull(border);

        var map = new GridMap(Width + (2 * Belt), Height + (2 * Belt), GridPosition.Zero, CellType.Border);

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                char glyph = Terrain[y][x];
                map.SetCell(Cell(x, y), IsAnchor(glyph) ? CellType.Floor : DistrictExport.CellOf(glyph));
            }
        }

        var places = Places();
        MapZone? van = null;

        foreach (var place in places)
        {
            map.AddZone(place);

            if (place.Kind == ZoneKind.Start)
            {
                // Comme la depose d'un quartier genere : le depart et le retour sont le meme
                // endroit, sous deux noms, parce que le domaine connait la sortie sous le nom
                // « extraction » et le briefing sous le nom « start ».
                van = place;
                var extraction = new MapZone($"{place.Name} (retour)", ZoneKind.Extraction, place.Min, place.Max);
                map.AddZone(extraction);
                Stamp(map, extraction, CellType.Extraction);
                continue;
            }

            if (place.Kind == ZoneKind.Pharmacy) Stamp(map, place, CellType.Pharmacy);

            // Un batiment est un interieur, murs compris : c'est ce qui donne au peintre son
            // carrelage et a la perception la distinction dedans / dehors.
            if (!place.Kind.IsOpenAir())
            {
                foreach (var cell in place.Positions()) map.SetIndoor(cell, true);
            }
        }

        if (van is null) throw new InvalidOperationException($"La grille « {Id} » n'a pas de van (lieu de type Start).");

        return new GeneratedDistrict
        {
            Map = map,
            Seed = seed,
            ProfileId = $"demo:{Id}",
            Border = border,
            BeltThickness = Belt,
            Playable = Rect.FromBounds(Belt, Belt, Belt + Width - 1, Belt + Height - 1),
            Drop = van.Center,
            Van = new Rect(van.Min, van.Max),
            ZombieSpawns = [],
            Items = [],
            Heavy = [],
            Breach = [],
            Attempts = 1,
        };
    }

    /// <summary>Le rectangle couvert par une lettre de la couche des lieux, ou <c>null</c> si elle n'y figure pas.</summary>
    internal Rect? Bounds(string letter)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;

        for (int y = 0; y < Zones.Count; y++)
        {
            for (int x = 0; x < Zones[y].Length; x++)
            {
                if (Zones[y][x].ToString() != letter) continue;

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        if (minX == int.MaxValue) return null;

        return new Rect(Cell(minX, minY), Cell(maxX, maxY));
    }

    /// <summary>Une case du dessin, en coordonnees de la carte : decalee de la ceinture.</summary>
    private static GridPosition Cell(int x, int y) => new(x + Belt, y + Belt);

    private static void Stamp(GridMap map, MapZone zone, CellType type)
    {
        foreach (var cell in zone.Positions())
        {
            if (map.GetCell(cell) is CellType.Floor) map.SetCell(cell, type);
        }
    }
}

/// <summary>Un survivant du scenario : qui, ou il commence, ce qu'il emporte, dans quel etat.</summary>
public sealed record DemoMember
{
    /// <summary>Identifiant d'un profil de <c>config/survivors.json</c>.</summary>
    public string Survivor { get; init; } = string.Empty;

    /// <summary>Repere de depart. Absent, le survivant descend du van comme dans une mission.</summary>
    public string? At { get; init; }

    /// <summary>Ce qu'il porte au depart. Une arme part en main, comme un paquetage du camp.</summary>
    public List<string> Carry { get; init; } = [];

    /// <summary>Points de vie au depart. Absent, il part en pleine forme.</summary>
    public float? Health { get; init; }
}

/// <summary>Un objet pose au sol, sur un repere.</summary>
public sealed record DemoItem
{
    public string Item { get; init; } = string.Empty;

    public string At { get; init; } = string.Empty;
}

/// <summary>Ce que le groupe vient chercher, et ou.</summary>
public sealed record DemoObjective
{
    public string Item { get; init; } = Survivor.DefaultObjectiveItem;

    /// <summary>Nom du lieu vise, tel que la legende de la grille l'ecrit.</summary>
    public string Zone { get; init; } = string.Empty;

    /// <summary>
    /// Reperes ou poser un exemplaire. Il peut etre hors du lieu vise : c'est ce qui fait qu'un objet
    /// <b>apercu</b> dans la rue se ramasse sans fouiller (<c>PickUpSpottedObjective</c>).
    /// </summary>
    public List<string> At { get; init; } = [];
}

/// <summary>
/// Ce que le clip doit montrer, sous la forme ou un test peut le verifier.
///
/// Chaque entree est un nom d'action du domaine (<c>ForceDoor</c>, <c>AttackWithWeapon</c>…) — une
/// action scindee par une consigne porte le nom que le compilateur lui donne (<c>EnterBuilding</c>) —
/// ou l'un des evenements
/// <see cref="DemoEvents"/>. Les attentes sont lues par <c>Tests/DemoScenarioTests.cs</c> dans le
/// fichier livre : un scenario qui cesse de produire ce qu'il annonce fait echouer la suite, au lieu
/// de donner un clip ou il ne se passe rien.
/// </summary>
public sealed record DemoExpectation
{
    /// <summary>Le scenario tel que livre, consignes comprises.</summary>
    public DemoOutcome With { get; init; } = new();

    /// <summary>
    /// Le meme scenario, consignes retirees : le contraste que le clip d'une consigne filme. Vide pour
    /// un scenario d'action, qui n'a rien a retirer.
    /// </summary>
    public DemoOutcome Without { get; init; } = new();

    public bool HasContrast => Without.Shows.Count > 0 || Without.Never.Count > 0;
}

/// <summary>Ce qui doit se produire au moins une fois, et ce qui ne doit jamais se produire.</summary>
public sealed record DemoOutcome
{
    public List<string> Shows { get; init; } = [];

    public List<string> Never { get; init; } = [];

    /// <summary>
    /// Les ecarts entre l'attente et ce qui s'est passe, un par ligne ; vide quand la prise tient sa
    /// promesse. Le jeu l'affiche au bilan, la suite de tests l'exige vide : c'est la meme lecture.
    /// </summary>
    public IReadOnlyList<string> Failures(IEnumerable<string> happened)
    {
        ArgumentNullException.ThrowIfNull(happened);

        var seen = new HashSet<string>(happened, StringComparer.OrdinalIgnoreCase);

        return
        [
            .. Shows.Where(name => !seen.Contains(name)).Select(name => $"{name} attendu, jamais survenu"),
            .. Never.Where(seen.Contains).Select(name => $"{name} interdit, survenu"),
        ];
    }

    public override string ToString()
        => (Shows.Count, Never.Count) switch
        {
            (_, 0) => string.Join(", ", Shows),
            (0, _) => $"jamais : {string.Join(", ", Never)}",
            _ => $"{string.Join(", ", Shows)} — jamais : {string.Join(", ", Never)}",
        };
}

/// <summary>Les evenements qu'une attente peut citer en plus des actions du domaine.</summary>
public static class DemoEvents
{
    /// <summary>Un survivant a appris quelque chose d'un autre (livrable 7).</summary>
    public const string Sharing = "Partage";

    /// <summary>Le van a rappele le groupe, faute de temps.</summary>
    public const string Recall = "Rappel";

    /// <summary>La mission s'est conclue sur une victoire.</summary>
    public const string Victory = "Victoire";

    /// <summary>Un zombie est tombe.</summary>
    public const string ZombieDown = "ZombieATerre";

    /// <summary>
    /// Deux survivants hors de portee de voix se sont parle par radio : l'echange n'aurait pas eu lieu
    /// sans les deux postes (<see cref="Agents.KnowledgeExchange.ByRadio"/>).
    /// </summary>
    public const string RadioSharing = "PartageRadio";

    /// <summary>Un zombie a mordu un survivant.</summary>
    public const string Bite = "Morsure";

    public static IReadOnlyList<string> All { get; } = [Sharing, Recall, Victory, ZombieDown, RadioSharing, Bite];

    /// <summary>
    /// Ce qu'une action terminee inscrit au deroule : son schema — <c>MoveToLocation</c> — et, quand
    /// elle en a, sa forme liee — <c>MoveToLocation(group)</c>. La seconde est ce qu'une attente cite
    /// quand le geste ne se distingue que par sa destination : rejoindre le groupe et aller a la
    /// pharmacie sont le meme schema. Le jeu et le harnais passent par ici tous les deux, pour que la
    /// meme prise s'y lise pareil.
    /// </summary>
    public static IEnumerable<string> Of(string schema, string grounded)
    {
        yield return schema;
        if (!grounded.Equals(schema, StringComparison.Ordinal)) yield return grounded;
    }

    /// <summary>
    /// Le deroule d'une prise, une entree par geste ou evenement, dans l'ordre de premiere apparition.
    /// C'est le bilan d'une carte blanche : sans attente ecrite, il n'y a rien a comparer, et le joueur
    /// juge l'effet de sa consigne sur ce qui s'est passe. Les formes liees sont ecartees — elles
    /// doublent leur schema, et la liste deviendrait illisible pour la seule destination d'un pas.
    /// </summary>
    public static IReadOnlyList<string> Chronicle(IEnumerable<string> happened)
    {
        ArgumentNullException.ThrowIfNull(happened);

        return [.. happened.Where(name => !name.Contains('(')).Distinct(StringComparer.OrdinalIgnoreCase)];
    }
}

/// <summary>Un scenario d'essai : une grille, un groupe, une horde placee a la main, et une consigne.</summary>
public sealed record DemoScenario
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>Identifiant de la grille jouee.</summary>
    public string Grid { get; init; } = string.Empty;

    public DemoFocus Focus { get; init; } = DemoFocus.Action;

    /// <summary>Ce que le scenario montre, en une ou deux phrases.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Ce qu'il faut regarder a l'ecran pendant la prise.</summary>
    public string? Watch { get; init; }

    public DemoObjective Objective { get; init; } = new();

    public List<DemoMember> Team { get; init; } = [];

    /// <summary>Reperes ou poser un zombie. Un repere repete en pose plusieurs.</summary>
    public List<string> Zombies { get; init; } = [];

    public List<DemoItem> Items { get; init; } = [];

    /// <summary>Contraintes dans la grammaire fermee du livrable 15, telles qu'un preset les ecrit.</summary>
    public List<string> Instructions { get; init; } = [];

    /// <summary>Temps imparti ; absent, celui de <c>mission.time_limit</c>.</summary>
    public double? TimeLimit { get; init; }

    /// <summary>
    /// La panique pour ce scenario, par-dessus <c>debug.panic_enabled</c>. Absente, le reglage de debug
    /// s'applique. Un combat a mains nues ou une fuite se tournent avec : la panique est une mecanique
    /// du survivant (livrable 8), et le reglage de debug qui la coupe sert a observer la
    /// planification seule — sans elle, un survivant desarme ne se retourne jamais contre un zombie.
    /// </summary>
    public bool? Panic { get; init; }

    /// <summary>Graine du placement du groupe et de la horde : une prise se refait a l'identique.</summary>
    public int Seed { get; init; } = 1;

    /// <summary>
    /// Le groupe part avec le plan du quartier (<see cref="Agents.SurvivorBand.KnowTerrain"/>) : le
    /// terrain et les lieux sont sus, les zombies et les objets non. Sans plan, une prise commence par
    /// une minute de reconnaissance qui n'est pas ce qu'elle filme — et pendant laquelle la horde a
    /// tout le temps de reperer le groupe.
    /// </summary>
    public bool Mapped { get; init; }

    public DemoExpectation Expect { get; init; } = new();

    /// <summary>Vrai si les consignes de la prise sont celles du joueur, et non celles du fichier.</summary>
    public bool TakesPlayerInstructions => Focus == DemoFocus.Strategy;

    /// <summary>
    /// L'attente qui s'applique a une prise : celle du scenario tel que livre, ou son contraste quand
    /// les consignes ont ete retirees. <c>null</c> quand on a retire des consignes dont le scenario ne
    /// dit pas ce qu'elles changent — il n'y a alors rien a verifier —, et pour une carte blanche jouee
    /// sous des consignes du joueur : le fichier ne sait pas ce qu'elles feront.
    /// </summary>
    /// <param name="playerInstructions">Le nombre de consignes donnees par le joueur a une carte blanche.</param>
    public DemoOutcome? Expected(bool withInstructions, int playerInstructions = 0)
    {
        if (TakesPlayerInstructions) return playerInstructions > 0 ? null : Expect.With;

        if (withInstructions || Instructions.Count == 0) return Expect.With;

        return Expect.HasContrast ? Expect.Without : null;
    }

    /// <summary>Ce que le camp de demonstration doit avoir en stock pour que chacun parte equipe.</summary>
    public IReadOnlyList<ItemRequirement> Storage()
        => [.. Team
            .SelectMany(member => member.Carry)
            .GroupBy(item => item, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ItemRequirement(group.Key, group.Count()))];
}

/// <summary>
/// Un scenario pret a jouer : le terrain construit et peuple, la mission qui en decoule, et les
/// departs. C'est ce que <c>GameRoot</c> et le harnais de test deroulent tous les deux — un scenario
/// ne se met en place qu'a un seul endroit.
/// </summary>
public sealed record DemoStage
{
    public required DemoScenario Scenario { get; init; }

    public required DemoGrid Grid { get; init; }

    public required GeneratedDistrict District { get; init; }

    public required MissionDefinition Mission { get; init; }

    /// <summary>Case de depart de chaque survivant qui en declare une.</summary>
    public required IReadOnlyDictionary<string, GridPosition> Starts { get; init; }

    public GridPosition? StartOf(string survivorId)
        => Starts.TryGetValue(survivorId, out var start) ? start : null;

    /// <summary>
    /// Le paquetage du scenario, compose sur un entrepot qui n'a que ca. Il passe par
    /// <see cref="CampLoadout"/> et non par un <c>AddToInventory</c> direct : un sac de demonstration
    /// obeit aux limites du vrai, et une arme confiee part en main par le meme chemin qu'au camp.
    /// </summary>
    public CampLoadout Loadout(ItemCatalogue catalogue, InventorySettings? limits = null)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        var stock = new CampStock(CampSettings.Default with { Storage = [.. Scenario.Storage()] });
        var loadout = new CampLoadout(stock, catalogue, limits);

        foreach (var member in Scenario.Team)
        {
            foreach (string item in member.Carry)
            {
                if (!loadout.Give(member.Survivor, item))
                {
                    throw new InvalidOperationException(
                        $"Scenario « {Scenario.Id} » : {member.Survivor} ne peut pas emporter {item} (sac plein ?).");
                }
            }
        }

        return loadout;
    }

    /// <summary>Met un survivant dans l'etat que le scenario annonce : blesse, s'il doit se soigner.</summary>
    public void Prepare(Survivor survivor)
    {
        ArgumentNullException.ThrowIfNull(survivor);

        var member = Scenario.Team.FirstOrDefault(
            candidate => candidate.Survivor.Equals(survivor.Id, StringComparison.OrdinalIgnoreCase));

        if (member?.Health is { } health && health < survivor.Health) survivor.TakeDamage(survivor.Health - health);
    }
}

/// <summary>
/// Les grilles et les scenarios du mode demonstration, charges depuis <c>config/demo_scenarios.json</c>.
///
/// Le mode existe pour tourner des clips : chaque scenario isole une consigne du joueur ou un geste
/// du survivant, sur un terrain dessine pour que ce geste-la se produise. Une mission du camp ne
/// garantit rien de tel — le quartier est tire, la horde aussi, et la vitre qu'on voulait filmer n'a
/// peut-etre jamais ete sur le chemin.
///
/// Rien de ce qui suit ne change les regles : un scenario est une <see cref="MissionDefinition"/>
/// jouee sur un <see cref="GeneratedDistrict"/>, par le meme groupe, le meme planificateur et le meme
/// suivi de partie qu'une mission. Seul le terrain et le placement sont ecrits a la main.
/// </summary>
public sealed class DemoCatalogue
{
    public List<DemoGrid> Grids { get; init; } = [];

    public List<DemoScenario> Scenarios { get; init; } = [];

    public static DemoCatalogue Empty { get; } = new();

    public DemoGrid? FindGrid(string? id)
        => Grids.FirstOrDefault(grid => grid.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public DemoScenario? Find(string? id)
        => Scenarios.FirstOrDefault(scenario => scenario.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Les scenarios joues sur une grille, dans l'ordre du fichier.</summary>
    public IReadOnlyList<DemoScenario> On(DemoGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        return [.. Scenarios.Where(scenario => scenario.Grid.Equals(grid.Id, StringComparison.OrdinalIgnoreCase))];
    }

    public static DemoCatalogue FromJson(string json)
    {
        var catalogue = JsonSerializer.Deserialize<DemoCatalogue>(json, GameConfig.JsonOptions) ?? Empty;
        catalogue.Validate();
        return catalogue;
    }

    /// <summary>
    /// Met un scenario en place : le terrain de sa grille, les objets et la horde sur leurs reperes,
    /// la mission qui en decoule.
    /// </summary>
    public DemoStage Stage(DemoScenario scenario, BorderKind border)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(border);

        var grid = FindGrid(scenario.Grid)
            ?? throw new InvalidOperationException($"Scenario « {scenario.Id} » : grille « {scenario.Grid} » inconnue.");

        var anchors = grid.Anchors();
        var district = grid.Build(border, scenario.Seed);

        var objective = district.Map.Zones.FirstOrDefault(
                zone => zone.Name.Equals(scenario.Objective.Zone, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Scenario « {scenario.Id} » : lieu « {scenario.Objective.Zone} » absent de la grille « {grid.Id} ».");

        var items = new List<DistrictItem>();
        items.AddRange(scenario.Objective.At.Select(at => new DistrictItem(scenario.Objective.Item, anchors[at])));
        items.AddRange(scenario.Items.Select(item => new DistrictItem(item.Item, anchors[item.At])));

        var mission = new MissionDefinition
        {
            Id = $"demo-{scenario.Id}",
            Name = scenario.Name,
            Description = scenario.Summary,
            Difficulty = scenario.Focus switch
            {
                DemoFocus.Constraint => "Consigne",
                DemoFocus.Cooperation => "Cooperation",
                DemoFocus.Strategy => "Carte blanche",
                DemoFocus.Combination => "Combinaison",
                _ => "Action",
            },
            Target = new ZoneTarget(objective.Kind, objective.Profile),
            ObjectiveZone = objective.Name,
            Item = scenario.Objective.Item,
            Supplies = Math.Max(1, scenario.Objective.At.Count),
            Camp = ZoneKind.Start,
            ZombieCount = scenario.Zombies.Count,
            RecommendedTeam = scenario.Team.Count,
            TimeLimit = scenario.TimeLimit,
        };

        return new DemoStage
        {
            Scenario = scenario,
            Grid = grid,
            District = district with
            {
                Objective = objective,
                Items = items,
                ZombieSpawns = [.. scenario.Zombies.Select(at => anchors[at])],
            },
            Mission = mission,
            Starts = scenario.Team
                .Where(member => member.At is not null)
                .ToDictionary(member => member.Survivor, member => anchors[member.At!], StringComparer.OrdinalIgnoreCase),
        };
    }

    /// <summary>
    /// Refuse au chargement ce qui ferait une prise ratee sans le dire : un repere cite mais absent,
    /// un lieu qui n'est pas un rectangle, un scenario sur une grille qui n'existe pas. Le catalogue
    /// d'objets et le roster sont facultatifs, pour que le fichier se valide aussi sans eux.
    /// </summary>
    public void Validate(ItemCatalogue? items = null, SurvivorRoster? roster = null)
    {
        var grids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var grid in Grids)
        {
            if (string.IsNullOrWhiteSpace(grid.Id)) throw new InvalidOperationException("Une grille sans identifiant a ete declaree.");
            if (!grids.Add(grid.Id)) throw new InvalidOperationException($"La grille « {grid.Id} » est declaree deux fois.");

            ValidateGrid(grid);
        }

        var scenarios = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var scenario in Scenarios)
        {
            if (string.IsNullOrWhiteSpace(scenario.Id)) throw new InvalidOperationException("Un scenario sans identifiant a ete declare.");
            if (!scenarios.Add(scenario.Id)) throw new InvalidOperationException($"Le scenario « {scenario.Id} » est declare deux fois.");

            ValidateScenario(scenario, items, roster);
        }
    }

    private static void ValidateGrid(DemoGrid grid)
    {
        string where = $"Grille « {grid.Id} »";

        if (grid.Height == 0 || grid.Width == 0) throw new InvalidOperationException($"{where} : terrain vide.");

        if (grid.Terrain.Any(row => row.Length != grid.Width))
            throw new InvalidOperationException($"{where} : toutes les lignes du terrain ont la meme longueur.");

        if (grid.Zones.Count != grid.Height || grid.Zones.Any(row => row.Length != grid.Width))
            throw new InvalidOperationException($"{where} : la couche des lieux a la taille du terrain ({grid.Width} x {grid.Height}).");

        foreach (string row in grid.Terrain)
        {
            foreach (char glyph in row.Where(glyph => !DemoGrid.IsAnchor(glyph)))
            {
                try
                {
                    DistrictExport.CellOf(glyph);
                }
                catch (InvalidOperationException exception)
                {
                    throw new InvalidOperationException($"{where} : {exception.Message}", exception);
                }
            }
        }

        var anchors = new HashSet<char>();

        foreach (char glyph in grid.Terrain.SelectMany(row => row).Where(DemoGrid.IsAnchor))
        {
            if (!anchors.Add(glyph)) throw new InvalidOperationException($"{where} : le repere « {glyph} » est ecrit deux fois.");
        }

        foreach (char letter in grid.Zones.SelectMany(row => row).Where(letter => letter != '.').Distinct())
        {
            if (!grid.Legend.ContainsKey(letter.ToString()))
                throw new InvalidOperationException($"{where} : la lettre de lieu « {letter} » n'est pas dans la legende.");
        }

        foreach (var (letter, zone) in grid.Legend)
        {
            if (letter.Length != 1) throw new InvalidOperationException($"{where} : « {letter} » n'est pas une lettre.");
            if (string.IsNullOrWhiteSpace(zone.Name)) throw new InvalidOperationException($"{where} : le lieu « {letter} » n'a pas de nom.");

            if (grid.Bounds(letter) is not { } bounds)
                throw new InvalidOperationException($"{where} : le lieu « {zone.Name} » n'est dessine nulle part.");

            // Un lieu est un rectangle plein : une lettre qui manque au milieu est une faute de
            // frappe, et un rectangle troue rattacherait au lieu des cases qui n'en sont pas.
            foreach (var cell in bounds.Positions())
            {
                if (grid.Zones[cell.Y - DemoGrid.Belt][cell.X - DemoGrid.Belt].ToString() != letter)
                {
                    throw new InvalidOperationException(
                        $"{where} : le lieu « {zone.Name} » n'est pas un rectangle plein "
                      + $"(case {cell.X - DemoGrid.Belt},{cell.Y - DemoGrid.Belt} du dessin).");
                }
            }
        }

        int vans = grid.Legend.Values.Count(zone => zone.Kind == ZoneKind.Start);
        if (vans != 1) throw new InvalidOperationException($"{where} : il faut exactement un van (lieu Start), il y en a {vans}.");

        if (grid.Legend.Values.Any(zone => zone.Kind == ZoneKind.Extraction))
            throw new InvalidOperationException($"{where} : le retour se deduit du van, il ne se dessine pas.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var zone in grid.Legend.Values)
        {
            if (!names.Add(zone.Name)) throw new InvalidOperationException($"{where} : deux lieux s'appellent « {zone.Name} ».");
        }
    }

    private void ValidateScenario(DemoScenario scenario, ItemCatalogue? items, SurvivorRoster? roster)
    {
        string where = $"Scenario « {scenario.Id} »";

        if (string.IsNullOrWhiteSpace(scenario.Name)) throw new InvalidOperationException($"{where} : pas de nom.");

        var grid = FindGrid(scenario.Grid)
            ?? throw new InvalidOperationException($"{where} : grille « {scenario.Grid} » inconnue.");

        var anchors = grid.Anchors();

        void Anchor(string? at, string what)
        {
            if (at is null || !anchors.ContainsKey(at))
                throw new InvalidOperationException($"{where} : {what} sur le repere « {at} », absent de la grille « {grid.Id} ».");
        }

        void Item(string item, string what)
        {
            if (items is not null && items.Find(item) is null)
                throw new InvalidOperationException($"{where} : {what} « {item} » absent du catalogue d'objets.");
        }

        var objective = grid.Legend.Values.FirstOrDefault(
                zone => zone.Name.Equals(scenario.Objective.Zone, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"{where} : lieu vise « {scenario.Objective.Zone} » absent de la grille.");

        if (objective.Kind == ZoneKind.Start)
            throw new InvalidOperationException($"{where} : l'objectif est au van, il n'y aurait rien a jouer.");

        Item(scenario.Objective.Item, "objet de mission");
        foreach (string at in scenario.Objective.At) Anchor(at, "objet de mission");

        if (scenario.Team.Count == 0) throw new InvalidOperationException($"{where} : personne ne part.");

        var enlisted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in scenario.Team)
        {
            if (!enlisted.Add(member.Survivor)) throw new InvalidOperationException($"{where} : {member.Survivor} est enrole deux fois.");

            if (roster is not null && roster.Find(member.Survivor) is null)
                throw new InvalidOperationException($"{where} : survivant « {member.Survivor} » absent du roster.");

            if (member.At is not null) Anchor(member.At, member.Survivor);
            foreach (string item in member.Carry) Item(item, $"paquetage de {member.Survivor} :");

            if (member.Health is <= 0f) throw new InvalidOperationException($"{where} : {member.Survivor} part hors de combat.");
        }

        foreach (string at in scenario.Zombies) Anchor(at, "zombie");

        foreach (var item in scenario.Items)
        {
            Item(item.Item, "objet");
            Anchor(item.At, item.Item);
        }

        if (scenario.TimeLimit is < 0d) throw new InvalidOperationException($"{where} : temps imparti negatif.");

        if (scenario.Focus == DemoFocus.Constraint && scenario.Instructions.Count == 0)
            throw new InvalidOperationException($"{where} : un scenario de consigne sans consigne ne montre rien.");

        // Une combinaison a une seule consigne est un scenario de consigne sous un autre nom.
        if (scenario.Focus == DemoFocus.Combination && scenario.Instructions.Count < 2)
            throw new InvalidOperationException($"{where} : une combinaison demande au moins deux consignes.");

        // Une consigne ecrite dans une carte blanche s'ajouterait en silence a celles du joueur, et la
        // prise lui attribuerait un effet qu'il n'a pas demande.
        if (scenario.TakesPlayerInstructions && scenario.Instructions.Count > 0)
            throw new InvalidOperationException($"{where} : une carte blanche ne porte pas de consigne, c'est le joueur qui les donne.");

        if (scenario.Expect.HasContrast && scenario.Instructions.Count == 0)
            throw new InvalidOperationException($"{where} : un contraste « sans consigne » demande une consigne.");

        // Une consigne peut ne promettre qu'une absence — « jamais EscapeLocation » —, a condition que
        // son contraste montre le geste en question : c'est lui qui prouve que l'absence tient a la
        // consigne et non au hasard de la prise. Ce qui arrive a la place varie d'une prise a l'autre
        // quand une horde s'en mele, et l'exiger ferait echouer la prise pour une raison qui n'est
        // pas la sienne.
        var with = scenario.Expect.With;
        bool provenAbsence = with.Never.Count > 0
            && scenario.Expect.Without.Shows.Intersect(with.Never, StringComparer.OrdinalIgnoreCase).Any();

        if (with.Shows.Count == 0 && !provenAbsence)
            throw new InvalidOperationException(
                $"{where} : un scenario doit dire ce qu'il montre (expect.with.shows), ou interdire un geste "
              + "que son contraste montre (expect.with.never et expect.without.shows).");
    }
}

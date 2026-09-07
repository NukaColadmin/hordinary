using Godot;
using Hordinary.Agents;
using Hordinary.AI.Navigation;
using Hordinary.AI.Planning;
using Hordinary.AI.Replanning;
using Hordinary.AI.Utility;
using Hordinary.AI.Zombies;
using Hordinary.Commands;
using Hordinary.Config;
using Hordinary.Game;
using Hordinary.UI;
using Hordinary.World;
using Hordinary.World.MapGeneration;

// Godot expose un noeud 3D nomme GridMap : on desambigue en faveur de notre grille de jeu.
using GridMap = Hordinary.World.GridMap;

namespace Hordinary.Game;

/// <summary>
/// Assemblage du prototype : construit la verite du monde depuis les TileMapLayer, cree les
/// survivants dotes chacun de leur propre carte mentale, et cable perception, A* et affichage.
///
/// Depuis le livrable 2, A* ne travaille plus sur la carte reelle mais sur
/// <see cref="AgentKnowledge.MentalMap"/> : un survivant ne peut planifier qu'avec ce qu'il sait.
/// Depuis le livrable 3, une horde et des evenements sonores partagent ce meme monde, chaque zombie
/// disposant lui aussi de sa propre memoire.
/// Depuis le livrable 4, le joueur ne fixe plus la destination : il donne un ordre, et la Utility AI
/// choisit l'objectif — quitte a lacher l'ordre pour fuir.
/// Depuis le livrable 5, l'objectif n'est plus resolu en une simple destination : GBFS le transforme en
/// suite d'actions, que <see cref="PlanExecutor"/> deroule en s'appuyant sur A* pour les trajets.
/// Depuis le livrable 6, cette boucle n'est plus cablee ici : <see cref="Replanner"/> la porte dans le
/// coeur, ce noeud ne fait plus que lui passer une situation percue et afficher son journal.
/// Depuis le livrable 7, ils sont trois. Le cablage d'un survivant vit dans <see cref="SurvivorAgent"/>
/// et le groupe dans <see cref="SurvivorBand"/> : ce noeud choisit les cadences, ecoute et affiche.
/// Rien n'est mis en commun entre les trois sauf le canal par lequel ils se parlent — deux survivants
/// eloignes en savent exactement autant que deux inconnus.
/// Depuis le livrable 8, la boucle est fermee : les entrees du joueur ne posent plus directement des
/// objectifs mais fabriquent des <see cref="PlayerCommand"/> que <see cref="CommandManager"/> traduit,
/// <see cref="MissionTracker"/> dit ou en est la mission, et l'affichage est reparti entre plusieurs
/// panneaux. Ce noeud ne contient plus aucune regle de jeu — seulement des cadences, des touches et
/// du texte.
///
/// Une partie se deroule en trois temps, et ce noeud les enchaine :
/// <list type="number">
/// <item>Le joueur compose son equipe et choisit sa destination au camp (<see cref="CampScreen"/>).
/// Rien ne tourne encore : la carte est la, personne n'est dessus.</item>
/// <item>La mission se joue. Elle commence au camp et s'y termine.</item>
/// <item>La caisse rentre au camp, et la partie est gagnee. Depart et arrivee au meme endroit :
/// c'est ce qui donne un sens au mot « mission ».</item>
/// </list>
/// </summary>
public partial class GameRoot : Node2D
{
    [Export] public string GameConfigPath { get; set; } = "res://config/game_config.json";
    [Export] public string TileLegendPath { get; set; } = "res://config/tile_legend.json";
    [Export] public string MapLocationsPath { get; set; } = "res://config/map_locations.json";
    [Export] public string UtilityGoalsPath { get; set; } = "res://config/utility_goals.json";
    [Export] public string PlannerDomainPath { get; set; } = "res://config/planner_domain.json";
    [Export] public string SurvivorsPath { get; set; } = "res://config/survivors.json";
    [Export] public string MissionsPath { get; set; } = "res://config/missions.json";
    [Export] public string ItemsPath { get; set; } = "res://config/items.json";
    [Export] public string WeaponsPath { get; set; } = "res://config/weapons.json";
    [Export] public string ZonesPath { get; set; } = "res://config/zones.json";
    [Export] public string CampPath { get; set; } = "res://config/camp.json";
    [Export] public string MapGenPath { get; set; } = "res://config/mapgen.json";

    /// <summary>Noms des TileMapLayer, du fond vers le premier plan.</summary>
    [Export] public string[] LayerNames { get; set; } = ["Ground", "Wall-Door", "Window", "Objects"];

    /// <summary>
    /// Une couleur d'identification par survivant, dans l'ordre ou le joueur les a enroles. Elle ne
    /// teinte pas le personnage — les six sprites sont livres en couleur — elle sert l'anneau au sol et
    /// le nom au-dessus de la tete. Deux survivants peuvent partager un sprite, jamais une couleur.
    /// </summary>
    private static readonly Color[] RingColors =
    [
        new(0.31f, 0.78f, 1f),
        new(1f, 0.71f, 0.29f),
        new(0.55f, 0.92f, 0.55f),
        new(0.92f, 0.55f, 0.92f),
        new(0.95f, 0.95f, 0.5f),
    ];

    private GameConfig _config = GameConfig.Default;
    private GridMap _map = null!;
    private WorldState _world = null!;
    private SurvivorBand _band = null!;
    private EntityManager _entities = null!;
    private UtilityGoalSet _goals = UtilityGoalSet.Empty;
    private PlanningDomain _domain = PlanningDomain.Empty;
    private SurvivorRoster _roster = SurvivorRoster.Empty;
    private MissionCatalogue _catalogue = MissionCatalogue.Empty;

    /// <summary>Les objets et les armes livres (livrable 11), les deux fichiers reunis.</summary>
    private ItemCatalogue _items = ItemCatalogue.Empty;

    /// <summary>Les profils de zone : ce qu'un lieu contient, par-dessus ce qu'il est.</summary>
    private ZoneCatalogue _zones = ZoneCatalogue.Empty;

    /// <summary>
    /// Le stock du camp. Il survit a la mission : c'est lui qui transforme « une mission, une caisse »
    /// en boucle de survie, et l'ecran de preparation le montre avant chaque depart.
    /// </summary>
    private CampStock _camp = new();

    /// <summary>Les profils de quartier (livrable 13). Vides, toute mission retombe sur la carte dessinee.</summary>
    private MapGenConfig _mapgen = MapGenConfig.Empty;

    /// <summary>Le quartier genere de la mission en cours, s'il y en a un.</summary>
    private GeneratedDistrict? _district;

    /// <summary>
    /// La legende, gardee apres le chargement : elle sert a lire la carte dessinee, puis a en peindre
    /// une generee. C'est le meme fichier dans les deux sens.
    /// </summary>
    private TileLegend _legend = new();

    /// <summary>
    /// Les couches de la scene, alignees sur <see cref="LayerNames"/> — une entree nulle pour une
    /// couche absente. L'alignement compte : le peintre designe ses couches par indice, et tasser la
    /// liste ferait dessiner les fenetres sur la couche des objets.
    /// </summary>
    private TileMapLayer?[] _layers = [];

    /// <summary>La camera, gardee pour recadrer et borner quand la carte change sous elle.</summary>
    private CameraController _camera = null!;

    /// <summary>
    /// Les vignettes d'objets, decoupees dans la meme planche que la carte. Il n'existe pas de serie
    /// d'icones a part : ce qu'on voit dans un sac est la tuile qu'on voit au sol.
    /// </summary>
    private ItemIcons? _icons;

    private NavigationDebugOverlay _overlay = null!;
    private KnowledgeDebugOverlay _knowledgeOverlay = null!;
    private NoiseDebugOverlay _noiseOverlay = null!;

    private Hud _hud = null!;
    private AgentPanel _agentPanel = null!;
    private PlanPanel _planPanel = null!;
    private AgentRoster _team = null!;
    private SurvivorPanel _survivorPanel = null!;
    private CampScreen _campScreen = null!;

    private CommandManager _commands = null!;
    private MissionDefinition _mission = null!;
    private MissionTracker _tracker = null!;

    private int _tileSize = 16;

    /// <summary>La planche peinte dans l'editeur, gardee pour y decouper les vignettes d'inventaire.</summary>
    private TileSet? _tileSet;

    /// <summary>Faux tant que le joueur n'a pas valide son equipe et sa mission.</summary>
    private bool _started;

    private readonly List<SurvivorNode> _nodes = [];
    private readonly Dictionary<string, ZombieNode> _zombieNodes = [];
    private VanNode? _van;

    /// <summary>
    /// Le peintre, garde apres la generation : il ne sert plus alors qu'a une case a la fois, celle
    /// d'un objet pose ou ramasse.
    /// </summary>
    private MapPainter _painter = null!;

    private double _sincePerception;
    private double _sinceZombieThink;
    private PathResult? _lastPath;

    /// <summary>Course : plus vite, mais trois fois plus bruyant (bareme de la section 18.1).</summary>
    private bool _running;

    private Random _noiseRng = new(0);

    /// <summary>
    /// Le survivant que le joueur a sous les yeux. Les overlays, les panneaux et les ordres le
    /// suivent : il n'y a pas de vue « du groupe », parce qu'il n'existe pas de connaissance du
    /// groupe. C'est <see cref="CommandManager"/> qui en tient le compte, pas ce noeud.
    /// </summary>
    private SurvivorAgent Current => _commands.Selected ?? _band.Agents[0];

    /// <summary>Les trois panneaux de texte appartiennent au mode debug ; la barre d'equipe, non.</summary>
    private bool ShowsText => _config.Debug.ShowsText;

    public override void _Ready()
    {
        // Tant que la configuration n'est pas lue, la console se tait : c'est le seul defaut qui ne
        // peut pas produire de bruit qu'on n'a pas demande.
        Trace.Enabled = false;

        _config = LoadJson(GameConfigPath, GameConfig.FromJson, GameConfig.Default, "configuration");
        Trace.Enabled = _config.Debug.ShowsConsole;
        Trace.UseChannels(_config.Debug.ConsoleChannels);
        Trace.Print($"[Config] configuration lue depuis {GameConfigPath}.");

        _legend = LoadJson(TileLegendPath, TileLegend.FromJson, new TileLegend(), "legende des tuiles");
        _tileSize = _legend.TileSize;
        _painter = new MapPainter(_legend);

        BuildMap(_legend);

        // Les profils de quartier avant les missions : une mission qui en nomme un doit trouver a qui
        // parler. Le fichier absent n'est pas une erreur — il n'y a alors que la carte dessinee.
        _mapgen = LoadJson(MapGenPath, MapGenConfig.FromJson, MapGenConfig.Empty, "profils de quartier");

        _noiseRng = new Random(_config.Zombies.Seed);

        _goals = LoadJson(UtilityGoalsPath, UtilityGoalSet.FromJson, UtilityGoalSet.Empty, "objectifs");
        _domain = LoadJson(PlannerDomainPath, PlanningDomain.FromJson, PlanningDomain.Empty, "domaine de planification");
        _roster = LoadJson(SurvivorsPath, SurvivorRoster.FromJson, SurvivorRoster.Empty, "survivants");
        _zones = LoadJson(ZonesPath, ZoneCatalogue.FromJson, ZoneCatalogue.Empty, "zones");

        // Les zones avant les missions : une mission peut viser un profil, et c'est le catalogue de
        // zones qui lui donne alors sa categorie structurelle.
        _catalogue = LoadJson(MissionsPath, json => MissionCatalogue.FromJson(json, _zones), MissionCatalogue.Empty, "missions");

        _camp = new CampStock(LoadJson(CampPath, CampSettings.FromJson, CampSettings.Default, "camp"));

        // Objets et armes partagent un espace d'identifiants unique : un inventaire ne connait qu'un
        // id, jamais le fichier dont il vient.
        _items = ItemCatalogue.Merge(
            LoadJson(ItemsPath, ItemCatalogue.FromJson, ItemCatalogue.Empty, "objets"),
            LoadJson(WeaponsPath, ItemCatalogue.FromJson, ItemCatalogue.Empty, "armes"));

        _icons = new ItemIcons(_legend, _tileSet, _items);

        // Une table de butin qui nomme un objet inexistant ne se verrait autrement qu'a la fouille,
        // c'est-a-dire jamais, puisqu'elle ne produirait rien.
        try
        {
            _zones.ValidateLoot(_items);
        }
        catch (Exception exception)
        {
            Trace.Error($"[Zone] {exception.Message}");
        }

        CreateCamera();

        LogMapSummary();
        LogCatalogues();

        ShowSetup();
    }

    // --- Preparation ----------------------------------------------------------------------------

    /// <summary>
    /// Le premier ecran : le camp. Ce qu'il reste dans les reserves, qui est la, et ou on les envoie.
    /// La carte est deja construite derriere : le joueur compose son equipe en voyant le terrain ou
    /// il l'envoie.
    /// </summary>
    private void ShowSetup()
    {
        if (_roster.Survivors.Count == 0)
        {
            Trace.Error("[Agent] Aucun survivant charge : la partie n'a personne a jouer.");
            return;
        }

        if (_catalogue.Missions.Count == 0)
        {
            Trace.Error("[Mission] Aucune mission chargee : il n'y a rien a proposer au joueur.");
            return;
        }

        if (_config.Debug.SkipsSetup)
        {
            Trace.Print("[Debug] Ecran de camp saute (config debug.skip_setup).");
            Begin(_catalogue.Missions[0], _roster.Survivors);
            return;
        }

        _campScreen = new CampScreen { Name = "Camp" };
        AddChild(_campScreen);

        _campScreen.Configure(
            _roster.Survivors,
            _catalogue.Missions,
            _config.Survivor,
            _camp,
            _icons,
            _config.Zombies.Seed);

        _campScreen.Paint(RingColors);

        _campScreen.MissionChosen += Begin;
    }

    /// <summary>
    /// Le joueur a valide : on peuple le monde. Tout ce qui suit dependait d'un choix, et rien de
    /// tout cela n'existait avant — ni les survivants, ni la horde, ni les caisses.
    /// </summary>
    private void Begin(MissionDefinition mission, IReadOnlyList<SurvivorProfile> team)
    {
        _mission = mission;

        Trace.Print(
            $"[Mission] {mission.Name} ({mission.Difficulty ?? "sans difficulte annoncee"}) — "
          + $"{team.Count} agent(s) : {string.Join(", ", team.Select(profile => profile.Name))}.");

        // Le quartier d'abord : tout ce qui suit se pose sur une carte, et cette carte peut n'exister
        // que depuis cette ligne.
        if (mission.IsGenerated) GenerateDistrict(mission);

        PlaceSupplies(mission);
        ShowItems();
        ShowTerrain();
        CreateBand(team, mission);

        if (_band.Agents.Count == 0) return;

        // La ceinture est connue avant le premier regard : sans cela, le bord du monde offrirait mille
        // frontieres a explorer, toutes infranchissables.
        if (_district is not null) Trace.Print($"[Carte] Ceinture connue du groupe : {_band.KnowBorder()} cases.");

        CreateHorde(mission);
        CreateOverlays();
        CreateHud();
        CreateSurvivorNodes();
        CreateZombieNodes();
        CreateVan(mission);

        _commands = new CommandManager(_band, _map, _config.UtilityAi);
        _commands.SelectionChanged += _ => Follow();
        // Sans personne a regarder, les overlays retombent sur le premier du groupe, comme le reste
        // de l'affichage : une carte mentale orpheline serait celle de plus personne.
        _commands.SelectionCleared += Follow;
        _commands.Executed += result => Trace.Print($"[Ordre] {result}.");

        _tracker = new MissionTracker(_world, _band, mission);

        WatchMission();
        ApplyDebugSettings();
        Follow();
        UpdatePerception();

        _started = true;
    }

    /// <summary>L'issue de la partie part au journal. Ce noeud ne fait que la relayer.</summary>
    private void WatchMission()
        => _tracker.Finished += verdict => Trace.Print($"[Partie] {verdict} (t = {verdict.At:0.0} s).");

    /// <summary>
    /// Etat de depart du mode debug, lu dans <c>config/game_config.json</c>. Ouvrir la partie
    /// directement avec les overlays allumes, ou sans panique, evite d'avoir a rejouer les trente
    /// premieres secondes en tapant sur les touches de fonction.
    /// </summary>
    private void ApplyDebugSettings()
    {
        _band.PanicEnabled = _config.Debug.PanicEnabled;

        _overlay.Visible = _config.Debug.ShowsOverlay(_config.Debug.ShowNavigation);
        _knowledgeOverlay.Visible = _config.Debug.ShowsOverlay(_config.Debug.ShowMentalMap);
        _noiseOverlay.Visible = _config.Debug.ShowsOverlay(_config.Debug.ShowNoise);

        _hud.Visible = ShowsText;
        _agentPanel.Visible = ShowsText;
        _planPanel.Visible = ShowsText;

        if (!_config.Debug.Enabled) Trace.Print("[Debug] Mode debug entierement desactive (config debug.enabled).");
        else if (!ShowsText) Trace.Print("[Debug] Panneaux de texte masques (config debug.hud_text).");

        if (!_config.Debug.PanicEnabled)
            Trace.Print("[Debug] Panique et stress neutralises au demarrage (config debug.panic_enabled).");
    }

    // --- Construction ---------------------------------------------------------------------------

    /// <summary>
    /// Genere le quartier de la mission (livrable 13) et le substitue a la carte dessinee.
    ///
    /// Tout ce qui suit dans <see cref="Begin"/> — les caisses, le groupe, la horde, les overlays —
    /// est ecrit contre une <see cref="GridMap"/> et un <see cref="WorldState"/>, sans jamais demander
    /// d'ou ils viennent. C'est la raison pour laquelle brancher un generateur tient en une methode :
    /// il produit exactement ce que le chargeur de tuiles produisait, et rien en aval n'apprend qu'il
    /// existe.
    ///
    /// Un echec ne fait pas tomber la partie. Le fichier peut manquer, le profil peut etre mal
    /// orthographie, et huit tentatives peuvent ne rien donner : dans ces trois cas la mission se joue
    /// sur la carte dessinee, avec une ligne d'erreur qui dit laquelle des trois. Se rabattre en
    /// silence serait pire — le joueur croirait avoir vu un quartier genere.
    /// </summary>
    private void GenerateDistrict(MissionDefinition mission)
    {
        if (_mapgen.Find(mission.MapProfile) is null)
        {
            Trace.Error(
                $"[Carte] La mission « {mission.Id} » demande le profil « {mission.MapProfile} », "
              + $"absent de {MapGenPath} : la carte dessinee sert de repli.");
            return;
        }

        // La graine du joueur si l'ecran de camp est passe par la, celle du fichier sinon, celle de la
        // partie a defaut. C'est le champ de saisie qui tranche : il a ete rempli avec la graine de la
        // mission au moment ou elle a ete choisie.
        int seed = _campScreen is not null && IsInstanceValid(_campScreen)
            ? _campScreen.MapSeed
            : mission.MapSeed ?? _config.Zombies.Seed;

        var generator = new DistrictGenerator(_mapgen);

        var request = new DistrictRequest
        {
            Mission = mission,
            Seed = seed,
            ZombieCount = mission.ZombieCount ?? _config.Zombies.Count,
            MinimumSpawnDistance = _config.Zombies.MinimumSpawnDistance,
        };

        GeneratedDistrict district;

        try
        {
            district = generator.Generate(request);
        }
        catch (Exception exception)
        {
            Trace.Error($"[Carte] Aucun quartier jouable pour la graine {seed} ({exception.Message}) ; "
                      + "la carte dessinee sert de repli.");
            return;
        }
        finally
        {
            // Les refus sont dits meme quand la generation finit par aboutir : ils expliquent pourquoi
            // le quartier joue n'est pas celui de la graine demandee.
            foreach (var refused in generator.Rejected) Trace.Warn($"[Carte] Quartier refuse — {refused}.");
        }

        _district = district;
        _map = district.Map;
        _world = new WorldState(_map, _config.Noise);

        // Les meubles poussables du livrable 11 : le generateur les a poses en tenant compte de la
        // navigation, le monde n'a plus qu'a les tenir — et le decor, les montrer.
        foreach (var heavy in district.Heavy)
        {
            _world.Heavy.Add(heavy);

            var body = new HeavyNode { Name = heavy.Id, ZIndex = 1 };
            AddChild(body);
            body.Configure(heavy, _tileSize);
        }

        int index = 0;
        foreach (var item in district.Items)
        {
            _world.SetItem($"{item.ItemId}-{++index}".ToLowerInvariant(), item.ItemId, item.Position);
        }

        foreach (var missing in _painter.Paint(_layers, district, _world.Items.Values))
            Trace.Warn($"[Carte] {missing}");

        _camera.FrameDistrict(_map, _tileSize, district.Drop);

        Trace.Print(
            $"[Carte] {district} — {district.Border.Name}, ceinture de {district.BeltThickness} cases, "
          + $"{district.Attempts} tentative(s), {district.Items.Count} objets au sol, "
          + $"{district.Heavy.Count} meubles, {district.ZombieSpawns.Count} zombies prevus.");
    }

    private void BuildMap(TileLegend legend)
    {
        _layers = new TileMapLayer?[LayerNames.Length];

        for (int rank = 0; rank < LayerNames.Length; rank++)
        {
            _layers[rank] = GetNodeOrNull<TileMapLayer>(LayerNames[rank]);
            if (_layers[rank] is null) Trace.Warn($"[Carte] Couche '{LayerNames[rank]}' introuvable dans la scene.");
        }

        var layers = _layers.OfType<TileMapLayer>().ToList();

        // La planche des tuiles sert deux fois : a peindre la carte, et a decouper les vignettes
        // d'inventaire. Les couches partagent toutes le meme TileSet, la premiere trouvee fait foi.
        _tileSet = layers.FirstOrDefault()?.TileSet;

        var result = new MapLoader(legend).Load(layers);
        _map = result.Map;

        foreach (var warning in result.Warnings) Trace.Warn($"[Carte] {warning}");

        var locations = LoadJson(MapLocationsPath, MapLocationsConfig.FromJson, MapLocationsConfig.Empty, "lieux");
        locations.ApplyTo(_map);

        _world = new WorldState(_map, _config.Noise);

        int index = 0;
        foreach (var item in result.Items)
        {
            string id = $"{item.ItemId}-{++index}".ToLowerInvariant();
            _world.SetItem(id, item.ItemId, item.Position);
            Trace.Print($"[Monde] Objet {id} en {item.Position}.");
        }
    }

    /// <summary>
    /// Pose ce que la mission demande de rapporter, dans la zone qu'elle designe. Les caisses vont
    /// aux cases les plus eloignees du camp : rien n'est plus decevant qu'un objectif qu'on trouve
    /// en poussant la premiere porte. Le tirage suit la graine de la horde, donc une mission rejouee
    /// se deroule a l'identique.
    /// </summary>
    private void PlaceSupplies(MissionDefinition mission)
    {
        if (mission.ObjectiveIn(_map) is not { } zone)
        {
            Trace.Error($"[Mission] {mission.Name} : aucune zone {mission.Objective} sur la carte.");
            return;
        }

        var camp = mission.CampIn(_map)?.Center ?? default;

        var cells = zone.Positions()
            .Where(_map.IsWalkable)
            .OrderByDescending(cell => cell.ChebyshevDistanceTo(camp))
            .ThenBy(cell => cell.X)
            .ThenBy(cell => cell.Y)
            .ToList();

        if (cells.Count == 0)
        {
            Trace.Error($"[Mission] {mission.Name} : « {zone.Name} » n'offre aucune case praticable.");
            return;
        }

        // Les caisses sont espacees : deux boites sur des cases voisines ne donnent pas au groupe
        // deux raisons de se separer, qui est tout l'interet d'en poser plusieurs.
        int spacing = Math.Max(1, cells.Count / mission.Supplies);

        for (int i = 0; i < mission.Supplies; i++)
        {
            var cell = cells[Math.Min(i * spacing, cells.Count - 1)];

            _world.SetItem($"supply-{i + 1}", mission.Item, cell);
            Trace.Print($"[Mission] {mission.Item} en {cell} ({zone.Name}).");
        }
    }

    /// <summary>
    /// Met le decor d'accord avec le monde, et l'y maintient : chaque objet reellement pose au sol
    /// se voit, et lui seul.
    ///
    /// Deux mensonges disparaissent ici, et le second etait le plus couteux. Les caisses de la
    /// mission sont posees <b>apres</b> la peinture du quartier : elles n'apparaissaient donc nulle
    /// part, et le joueur voyait un survivant entrer dans la pharmacie et en ressortir avec des
    /// medicaments qu'aucune image n'avait annonces. Et rien n'effacait ce qui etait ramasse : la
    /// tuile d'un objet emporte restait au sol.
    ///
    /// Le peintre ne decide de rien : c'est <see cref="WorldState"/> qui annonce, ici comme partout.
    /// </summary>
    /// <summary>
    /// Met le decor d'accord avec le terrain, et l'y maintient.
    ///
    /// Une porte forcee, une vitre brisee : le jeu change le type d'une case, et jusqu'ici l'image ne
    /// bougeait pas. Le joueur voyait donc un survivant franchir une porte dessinee fermee — le seul
    /// endroit du prototype ou l'ecran contredisait la simulation.
    ///
    /// Comme pour les objets, le peintre ne decide de rien : c'est la grille qui annonce
    /// (<see cref="GridMap.CellChanged"/>), et l'abonnement est pris <b>apres</b> la peinture du
    /// quartier — la generation pose des milliers de cases, et aucune n'a besoin d'etre repeinte.
    /// </summary>
    private void ShowTerrain()
        => _map.CellChanged += (cell, type) => _painter.ShowCell(_layers, cell, type);

    private void ShowItems()
    {
        foreach (var item in _world.Items.Values) _painter.ShowItem(_layers, item.Position, item.ItemId);

        _world.ItemPlaced += item => _painter.ShowItem(_layers, item.Position, item.ItemId);

        // Deux objets peuvent partager une case — un sac tombe sur une caisse. La case revient donc
        // a ce qu'il y reste, et non a rien.
        _world.ItemTaken += item => _painter.ShowItem(
            _layers, item.Position, _world.ItemsAt(item.Position).FirstOrDefault()?.ItemId);
    }

    /// <summary>
    /// Enrole les profils que le joueur a retenus dans l'ecran de preparation. Aucun d'eux n'est
    /// nomme dans le code : Alice, Bob et Claire sont des donnees, et un quatrieme profil entrerait
    /// en jeu sans recompilation.
    /// </summary>
    private void CreateBand(IReadOnlyList<SurvivorProfile> team, MissionDefinition mission)
    {
        _band = new SurvivorBand(_world, _config, catalogue: _items);

        // Les agents commencent et finissent au meme endroit : le camp est le point de depart.
        var spawn = FindSpawn(mission);

        // Le lieu exact vise par la mission, pas seulement sa categorie : deux batiments partagent la
        // meme, et le domaine de planification n'a qu'un « objective_zone » a offrir.
        var objective = mission.ObjectiveIn(_map);
        var site = objective is null ? null : new ObjectiveSite(objective.Name, objective.Kind);

        foreach (var profile in team)
        {
            var agent = _band.Enlist(profile, spawn, _goals, _domain);

            // Le survivant sait ce qu'on l'envoie chercher, et ou : c'est la mission qui le dit, et
            // c'est la seule raison pour laquelle ni le code ni le domaine de planification n'ont jamais
            // a nommer un objet ni un lieu. Le domaine parle d'« objective » et d'« objective_zone » ;
            // ces deux lignes sont ce qui leur donne un sens, et elles changent a chaque mission.
            agent.Survivor.ObjectiveItem = mission.Item;
            agent.Survivor.ObjectiveZone = site;

            Watch(agent);

            Trace.Print(
                $"[Agent] {profile.Name} demarre en {spawn} — " +
                $"vision {agent.Survivor.VisionRange} cases, " +
                $"HP {agent.Survivor.Health:0}/{agent.Survivor.Settings.MaxHealth:0}.");
        }

        // Briefing du joueur : les lieux de la mission, pas leur contenu. C'est une connaissance
        // transmise, pas une observation — le terrain reste entierement a decouvrir.
        _band.Brief([.. mission.BriefedZones()]);

        // Et le lieu vise nommement : sans cette ligne, une mission qui envoie au hangar sud briefe le
        // premier batiment venu, et le groupe part fouiller l'entrepot ouest.
        if (objective is not null) _band.Brief(objective);

        _band.Sharing.Exchanged += exchange =>
        {
            if (exchange.Learned > 0) Trace.Print($"[Partage] {exchange}.");
        };
    }

    /// <summary>Branche les journaux d'un agent sur la console Godot.</summary>
    private void Watch(SurvivorAgent agent)
    {
        string who = agent.Survivor.Name;

        // Canal a part, et ferme par defaut : ce sont les details d'execution — chaque pas fait du
        // bruit, chaque fouille dit ce qu'elle n'a pas trouve. Utile quand on cherche pourquoi une
        // action a echoue, illisible quand on suit un plan, que « Plan » resume deja.
        agent.Logged += message => Trace.Print($"[Action] {who} : {message}.");

        agent.Executor.ActionStarted += action =>
        {
            Trace.Print($"[Plan] {who} commence {action.Describe()}.");

            if (!ReferenceEquals(agent, Current)) return;
            if (agent.Runtime.LastPath is not { } path || agent.Runtime.LastDestination is not { } destination) return;

            _lastPath = path;
            _overlay.SetPath(path, destination, agent.Astar.LastExploredNodes);
        };

        agent.Executor.ActionFailed += (action, failure)
            => Trace.Print($"[Plan] {who} : echec de {action.Name} — {failure.Reason} ({failure.Kind}).");

        agent.Executor.PlanFinished += plan
            => Trace.Print($"[Plan] {who} : {plan.Goal} — {plan.Status}"
                         + $"{(plan.FailureReason is { } why ? $" ({why})" : string.Empty)}.");

        // « IA » est le journal de decision complet. Les replanifications en sont exclues : elles
        // arrivent aussi par Replanned, qui imprime le bloc de quatre lignes que la specification
        // impose — les laisser ici les ecrivait deux fois, mot pour mot, et cela faisait a soi seul
        // un quart du journal d'une mission.
        agent.Brain.Journal.Written += entry =>
        {
            if (entry.Kind is DecisionKind.Action or DecisionKind.Replan) return;
            Trace.Print($"[IA] {entry}");
        };

        agent.Brain.Monitor.Detected += trigger => Trace.Print($"[Replan] {who} : {trigger.Reason}.");

        agent.Brain.Replanned += record =>
        {
            // Le bloc impose par la specification, tel quel, avant tout resume.
            foreach (var line in record.Lines) Trace.Print($"[Replan] {line}");

            if (record.Plan is { } plan)
            {
                foreach (var line in plan.DescribeSteps()) Trace.Print($"[Replan]   {line}");
            }
        };

        agent.Glance.Glanced += _ => Trace.Print($"[Agent] {who} jette un coup d'oeil derriere elle.");
    }

    /// <summary>
    /// Peuple la carte (specification section 17). Chaque zombie recoit sa propre memoire, vide :
    /// aucun ne sait ou sont les survivants au demarrage, meme si le monde, lui, le sait. La taille
    /// de la horde est celle que la mission annonce — c'est elle, et non l'etiquette de difficulte,
    /// qui rend une mission dure.
    /// </summary>
    private void CreateHorde(MissionDefinition mission)
    {
        _entities = new EntityManager(_world, _config.Zombies, _config.Knowledge, _config.Perception);

        // Sur un quartier genere, les emplacements sont deja tires — avec la graine du quartier, en
        // meme temps que le reste. Refaire ici un tirage sur la graine de la horde reviendrait a
        // decider deux fois, et deux quartiers identiques n'auraient plus la meme horde.
        int spawned = _district is { } district
            ? _entities.SpawnHorde(district.ZombieSpawns)
            : _entities.SpawnHorde(FindSpawn(mission), mission.ZombieCount);

        // Les survivants peuvent desormais frapper (livrable 8) : la horde se branche apres coup,
        // parce qu'elle est placee en fonction du point de depart du groupe.
        _band.Horde = _entities;

        _entities.Removed += zombie =>
        {
            if (!_zombieNodes.Remove(zombie.Id, out var node)) return;
            node.QueueFree();
        };

        _entities.StateChanged += (zombie, decision) =>
        {
            // Le journal ne retient que les bascules qui comptent : l'errance est du bruit de fond.
            if (decision.State is ZombieState.Idle or ZombieState.Wander) return;
            Trace.Print($"[Zombie] {zombie.Id} -> {decision.State} ({decision.Reason}).");
        };

        _entities.Attacked += (zombie, target) => Bite(zombie, target);

        Trace.Print(
            $"[Monde] {spawned} zombies places (graine {_district?.Seed ?? _config.Zombies.Seed}, " +
            $"a {_config.Zombies.MinimumSpawnDistance} cases minimum du camp).");
    }

    /// <summary>
    /// Un zombie mord celui qu'il a rattrape. Se faire mordre est une observation directe : on sait
    /// ce qui vous touche, meme dans le dos. Sans cela, un zombie hors du champ de vision devorerait
    /// un survivant qui continue tranquillement d'explorer.
    /// </summary>
    private void Bite(Zombie zombie, string targetId)
    {
        if (_band.Find(targetId) is not { } agent) return;

        var victim = agent.Survivor;
        if (!victim.Alive) return;

        victim.Knowledge.RememberEntity(zombie.Id, EntityKind.Zombie, zombie.Position, _world.Time);
        victim.TakeDamage(_config.Zombies.AttackDamage);

        Trace.Print(
            $"[Combat] {zombie.Id} frappe {victim.Name} : sante {victim.Health:0}, " +
            $"panique {victim.Panic:P0}.");

        if (victim.Alive)
        {
            // « Lorsqu'un survivant est blesse » : la decision ne peut pas attendre le cooldown. Le
            // plan, lui, n'est pas invalide pour autant — c'est la montee de panique que le moniteur
            // de replanification verra, si elle franchit le seuil.
            agent.Utility.Request("survivant blesse");
            return;
        }

        agent.Stop("survivant hors de combat");
        Trace.Print($"[Combat] {victim.Name} est hors de combat.");

        // Perdre le dernier survivant est une fin de partie : la question se pose maintenant, pas a
        // la prochaine passe.
        _tracker.Evaluate(_world.Time);
    }

    private void CreateSurvivorNodes()
    {
        for (int i = 0; i < _band.Agents.Count; i++)
        {
            var node = new SurvivorNode
            {
                // Le nom porte l'identifiant du survivant : c'est ainsi qu'on retrouve son corps
                // quand il rentre au camp et qu'il faut le retirer de la carte.
                Name = _band.Agents[i].Survivor.Id,
                RingColor = RingColors[i % RingColors.Length],
                ZIndex = 9,
            };

            AddChild(node);
            node.Configure(_band.Agents[i], _tileSize);
            _nodes.Add(node);
        }
    }

    private void CreateZombieNodes()
    {
        foreach (var zombie in _entities.Zombies)
        {
            var node = new ZombieNode { Name = zombie.Id, ZIndex = 7 };
            AddChild(node);
            node.Configure(zombie, _tileSize);
            _zombieNodes[zombie.Id] = node;
        }
    }

    /// <summary>
    /// Pose le van sur la zone de camp : un skin visuel, sans aucune fonction de jeu. Une seule
    /// mission par partie dans ce prototype, mais la garde evite un doublon si <see cref="Begin"/>
    /// devait un jour se rejouer.
    /// </summary>
    private void CreateVan(MissionDefinition mission)
    {
        if (_van is not null) return;
        if (mission.CampIn(_map) is not { } camp) return;

        _van = new VanNode { Name = "Van", ZIndex = 2 };
        AddChild(_van);
        _van.Configure(camp, _tileSize);
    }

    private GridPosition FindSpawn(MissionDefinition mission)
    {
        if (mission.CampIn(_map) is { } camp && _map.FindWalkableIn(camp) is { } cell) return cell;

        Trace.Warn("[Carte] Aucun camp praticable ; repli sur la premiere case libre.");
        return _map.AllPositions().FirstOrDefault(_map.IsWalkable);
    }

    private void CreateCamera()
    {
        _camera = new CameraController { Name = "Camera" };
        AddChild(_camera);
        _camera.FrameMap(_map, _tileSize);
    }

    private void CreateOverlays()
    {
        _knowledgeOverlay = new KnowledgeDebugOverlay { Name = "KnowledgeOverlay", ZIndex = 8 };
        AddChild(_knowledgeOverlay);

        _overlay = new NavigationDebugOverlay { Name = "DebugOverlay", ZIndex = 10 };
        AddChild(_overlay);

        _noiseOverlay = new NoiseDebugOverlay { Name = "NoiseOverlay", ZIndex = 11 };
        AddChild(_noiseOverlay);
        _noiseOverlay.Configure(_world.Noises, _entities.Zombies, _tileSize);
    }

    /// <summary>
    /// Rebranche les overlays sur le survivant selectionne. Ils montrent une carte mentale, une
    /// carte de chaleur et des frontieres — il y en a autant que de survivants, differents, et c'est
    /// precisement ce qu'on veut voir.
    /// </summary>
    private void Follow()
    {
        var agent = Current;
        var knowledge = agent.Survivor.Knowledge;

        _knowledgeOverlay.Configure(knowledge, _tileSize);
        _overlay.Configure(knowledge.MentalMap, agent.Navigation, _tileSize);

        _overlay.SetDangerMemory(() => knowledge.Danger.Cells.Keys
            .Select(cell => (Cell: cell, Value: knowledge.Danger.ValueAt(cell, _world.Time))));

        // Les fermetures interrogent toujours le survivant affiche : elles sont rebranchees a chaque
        // changement de point de vue, et lisent donc la memoire de celui qu'on regarde.
        _overlay.SetFrontiers(() => Current.Context is not { } context
            ? []
            : context.RankFrontiers()
                .Take(8)
                .Select(frontier => (frontier.Cell, Chosen: frontier.Cell == context.BestFrontier?.Cell)));

        _overlay.SetForbiddenZones(() => Current.Survivor.Orders.ForbiddenZones);

        _lastPath = agent.Runtime.LastPath;
        _overlay.SetPath(_lastPath, agent.Runtime.LastDestination, agent.Astar.LastExploredNodes);

        if (agent.LastPerception is { } perception)
            _knowledgeOverlay.SetObservation(perception.VisibleCells, _world.Time);

        UpdateHud();
    }

    /// <summary>
    /// L'affichage de la mission. La barre d'equipe du bas est celle du <b>joueur</b> : elle montre
    /// qui est dehors et dans quel etat, et elle survit a l'extinction du mode debug. Les trois
    /// panneaux de texte, eux, montrent ce que la <b>simulation</b> pense — l'etat de la partie, ce
    /// qu'un survivant sait, ce que la recherche a produit — et disparaissent avec lui.
    /// </summary>
    private void CreateHud()
    {
        var layer = new CanvasLayer { Name = "Hud" };
        AddChild(layer);

        _hud = new Hud();
        _hud.AnchorTo(HorizontalAlignment.Left, VerticalAlignment.Top);
        layer.AddChild(_hud);

        _agentPanel = new AgentPanel();
        _agentPanel.AnchorTo(HorizontalAlignment.Right, VerticalAlignment.Top);
        layer.AddChild(_agentPanel);

        _planPanel = new PlanPanel();
        _planPanel.AnchorTo(HorizontalAlignment.Right, VerticalAlignment.Bottom);
        layer.AddChild(_planPanel);

        _survivorPanel = new SurvivorPanel { Name = "SurvivorPanel" };
        AddChild(_survivorPanel);
        _survivorPanel.Configure(_icons);

        _team = new AgentRoster { Name = "Team" };
        AddChild(_team);

        _team.Configure(_band.Agents, _camp, _icons);
        _team.Paint(RingColors);
        _team.Picked += agent => _commands.Execute(PlayerCommand.Select(agent.Survivor.Id));

        // Double-clic sur une fiche : le meme geste que sur la carte, et le meme effet.
        _team.Centered += agent =>
        {
            _commands.Execute(PlayerCommand.Select(agent.Survivor.Id));
            FollowWithCamera(agent);
        };
    }

    // --- Boucle ---------------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        // Rien ne tourne tant que le joueur n'a pas choisi. Une partie finie se laisse regarder : les
        // corps s'arretent, les panneaux restent. Rien ne gagnerait a faire tourner dix systemes
        // derriere un verdict qui ne changera plus.
        if (!_started || _tracker.IsOver) return;

        _world.Advance(delta);

        // Les corps bougent a chaque frame ; les cerveaux tournent moins souvent (section A).
        _entities.Advance((float)delta);
        _band.Move((float)delta);
        EmitFootsteps();

        _sinceZombieThink += delta;
        if (_sinceZombieThink >= _config.Zombies.UpdateInterval)
        {
            _sinceZombieThink = 0d;
            _entities.Think(_world.Time);
        }

        _noiseOverlay.Refresh(_world.Time);

        _sincePerception += delta;
        if (_sincePerception >= _config.Perception.UpdateInterval)
        {
            double elapsed = _sincePerception;
            _sincePerception = 0d;
            UpdatePerception(elapsed);
        }

        // Le partage a sa propre cadence — une fois par seconde (document A) —, et il vient entre la
        // perception et la decision : un survivant doit pouvoir agir dans la seconde sur ce qu'on
        // vient de lui apprendre.
        _band.ShareKnowledge(_world.Time);

        // Une seule passe pour le reste : chaque cerveau guette les evenements, reevalue ses
        // priorites, replanifie si besoin, puis fait avancer son action. La Utility AI tourne 1 a 2
        // fois par seconde et la planification reste « uniquement a la demande » (section A) — ce
        // sont ses cadences a lui, pas celles de la frame.
        _band.Think((float)delta);

        // Et pour finir : ou en est la mission ? La question vient apres tout le reste, sur le monde
        // tel que la passe vient de le laisser.
        _tracker.Evaluate(_world.Time);

        if (_tracker.IsOver) UpdateHud();
    }

    /// <summary>
    /// Se deplacer fait du bruit (specification section 18.1). Franchir une porte fermee, c'est
    /// l'ouvrir : le bareme le facture deux fois plus cher qu'un pas. Le bareme vaut pour tout le
    /// monde — courir s'entend, quel que soit celui qui court.
    /// </summary>
    private void EmitFootsteps()
    {
        foreach (var agent in _band.Standing)
        {
            var cell = agent.Follower.CurrentCell;

            // Un bruit par case franchie, pas un par frame.
            if (_footprints.TryGetValue(agent.Survivor.Id, out var previous) && previous == cell) continue;
            _footprints[agent.Survivor.Id] = cell;

            var action = _map.GetCell(cell) == CellType.DoorClosed
                ? NoiseAction.OpenDoor
                : _running ? NoiseAction.Run : NoiseAction.Walk;

            float level = _config.Noise.LevelOf(action);
            var noise = _world.Noises.EmitRaw(cell, level, _world.Time, agent.Survivor.Id, action);

            // Les pas ne meritent pas une ligne de journal ; ouvrir une porte, si.
            if (action == NoiseAction.OpenDoor && noise is not null)
                Trace.Print($"[Bruit] {agent.Survivor.Name} : {noise}.");
        }
    }

    /// <summary>Derniere case ou chaque survivant a pose le pied, pour ne sonner qu'aux changements.</summary>
    private readonly Dictionary<string, GridPosition> _footprints = [];

    /// <summary>
    /// Une passe de perception (5 a 10 fois par seconde, cf. section A) : chaque survivant regarde
    /// autour de lui et met a jour sa carte mentale, qui est aussi sa carte de navigation.
    /// </summary>
    private void UpdatePerception(double elapsed = 0d)
    {
        _band.Perceive(elapsed, _noiseRng);

        NoticeImportantEvents();

        if (Current.LastPerception is { } perception)
            _knowledgeOverlay.SetObservation(perception.VisibleCells, _world.Time);

        _overlay.Refresh();
        UpdateHud();
    }

    /// <summary>
    /// Les evenements qui ne peuvent pas attendre la prochaine reevaluation periodique
    /// (specification section 11.4). La decouverte d'un zombie n'est plus signalee depuis ici : c'est
    /// un des dix declencheurs du livrable 6, et <see cref="ReplanMonitor"/> le deduit de la memoire
    /// du survivant — donc aussi pour un zombie appris par le bruit ou par un allie.
    /// </summary>
    private void NoticeImportantEvents()
    {
        foreach (var agent in _band.Standing)
        {
            foreach (var heard in agent.LastHeard)
            {
                if (heard.PerceivedIntensity < _config.UtilityAi.LoudNoiseIntensity) continue;

                agent.Utility.Request($"bruit fort entendu vers {heard.BelievedPosition}");
                break;
            }
        }
    }

    // --- Entrees --------------------------------------------------------------------------------

    public override void _UnhandledInput(InputEvent @event)
    {
        // Echap ferme le jeu meme depuis l'ecran de preparation : c'est la seule touche qui doit
        // repondre avant qu'une partie n'existe.
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            GetTree().Quit();
            return;
        }

        if (!_started) return;

        switch (@event)
        {
            case InputEventKey { Pressed: true, Echo: false } key:
                HandleKey(key.Keycode, key.ShiftPressed);
                break;

            case InputEventMouseButton { Pressed: true } mouse:
                HandleClick(mouse);
                break;
        }
    }

    private void HandleKey(Key keycode, bool toAll)
    {
        if (DebugKey(keycode)) return;

        switch (keycode)
        {
            case Key.Tab:
                _commands.SelectNext();
                Trace.Print($"[Ordre] Survivant selectionne : {Current.Survivor.Name}.");
                break;

            case Key.G:
                _commands.Execute(PlayerCommand.SelectGroup());
                break;

            case Key.R:
                ToggleRun();
                break;

            case Key.Key1: _commands.Execute(PlayerCommand.FetchObjective(toAll)); break;
            case Key.Key2: _commands.Execute(PlayerCommand.ReachExtraction(toAll)); break;
            case Key.Key4: _commands.Execute(PlayerCommand.Explore(ZoneUnderCursor(), toAll)); break;
            case Key.Key5: _commands.Execute(PlayerCommand.Regroup(toAll)); break;

            case Key.B: ToggleNoiseBan(toAll); break;
            case Key.Z: ToggleForbiddenZone(toAll); break;
        }
    }

    /// <summary>
    /// Les touches de l'observateur, et non celles du joueur : bascules d'overlay, danger pose a la
    /// main, distraction. Elles obeissent a <c>debug.commands</c> — sans quoi une partie livree
    /// laisserait n'importe qui deplacer une horde d'une pression sur la barre d'espace.
    /// </summary>
    private bool DebugKey(Key keycode)
    {
        bool debug = keycode is Key.F1 or Key.F2 or Key.F3 or Key.F4
            or Key.F5 or Key.F6 or Key.F7 or Key.C or Key.Space;

        // Mode debug eteint : la touche est reconnue puis ignoree, jamais reinterpretee. Laisser
        // Espace retomber sur autre chose serait pire que de ne rien faire.
        if (debug && !_config.Debug.AcceptsCommands) return true;

        switch (keycode)
        {
            case Key.F1:
                _overlay.Visible = !_overlay.Visible;
                Trace.Print($"[Debug] Overlay de navigation {(_overlay.Visible ? "affiche" : "masque")}.");
                return true;

            case Key.F2:
                _knowledgeOverlay.Visible = !_knowledgeOverlay.Visible;
                Trace.Print($"[Debug] Carte mentale {(_knowledgeOverlay.Visible ? "affichee" : "masquee")}.");
                return true;

            case Key.F3:
                _noiseOverlay.Visible = !_noiseOverlay.Visible;
                Trace.Print($"[Debug] Bruits et zombies {(_noiseOverlay.Visible ? "affiches" : "masques")}.");
                return true;

            case Key.F4:
                _agentPanel.ShowScoreDetail = !_agentPanel.ShowScoreDetail;
                UpdateHud();
                return true;

            case Key.F5:
                _planPanel.Visible = !_planPanel.Visible;
                UpdateHud();
                return true;

            case Key.F6:
                _planPanel.ShowJournal = !_planPanel.ShowJournal;
                UpdateHud();
                return true;

            case Key.F7:
                _hud.ShowBandDetail = !_hud.ShowBandDetail;
                Trace.Print($"[Debug] Detail du groupe {(_hud.ShowBandDetail ? "affiche" : "masque")}.");
                UpdateHud();
                return true;

            case Key.C:
                Current.Navigation.ClearDanger();
                Trace.Print($"[Navigation] Zones de danger de {Current.Survivor.Name} effacees.");
                Current.Replan("danger efface");
                _overlay.Refresh();
                return true;

            case Key.Space:
                ThrowDistraction(WorldToCell(GetGlobalMousePosition()));
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// « Interdire » ou « autoriser les actions bruyantes ». Une seule touche pour les deux commandes
    /// de la specification : l'etat courant du survivant affiche decide laquelle est envoyee.
    /// </summary>
    private void ToggleNoiseBan(bool toAll)
    {
        bool quiet = !Current.Survivor.Orders.NoiseAllowed;

        _commands.Execute(quiet
            ? PlayerCommand.AllowNoise(toAll || _commands.GroupSelected)
            : PlayerCommand.ForbidNoise(toAll || _commands.GroupSelected));
    }

    /// <summary>« Definir une zone interdite », sur la zone que le curseur survole.</summary>
    private void ToggleForbiddenZone(bool toAll)
    {
        if (ZoneUnderCursor() is not { } zone)
        {
            Trace.Warn("[Ordre] Aucune zone nommee sous le curseur : rien a interdire.");
            return;
        }

        bool already = Current.Survivor.Orders.ForbiddenZones.Any(known => known.Name == zone.Name);

        _commands.Execute(already
            ? PlayerCommand.AllowZone(zone, toAll || _commands.GroupSelected)
            : PlayerCommand.ForbidZone(zone, toAll || _commands.GroupSelected));

        _overlay.Refresh();
    }

    /// <summary>La zone nommee sous le curseur, s'il y en a une. Sert aux ordres qui visent un lieu.</summary>
    private MapZone? ZoneUnderCursor() => _map.ZonesAt(WorldToCell(GetGlobalMousePosition())).FirstOrDefault();

    /// <summary>
    /// Le clic gauche sur la carte, et ses trois lectures.
    ///
    /// <list type="bullet">
    /// <item>Sur un survivant : il devient celui qu'on regarde.</item>
    /// <item>Deux fois sur un survivant : la camera part avec lui. C'est le seul moyen de garder un
    /// oeil sur quelqu'un pendant qu'il traverse le quartier, et cela n'enleve rien au joueur — la
    /// molette continue de zoomer, un glissement au bouton du milieu reprend la main.</item>
    /// <item>A cote : plus personne. Le panneau lateral se ferme, l'anneau s'eteint, la camera cesse
    /// de suivre. Un ecran qui montre une fiche detaillee alors qu'on vient de cliquer ailleurs
    /// laisse croire que le clic n'a pas ete recu.</item>
    /// </list>
    /// </summary>
    private void HandleClick(InputEventMouseButton mouse)
    {
        var cell = WorldToCell(GetGlobalMousePosition());

        switch (mouse.ButtonIndex)
        {
            case MouseButton.Left:
                if (_band.Standing.FirstOrDefault(agent => agent.Survivor.Position == cell) is not { } clicked)
                {
                    Deselect();
                    break;
                }

                _commands.Execute(PlayerCommand.Select(clicked.Survivor.Id));
                if (mouse.DoubleClick) FollowWithCamera(clicked);
                break;

            case MouseButton.Right when _config.Debug.AcceptsCommands:
                CycleDanger(cell);
                break;
        }
    }

    /// <summary>
    /// Colle la camera au corps du survivant. C'est bien le corps qui est suivi et non la case :
    /// <see cref="PathFollower"/> interpole entre deux cellules, et suivre la case donnerait une vue
    /// qui avance par sauts.
    /// </summary>
    private void FollowWithCamera(SurvivorAgent agent)
    {
        // Les corps sont crees dans l'ordre du groupe : le rang suffit a retrouver le bon.
        int rank = _band.Agents.ToList().IndexOf(agent);
        if (rank < 0 || rank >= _nodes.Count || !IsInstanceValid(_nodes[rank])) return;

        var body = _nodes[rank];

        // Un survivant rentre au camp voit son corps libere : la fermeture doit y survivre, sans quoi
        // la camera lirait un noeud detruit a chaque image.
        _camera.Follow(() => IsInstanceValid(body) ? body.GlobalPosition : _camera.GlobalPosition);
        Trace.Print($"[Camera] Suit {agent.Survivor.Name}.");
    }

    /// <summary>Le joueur a clique a cote : on lache celui qu'on regardait, et la camera avec.</summary>
    private void Deselect()
    {
        if (_commands.Deselect()) _camera.Unfollow();
    }

    // --- Bruit ----------------------------------------------------------------------------------

    private void ToggleRun()
    {
        _running = !_running;

        // C'est l'agent qui fixe la vitesse : la penalite de surcharge s'y applique, et un survivant
        // charge ne court pas aussi vite que les autres.
        foreach (var agent in _band.Agents) agent.SetPace(_running);

        Trace.Print(
            $"[Bruit] Le groupe {(_running ? "court" : "marche")} : chaque case fait desormais " +
            $"{_config.Noise.LevelOf(_running ? NoiseAction.Run : NoiseAction.Walk):0.#} de bruit, " +
            "module par la discretion de chacun.");
    }

    /// <summary>
    /// Jette une distraction sur une case (specification section 18.1, niveau 10). C'est le moyen le
    /// plus direct de verifier le critere du livrable 3 : les zombies convergent vers le point vise.
    /// </summary>
    private void ThrowDistraction(GridPosition cell)
    {
        if (!_map.Contains(cell)) return;

        var noise = _world.Noises.Emit(NoiseAction.Distraction, cell, _world.Time, Current.Survivor.Id);
        Trace.Print($"[Bruit] {noise}.");
    }

    // --- Ordres ---------------------------------------------------------------------------------

    private void CycleDanger(GridPosition cell)
    {
        var navigation = Current.Navigation;
        if (!navigation.Map.Contains(cell)) return;

        var next = navigation.GetDanger(cell) switch
        {
            DangerLevel.None => DangerLevel.Suspected,
            DangerLevel.Suspected => DangerLevel.ZombieNearby,
            _ => DangerLevel.None,
        };

        navigation.SetDanger(cell, next);
        Trace.Print($"[Navigation] Danger en {cell} pour {Current.Survivor.Name} : {next}.");

        Current.Replan($"danger modifie en {cell}");
        _overlay.Refresh();
    }

    // --- Affichage ------------------------------------------------------------------------------

    /// <summary>
    /// Rafraichit l'affichage. Ce noeud ne compose plus une seule ligne de texte : chaque panneau
    /// sait ce qu'il a a dire, et le partage entre eux suit celui de la specification — l'etat de la
    /// partie, l'etat d'un survivant, l'etat de la recherche. La barre d'equipe, elle, ne parle que
    /// des jauges, et reste allumee meme sans mode debug.
    /// </summary>
    private void UpdateHud()
    {
        _team.Refresh(_commands.Selected);

        int rank = _commands.SelectedIndex;
        _survivorPanel.RingColor = RingColors[rank % RingColors.Length];
        _survivorPanel.Refresh(_commands.Selected, rank, _world.Time);

        for (int i = 0; i < _nodes.Count; i++)
        {
            if (!IsInstanceValid(_nodes[i])) continue;
            _nodes[i].Selected = _commands.HasSelection && i == _commands.SelectedIndex;
        }

        if (!ShowsText) return;

        _hud.Refresh(_map, _world, _band, _commands, _tracker, _entities.Zombies, _running);
        _agentPanel.Refresh(Current, _world.Time);
        _planPanel.Refresh(Current);
    }

    private void LogMapSummary()
    {
        Trace.Print($"[Carte] Grille {_map.Width} x {_map.Height} (origine {_map.Origin}, {_map.CellCount} cellules).");
        Trace.Print(
            $"[Carte] Sol {_map.CountCells(CellType.Floor)}, murs {_map.CountCells(CellType.Wall)}, " +
            $"portes fermees {_map.CountCells(CellType.DoorClosed)}, portes verrouillees {_map.CountCells(CellType.DoorLocked)}, " +
            $"fenetres {_map.CountCells(CellType.Window)}, pharmacie {_map.CountCells(CellType.Pharmacy)}, " +
            $"camp {_map.CountCells(CellType.Extraction)}, inconnues {_map.CountCells(CellType.Unknown)}.");

        foreach (var zone in _map.Zones) Trace.Print($"[Carte] Lieu : {zone}.");
    }

    private void LogCatalogues()
    {
        if (_goals.Goals.Count == 0) Trace.Warn("[Utility] Aucun objectif charge : personne n'aura d'initiative.");
        else Trace.Print($"[Utility] {_goals.Goals.Count} objectifs charges : {string.Join(", ", _goals.Active.Select(g => g.Name))}.");

        if (_domain.Actions.Count == 0) Trace.Warn("[Plan] Aucune action chargee : personne ne pourra planifier.");
        else Trace.Print(
            $"[Plan] {_domain.Actions.Count} actions et {_domain.Goals.Count} objectifs symboliques charges " +
            $"(limites : {_config.Planner.MaxNodes} noeuds, profondeur {_config.Planner.MaxDepth}, " +
            $"{_config.Planner.MaxTimeMs} ms).");

        Trace.Print(
            $"[Mission] {_catalogue.Missions.Count} missions proposees : " +
            $"{string.Join(", ", _catalogue.Missions.Select(mission => mission.ToString()))}.");

        Trace.Print(
            $"[Partage] Portee de communication {_config.Knowledge.CommunicationRange} cases, " +
            $"echange toutes les {_config.Knowledge.SharingInterval:0.##} s" +
            $"{(_config.Knowledge.RadioEnabled ? ", radio de groupe activee" : string.Empty)}.");

        Trace.Print(
            $"[Objet] {_items.Items.Count} objets et armes charges " +
            $"({_items.OfCategory(ItemCategory.Weapon).Count()} armes, " +
            $"{_items.Items.Count(item => item.ActionModifiers.Count > 0)} d'entre eux ponderent une action).");

        if (_zones.Zones.Count > 0)
        {
            Trace.Print(
                $"[Zone] {_zones.Zones.Count} profils charges : " +
                $"{string.Join(", ", _zones.Zones.Select(zone => zone.ToString()))}.");
        }

        Trace.Print($"[Camp] Stock au depart : {_camp}.");
    }

    // --- Utilitaires ----------------------------------------------------------------------------

    private GridPosition WorldToCell(Vector2 worldPosition) => new(
        Mathf.FloorToInt(worldPosition.X / _tileSize),
        Mathf.FloorToInt(worldPosition.Y / _tileSize));

    private static T LoadJson<T>(string path, Func<string, T> parse, T fallback, string label)
    {
        if (!Godot.FileAccess.FileExists(path))
        {
            Trace.Warn($"[Config] {path} introuvable ; valeurs par defaut utilisees pour la {label}.");
            return fallback;
        }

        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (file is null)
        {
            Trace.Warn($"[Config] Lecture de {path} impossible ; valeurs par defaut utilisees pour la {label}.");
            return fallback;
        }

        try
        {
            var value = parse(file.GetAsText());
            Trace.Print($"[Config] {label} chargee depuis {path}.");
            return value;
        }
        catch (Exception exception)
        {
            Trace.Error($"[Config] {path} invalide ({exception.Message}) ; valeurs par defaut utilisees.");
            return fallback;
        }
    }
}

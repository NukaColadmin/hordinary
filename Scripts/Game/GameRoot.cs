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
/// Une partie se deroule desormais en trois temps, et ce noeud les enchaine :
/// <list type="number">
/// <item>Le joueur choisit ses agents et sa mission (<see cref="MissionSetupScreen"/>). Rien ne
/// tourne encore : la carte est la, personne n'est dessus.</item>
/// <item>La mission se joue. Elle commence au camp et s'y termine.</item>
/// <item>La caisse rentre au camp : la mission est remplie, mais la partie <b>continue</b>. Celui
/// qui l'a rapportee repart chercher les autres (<see cref="MissionRecall"/>), et chacun disparait
/// de la carte en annoncant qu'il est rentre. Le verdict ne tombe qu'une fois le terrain vide.</item>
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

    private NavigationDebugOverlay _overlay = null!;
    private KnowledgeDebugOverlay _knowledgeOverlay = null!;
    private NoiseDebugOverlay _noiseOverlay = null!;

    private Hud _hud = null!;
    private AgentPanel _agentPanel = null!;
    private PlanPanel _planPanel = null!;
    private AgentRoster _team = null!;
    private MissionSetupScreen _setup = null!;

    private CommandManager _commands = null!;
    private MissionDefinition _mission = null!;
    private MissionTracker _tracker = null!;
    private MissionRecall _recall = null!;

    private int _tileSize = 16;

    /// <summary>Faux tant que le joueur n'a pas valide son equipe et sa mission.</summary>
    private bool _started;

    private readonly List<SurvivorNode> _nodes = [];
    private readonly Dictionary<string, ZombieNode> _zombieNodes = [];

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
        Trace.Print($"[Config] configuration lue depuis {GameConfigPath}.");

        var legend = LoadJson(TileLegendPath, TileLegend.FromJson, new TileLegend(), "legende des tuiles");
        _tileSize = legend.TileSize;

        BuildMap(legend);

        _noiseRng = new Random(_config.Zombies.Seed);

        _goals = LoadJson(UtilityGoalsPath, UtilityGoalSet.FromJson, UtilityGoalSet.Empty, "objectifs");
        _domain = LoadJson(PlannerDomainPath, PlanningDomain.FromJson, PlanningDomain.Empty, "domaine de planification");
        _roster = LoadJson(SurvivorsPath, SurvivorRoster.FromJson, SurvivorRoster.Empty, "survivants");
        _catalogue = LoadJson(MissionsPath, MissionCatalogue.FromJson, MissionCatalogue.Empty, "missions");

        CreateCamera();

        LogMapSummary();
        LogCatalogues();

        ShowSetup();
    }

    // --- Preparation ----------------------------------------------------------------------------

    /// <summary>
    /// Le premier ecran : la grille d'agents en bas, les missions au-dessus, et un bouton. La carte
    /// est deja construite et cadree derriere — le joueur choisit son equipe en voyant le terrain
    /// sur lequel il l'envoie.
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
            Trace.Print("[Debug] Ecran de preparation saute (config debug.skip_setup).");
            Begin(_catalogue.Missions[0], _roster.Survivors);
            return;
        }

        _setup = new MissionSetupScreen { Name = "Setup" };
        AddChild(_setup);

        _setup.Configure(_roster.Survivors, _catalogue.Missions, _config.Survivor);
        _setup.Started += Begin;
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

        PlaceSupplies(mission);
        CreateBand(team, mission);

        if (_band.Agents.Count == 0) return;

        CreateHorde(mission);
        CreateOverlays();
        CreateHud();
        CreateSurvivorNodes();
        CreateZombieNodes();

        _commands = new CommandManager(_band, _map, _config.UtilityAi);
        _commands.SelectionChanged += _ => Follow();
        _commands.Executed += result => Trace.Print($"[Ordre] {result}.");

        _tracker = new MissionTracker(_world, _band, mission);
        _recall = new MissionRecall(_band, _map, mission, _config.Mission);

        WatchMission();
        ApplyDebugSettings();
        Follow();
        UpdatePerception();

        _started = true;
    }

    /// <summary>
    /// Le rappel du groupe. C'est le seul cablage de ce noeud qui ait encore l'air d'une regle, et
    /// il n'en est pas une : <see cref="MissionTracker"/> constate que la caisse est rentree,
    /// <see cref="MissionRecall"/> fait partir le messager. Ce noeud ne fait que les presenter.
    /// </summary>
    private void WatchMission()
    {
        _tracker.Accomplished += courier =>
        {
            Trace.Print($"[Mission] {_mission.Name} remplie par {courier.Name} (t = {_tracker.DoneAt:0.0} s).");

            if (_band.Find(courier.Id) is { } messenger) _recall.Begin(messenger);
        };

        _tracker.Finished += verdict => Trace.Print($"[Partie] {verdict} (t = {verdict.At:0.0} s).");

        _recall.Said += line => Trace.Print($"[Rappel] {line}");

        _recall.CameHome += survivor =>
        {
            // Le corps quitte la carte : le survivant est rentre, il n'est pas mort. Sa fiche reste
            // dans la barre du bas, eteinte — la faire disparaitre laisserait un trou dans l'equipe.
            var body = _nodes.FirstOrDefault(node => node.Name.ToString() == survivor.Id);
            if (body is not null && IsInstanceValid(body)) body.QueueFree();

            UpdateHud();
        };
    }

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

    private void BuildMap(TileLegend legend)
    {
        var layers = new List<TileMapLayer>();
        foreach (var layerName in LayerNames)
        {
            if (GetNodeOrNull<TileMapLayer>(layerName) is { } layer) layers.Add(layer);
            else Trace.Warn($"[Carte] Couche '{layerName}' introuvable dans la scene.");
        }

        var result = new MapLoader(legend).Load(layers);
        _map = result.Map;

        foreach (var warning in result.Warnings) Trace.Warn($"[Carte] {warning}");

        var locations = LoadJson(MapLocationsPath, MapLocationsConfig.FromJson, MapLocationsConfig.Empty, "lieux");
        locations.ApplyTo(_map);

        _world = new WorldState(_map, _config.Noise);

        int index = 0;
        foreach (var item in result.Items)
        {
            string id = $"{item.Kind}-{++index}".ToLowerInvariant();
            _world.SetItem(id, ItemKind.Medicine, item.Position);
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
    /// Enrole les profils que le joueur a retenus dans l'ecran de preparation. Aucun d'eux n'est
    /// nomme dans le code : Alice, Bob et Claire sont des donnees, et un quatrieme profil entrerait
    /// en jeu sans recompilation.
    /// </summary>
    private void CreateBand(IReadOnlyList<SurvivorProfile> team, MissionDefinition mission)
    {
        _band = new SurvivorBand(_world, _config);

        // Les agents commencent et finissent au meme endroit : le camp est le point de depart.
        var spawn = FindSpawn(mission);

        foreach (var profile in team)
        {
            var agent = _band.Enlist(profile, spawn, _goals, _domain);
            Watch(agent);

            Trace.Print(
                $"[Agent] {profile.Name} ({profile.Role}) demarre en {spawn} — " +
                $"vision {agent.Survivor.VisionRange} cases, {profile.Personality}, " +
                $"discretion x{profile.NoiseMultiplier:0.##}, {profile.Skills.Count} competences, " +
                $"HP {agent.Survivor.Health:0}/{agent.Survivor.Settings.MaxHealth:0}, " +
                $"SANITY {agent.Survivor.Sanity:0}/{agent.Survivor.Settings.MaxSanity:0}.");
        }

        // Briefing du joueur : les lieux de la mission, pas leur contenu. C'est une connaissance
        // transmise, pas une observation — le terrain reste entierement a decouvrir.
        _band.Brief([.. mission.BriefedZones()]);

        _band.Sharing.Exchanged += exchange =>
        {
            if (exchange.Learned > 0) Trace.Print($"[Partage] {exchange}.");
        };
    }

    /// <summary>Branche les journaux d'un agent sur la console Godot.</summary>
    private void Watch(SurvivorAgent agent)
    {
        string who = agent.Survivor.Name;

        agent.Logged += message => Trace.Print($"[Plan] {who} : {message}.");

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

        agent.Brain.Journal.Written += entry =>
        {
            if (entry.Kind is DecisionKind.Action) return;
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
        int spawned = _entities.SpawnHorde(FindSpawn(mission), mission.ZombieCount);

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
            $"[Monde] {spawned} zombies places (graine {_config.Zombies.Seed}, " +
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
        if (!victim.Alive || victim.Returned) return;

        victim.Knowledge.RememberEntity(zombie.Id, EntityKind.Zombie, zombie.Position, _world.Time);
        victim.TakeDamage(_config.Zombies.AttackDamage);

        Trace.Print(
            $"[Combat] {zombie.Id} frappe {victim.Name} : sante {victim.Health:0}, " +
            $"panique {victim.Panic:P0}, motivation {victim.Sanity:0}.");

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

    private GridPosition FindSpawn(MissionDefinition mission)
    {
        if (mission.CampIn(_map) is { } camp && _map.FindWalkableIn(camp) is { } cell) return cell;

        Trace.Warn("[Carte] Aucun camp praticable ; repli sur la premiere case libre.");
        return _map.AllPositions().FirstOrDefault(_map.IsWalkable);
    }

    private void CreateCamera()
    {
        var camera = new CameraController { Name = "Camera" };
        AddChild(camera);
        camera.FrameMap(_map, _tileSize);
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

        _team = new AgentRoster { Name = "Team" };
        AddChild(_team);

        _team.Configure(_band.Agents);
        _team.Picked += agent => _commands.Execute(PlayerCommand.Select(agent.Survivor.Id));
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
        // tel que la passe vient de le laisser. Remplir la mission n'arrete rien — c'est le rappel
        // qui demarre, et le verdict n'arrive qu'une fois le terrain vide.
        _tracker.Evaluate(_world.Time);
        _recall.Advance(_world.Time);

        if (_tracker.IsOver) UpdateHud();
    }

    /// <summary>
    /// Se deplacer fait du bruit (specification section 18.1). Franchir une porte fermee, c'est
    /// l'ouvrir : le bareme le facture deux fois plus cher qu'un pas. La discretion du profil
    /// (livrable 7) s'applique ici : Alice ne s'entend pas comme Bob.
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

            float level = agent.Profile.NoiseOf(_config.Noise.LevelOf(action));
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

            case Key.Key1: _commands.Execute(PlayerCommand.FetchMedicine(toAll)); break;
            case Key.Key2: _commands.Execute(PlayerCommand.ReachExtraction(toAll)); break;
            case Key.Key3: SendToCamp(toAll); break;
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
    /// « Rentrez au camp. » Ce n'est pas une commande de la specification, mais c'est celle qui rend
    /// la boucle jouable : le joueur peut rappeler son equipe sans attendre que la mission soit
    /// remplie — et un agent rappele qui atteint le camp en sort, comme les autres.
    /// </summary>
    private void SendToCamp(bool toAll)
    {
        var addressed = toAll || _commands.GroupSelected
            ? _band.Standing.ToList()
            : [Current];

        foreach (var agent in addressed)
        {
            if (!_recall.Abandon(agent, "rappele au camp par le joueur"))
                Trace.Warn($"[Ordre] {agent.Survivor.Name} rentrait deja.");
        }
    }

    private void HandleClick(InputEventMouseButton mouse)
    {
        var cell = WorldToCell(GetGlobalMousePosition());

        switch (mouse.ButtonIndex)
        {
            case MouseButton.Left:
                // Un clic sur un survivant le selectionne.
                if (_band.Standing.FirstOrDefault(agent => agent.Survivor.Position == cell) is { } clicked)
                    _commands.Execute(PlayerCommand.Select(clicked.Survivor.Id));
                break;

            case MouseButton.Right when _config.Debug.AcceptsCommands:
                CycleDanger(cell);
                break;
        }
    }

    // --- Bruit ----------------------------------------------------------------------------------

    private void ToggleRun()
    {
        _running = !_running;

        foreach (var agent in _band.Agents)
        {
            agent.Follower.Speed = _running
                ? agent.Survivor.Settings.RunSpeed
                : agent.Survivor.Settings.WalkSpeed;
        }

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

        for (int i = 0; i < _nodes.Count; i++)
        {
            if (!IsInstanceValid(_nodes[i])) continue;
            _nodes[i].Selected = i == _commands.SelectedIndex;
        }

        if (!ShowsText) return;

        _hud.Refresh(_map, _world, _band, _commands, _tracker, _recall, _entities.Zombies, _running);
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

using Hordinary.AI.Instructions;
using Hordinary.AI.Navigation;
using Hordinary.AI.Planning;
using Hordinary.AI.Planning.Pddl;
using Hordinary.AI.Replanning;
using Hordinary.AI.Utility;
using Hordinary.Agents;
using Hordinary.Config;
using Hordinary.Game;
using Hordinary.World;

namespace Hordinary.Tests;

/// <summary>
/// La boucle de jeu complete, sans Godot, sur la carte livree :
/// perception → connaissances → partage → Utility AI → GBFS → A* → execution → replanification.
///
/// C'est le meme assemblage que <c>GameRoot</c>, aux entrees du joueur et a l'affichage pres : depuis
/// le livrable 7 le cablage lui-meme vit dans le coeur (<see cref="SurvivorBand"/> et
/// <see cref="SurvivorAgent"/>), et ce harnais ne fait plus que le derouler. Les deux couches ne
/// peuvent donc pas verifier deux boucles differentes.
///
/// Par defaut la mission ne compte qu'Alice — c'est le cas de figure des livrables 5 et 6, ou un
/// survivant seul suffit a montrer qu'un plan aboutit et qu'il change quand le monde change. Passer
/// <c>team: true</c> enrole les trois profils de <c>config/survivors.json</c> et arme le partage de
/// connaissances : c'est le cas du livrable 7.
/// </summary>
internal sealed class MissionRun
{
    public const float Step = 0.1f;
    public const int DefaultMaxTicks = 6000;

    /// <summary>Position de l'objet de la mission sur la carte de demonstration (interieur de la pharmacie).</summary>
    public static readonly GridPosition ObjectiveCell = new(11, 4);

    /// <summary>Porte de la pharmacie sur la carte livree. Fermee, jamais verrouillee (voir README).</summary>
    public static readonly GridPosition PharmacyDoor = new(6, -2);

    public static GameConfig Config { get; } = GameConfig.FromJson(DemoMap.ReadConfig("game_config.json"));

    public static UtilityGoalSet Goals { get; } = UtilityGoalSet.FromJson(DemoMap.ReadConfig("utility_goals.json"));

    public static PlanningDomain Domain { get; } = PlanningDomain.FromJson(DemoMap.ReadConfig("planner_domain.json"));

    public static SurvivorRoster Roster { get; } = SurvivorRoster.FromJson(DemoMap.ReadConfig("survivors.json"));

    public static MissionCatalogue Catalogue { get; } = MissionCatalogue.FromJson(DemoMap.ReadConfig("missions.json"));

    /// <summary>
    /// Un survivant seul, hors carte et hors mission, arme des vrais profils et du vrai catalogue
    /// d'objets. C'est ce dont ont besoin les tests qui portent sur le survivant lui-meme — son sac,
    /// ce qu'une expedition lui fait — et pas sur le monde ou il se trouve.
    /// </summary>
    public static Survivor Lone(string id, GridPosition at, int size = 20)
    {
        var profile = Roster.Find(id)
            ?? throw new InvalidOperationException($"Profil « {id} » absent de config/survivors.json.");

        var knowledge = new AgentKnowledge(id, size, size, default, Config.Knowledge);
        var survivor = Survivor.FromProfile(profile, knowledge, Config.Perception, Config.Survivor, DemoMap.Items, Config.Inventory);
        survivor.Position = at;
        return survivor;
    }

    /// <summary>Graine de l'erreur de localisation des bruits, quand le test en demande une.</summary>
    private readonly Random? _noise;

    public GridMap Map { get; }
    public WorldState World { get; }
    public SurvivorBand Band { get; }

    /// <summary>Ce qui a ete demande au groupe : ou chercher, quoi rapporter, ou rentrer.</summary>
    public MissionDefinition Definition { get; }

    /// <summary>Ou en est la mission. Comme dans <c>GameRoot</c>, c'est la boucle qui l'interroge.</summary>
    public MissionTracker Tracker { get; }

    /// <summary>Les compteurs du livrable 16, quand la mission jouee est une offre generee.</summary>
    public MissionObjectiveTracker? Objectives { get; }

    /// <summary>L'agent principal. Les tests des livrables 5 et 6 ne connaissent que lui.</summary>
    public SurvivorAgent Agent { get; }

    /// <summary>Tout ce que la simulation raconte : execution, planification, replanification, partage.</summary>
    public List<string> Journal { get; } = [];

    public List<string> Executed { get; } = [];
    public List<string> Failures { get; } = [];
    public List<ReplanTrigger> Triggers { get; } = [];
    public List<ReplanRecord> Replans { get; } = [];
    public List<KnowledgeExchange> Exchanges { get; } = [];

    /// <summary>Chaque plan retenu, dans l'ordre. Deux entrees signifient qu'un plan en a remplace un autre.</summary>
    public List<Plan> Plans { get; } = [];

    // --- Raccourcis vers l'agent principal, pour que les tests restent lisibles ------------------

    public Survivor Alice => Agent.Survivor;
    public Replanner Brain => Agent.Brain;
    public PlanExecutor Executor => Agent.Executor;
    public PathFollower Follower => Agent.Follower;
    public Plan? Plan => Agent.Brain.Plan;

    /// <param name="map">Terrain a jouer ; par defaut la carte de demonstration livree.</param>
    /// <param name="lockedDoors">Portes a verrouiller dans le monde reel avant le depart.</param>
    /// <param name="replanning">Reglages de replanification, pour resserrer les cadences en test.</param>
    /// <param name="objective">Ou poser l'objet de la mission ; par defaut <see cref="ObjectiveCell"/>.</param>
    /// <param name="withObjective">A <c>false</c>, la pharmacie est vide : fouiller n'y donnera rien.</param>
    /// <param name="team">A <c>true</c>, les trois survivants du livrable 7 partent ensemble.</param>
    /// <param name="definition">La mission jouee ; par defaut la premiere de <c>config/missions.json</c>.</param>
    /// <param name="settings">
    /// Reglages de mission — le temps imparti, essentiellement. Par defaut ceux du fichier livre :
    /// une mission de test est chronometree comme une vraie. Un test qui observe autre chose que
    /// l'horloge — une memoire qui s'efface, un plan qui se rebatit — passe un
    /// <c>TimeLimit = 0</c> pour que le van ne parte pas au milieu de sa mesure.
    /// </param>
    public MissionRun(
        GridMap? map = null,
        IEnumerable<GridPosition>? lockedDoors = null,
        ReplanningSettings? replanning = null,
        GridPosition? objective = null,
        bool withObjective = true,
        bool team = false,
        MissionDefinition? definition = null,
        MissionSettings? settings = null,
        IEnumerable<string>? instructions = null,
        int? noiseSeed = null,
        PlannerSettings? planner = null)
    {
        // Sans graine de bruit, l'erreur de localisation tire dans Random.Shared et deux parcours de
        // la meme partie divergent des qu'un bruit est entendu de loin. Le defaut reste celui du jeu ;
        // un test qui compare deux deroules en passe une.
        _noise = noiseSeed is { } seed ? new Random(seed) : null;

        Map = map ?? DemoMap.Load();

        foreach (var cell in lockedDoors ?? []) Map.SetCell(cell, CellType.DoorLocked);

        World = new WorldState(Map, Config.Noise);

        // Une mission generee (livrable 16) pose ce que ses exigences reclament, la ou elle les
        // reclame : c'est GameRoot.PlaceSupplies, mais dans le harnais. Une mission ecrite a la main
        // garde la caisse unique des livrables 5 a 8, a la case que le test choisit.
        if (withObjective && (definition?.IsOffer ?? false)) PlacePayload(definition);
        else if (withObjective) World.SetItem("objective-1", Survivor.DefaultObjectiveItem, objective ?? ObjectiveCell);

        // Les tests du livrable 6 resserrent les cadences de replanification pour ne pas simuler des
        // minutes de jeu ; le reste de la configuration livree ne bouge pas.
        var config = Config;
        if (replanning is not null) config = config with { Replanning = replanning };

        // Le budget de planification est une horloge (planner.max_time_ms). Un test qui compare deux
        // deroules doit donc pouvoir le desserrer : sinon la suite de plans depend de la charge de la
        // machine, et non de la simulation.
        if (planner is not null) config = config with { Planner = planner };

        Band = new SurvivorBand(World, config, catalogue: DemoMap.Items);

        var profiles = team
            ? Roster.Survivors
            : [Roster.Find("alice") ?? throw new InvalidOperationException("Aucun profil « alice ».")];

        Definition = definition
            ?? Catalogue.Default
            ?? throw new InvalidOperationException("Aucune mission dans config/missions.json.");

        // Les agents commencent et finissent au meme endroit : le camp est le point de depart.
        var start = Spawn(Definition.Camp);

        // Les instructions tactiques du joueur (livrable 15), avant l'enrolement comme dans GameRoot :
        // un survivant nait avec le repertoire que la consigne laisse. Un texte refuse par un
        // portillon fait echouer le harnais plutot que de partir en silence — un test qui croirait
        // mesurer l'effet d'une contrainte inactive ne mesurerait rien.
        Band.Instruct(Instruct(instructions));

        foreach (var profile in profiles) Watch(Band.Enlist(profile, start, Goals, Domain));

        // Ce qu'on les envoie chercher, et ou — comme dans GameRoot. Le domaine ne parle que d'un
        // « objective » et d'une « objective_zone » : ces deux lignes sont ce qui leur donne un sens,
        // et sans elles un survivant de test ne sait pas ou aller fouiller.
        foreach (var agent in Band.Agents)
        {
            agent.Survivor.ObjectiveItem = Definition.Item;

            if (Definition.ObjectiveIn(Map) is { } where)
                agent.Survivor.ObjectiveZone = new ObjectiveSite(where.Name, where.Kind);
        }

        // Briefing du joueur : les lieux de la mission, pas leur contenu (comme dans GameRoot).
        Band.Brief([.. Definition.BriefedZones()]);

        if (Definition.ObjectiveIn(Map) is { } objectiveZone) Band.Brief(objectiveZone);

        Objectives = Definition.IsOffer
            ? new MissionObjectiveTracker(Definition, World, Band, DemoMap.Items)
            : null;

        Tracker = new MissionTracker(World, Band, Definition, settings ?? config.Mission, Objectives);

        Tracker.Finished += verdict => Journal.Add(verdict.ToString());

        Band.Sharing.Exchanged += exchange =>
        {
            Exchanges.Add(exchange);
            if (exchange.Learned > 0) Journal.Add(exchange.ToString());
        };

        Agent = Band.Agents[0];

        Band.Perceive();
    }

    /// <summary>Branche les journaux d'un agent sur ceux de la mission.</summary>
    private void Watch(SurvivorAgent agent)
    {
        agent.Logged += Journal.Add;
        agent.Fought += Journal.Add;
        agent.Executor.ActionCompleted += action => Executed.Add(action.Schema.Name);
        agent.Executor.ActionFailed += (action, failure) => Failures.Add($"{action.Schema.Name} : {failure.Reason}");
        agent.Brain.Monitor.Detected += Triggers.Add;
        agent.Brain.Replanned += Replans.Add;
        agent.Brain.Journal.Written += entry => Journal.Add(entry.Message);

        agent.Planner.Planned += result =>
        {
            if (result.Plan is { } plan) Plans.Add(plan);
        };
    }

    /// <summary>
    /// Pose ce qu'une mission generee demande de rapporter, dans sa zone cible — la meme regle que
    /// <c>GameRoot.PlaceSupplies</c> : les cases les plus eloignees du camp d'abord, pour qu'un
    /// objectif ne se trouve pas en poussant la premiere porte.
    /// </summary>
    private void PlacePayload(MissionDefinition definition)
    {
        if (definition.ObjectiveIn(Map) is not { } zone) return;

        var camp = definition.CampIn(Map)?.Center ?? default;

        var cells = zone.Positions()
            .Where(Map.IsWalkable)
            .OrderByDescending(cell => cell.ChebyshevDistanceTo(camp))
            .ThenBy(cell => cell.X)
            .ThenBy(cell => cell.Y)
            .ToList();

        if (cells.Count == 0) return;

        int placed = 0;

        foreach (var line in definition.SuppliesFor(DemoMap.Items))
        {
            for (int copy = 0; copy < line.Quantity; copy++)
            {
                World.SetItem($"supply-{++placed}", line.Item, cells[placed % cells.Count]);
            }
        }
    }

    /// <summary>
    /// Les instructions tactiques de la mission (livrable 15), compilees comme <c>GameRoot</c> les
    /// compile : memes portillons, meme contexte, meme domaine exporte.
    ///
    /// Un texte refuse leve ici plutot que de partir en silence. C'est une difference assumee avec le
    /// jeu, ou un refus fait tomber la contrainte et laisse la mission continuer : un test qui
    /// croirait mesurer l'effet d'une contrainte inactive ne mesurerait rien du tout.
    /// </summary>
    private TacticalInstructions? Instruct(IEnumerable<string>? texts)
    {
        var wanted = texts?.ToList() ?? [];
        if (wanted.Count == 0) return null;

        var instructions = new TacticalInstructions(PddlDomainBuilder.From(Domain), Config.Instructions);

        var kinds = new HashSet<string>(
            Map.Zones.Select(zone => SymbolicStateBuilder.KindNameOf(zone.Kind)).OfType<string>(),
            StringComparer.OrdinalIgnoreCase);

        var context = new CompilationContext(Config.Planner.Search, kinds);

        foreach (string text in wanted)
        {
            var entry = instructions.Add(text, context);

            if (!entry.Accepted)
                throw new InvalidOperationException($"« {text} » refusee par le portillon {entry.Gate} : {entry.Reason}");
        }

        return instructions;
    }

    /// <summary>Cellule representative d'un lieu : celle que la memoire retiendra pour lui.</summary>
    public GridPosition Spawn(ZoneKind kind)
    {
        var zone = Map.GetZone(kind) ?? throw new InvalidOperationException($"Aucune zone {kind} sur la carte.");
        return Map.FindWalkableIn(zone) ?? zone.Center;
    }

    /// <summary>
    /// Une passe de la boucle, dans l'ordre de <c>GameRoot</c> : le monde avance, le groupe percoit,
    /// se parle, decide — puis on demande ou en est la mission.
    /// </summary>
    public void Tick()
    {
        World.Advance(Step);
        Band.Advance(Step, _noise);

        Tracker.Evaluate(World.Time);
    }

    /// <summary>Deroule la mission jusqu'a ce que la condition soit remplie, ou jusqu'a l'abandon.</summary>
    public bool RunUntil(Func<bool> done, int maxTicks = DefaultMaxTicks)
    {
        for (int tick = 0; tick < maxTicks; tick++)
        {
            if (done()) return true;
            Tick();
        }

        return done();
    }

    public SurvivorAgent AgentOf(string id)
        => Band.Find(id) ?? throw new InvalidOperationException($"Aucun survivant « {id} » dans le groupe.");

    /// <summary>Ce qu'il faut savoir pour comprendre un echec de test.</summary>
    public string Trace()
        => $"Actions executees : {string.Join(", ", Executed)}. "
         + $"Echecs : {string.Join(" | ", Failures.Distinct())}. "
         + $"Declencheurs : {string.Join(" | ", Triggers.Select(t => t.Reason).Distinct())}. "
         + $"Journal : {string.Join(" | ", Journal.TakeLast(12))}";
}

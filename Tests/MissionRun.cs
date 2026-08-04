using Hordinary.AI.Navigation;
using Hordinary.AI.Planning;
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

    /// <summary>Position des medicaments sur la carte de demonstration (interieur de la pharmacie).</summary>
    public static readonly GridPosition MedicineCell = new(11, 4);

    /// <summary>Porte de la pharmacie sur la carte livree. Fermee, jamais verrouillee (voir README).</summary>
    public static readonly GridPosition PharmacyDoor = new(6, -2);

    public static GameConfig Config { get; } = GameConfig.FromJson(DemoMap.ReadConfig("game_config.json"));

    public static UtilityGoalSet Goals { get; } = UtilityGoalSet.FromJson(DemoMap.ReadConfig("utility_goals.json"));

    public static PlanningDomain Domain { get; } = PlanningDomain.FromJson(DemoMap.ReadConfig("planner_domain.json"));

    public static SurvivorRoster Roster { get; } = SurvivorRoster.FromJson(DemoMap.ReadConfig("survivors.json"));

    public static MissionCatalogue Catalogue { get; } = MissionCatalogue.FromJson(DemoMap.ReadConfig("missions.json"));

    public GridMap Map { get; }
    public WorldState World { get; }
    public SurvivorBand Band { get; }

    /// <summary>Ce qui a ete demande au groupe : ou chercher, quoi rapporter, ou rentrer.</summary>
    public MissionDefinition Definition { get; }

    /// <summary>Ou en est la mission. Comme dans <c>GameRoot</c>, c'est la boucle qui l'interroge.</summary>
    public MissionTracker Tracker { get; }

    /// <summary>Le rappel au camp, arme des que la caisse rentre.</summary>
    public MissionRecall Recall { get; }

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
    /// <param name="medicine">Ou poser les medicaments ; par defaut <see cref="MedicineCell"/>.</param>
    /// <param name="withMedicine">A <c>false</c>, la pharmacie est vide : fouiller n'y donnera rien.</param>
    /// <param name="team">A <c>true</c>, les trois survivants du livrable 7 partent ensemble.</param>
    /// <param name="definition">La mission jouee ; par defaut la premiere de <c>config/missions.json</c>.</param>
    public MissionRun(
        GridMap? map = null,
        IEnumerable<GridPosition>? lockedDoors = null,
        ReplanningSettings? replanning = null,
        GridPosition? medicine = null,
        bool withMedicine = true,
        bool team = false,
        MissionDefinition? definition = null)
    {
        Map = map ?? DemoMap.Load();

        foreach (var cell in lockedDoors ?? []) Map.SetCell(cell, CellType.DoorLocked);

        World = new WorldState(Map, Config.Noise);

        if (withMedicine) World.SetItem("medicine-1", ItemKind.Medicine, medicine ?? MedicineCell);

        // Les tests du livrable 6 resserrent les cadences de replanification pour ne pas simuler des
        // minutes de jeu ; le reste de la configuration livree ne bouge pas.
        var config = replanning is null ? Config : Config with { Replanning = replanning };
        Band = new SurvivorBand(World, config);

        var profiles = team
            ? Roster.Survivors
            : [Roster.Find("alice") ?? throw new InvalidOperationException("Aucun profil « alice ».")];

        Definition = definition
            ?? Catalogue.Default
            ?? throw new InvalidOperationException("Aucune mission dans config/missions.json.");

        // Les agents commencent et finissent au meme endroit : le camp est le point de depart.
        var start = Spawn(Definition.Camp);

        foreach (var profile in profiles) Watch(Band.Enlist(profile, start, Goals, Domain));

        // Briefing du joueur : les lieux de la mission, pas leur contenu (comme dans GameRoot).
        Band.Brief([.. Definition.BriefedZones()]);

        Tracker = new MissionTracker(World, Band, Definition);
        Recall = new MissionRecall(Band, Map, Definition, config.Mission);

        // Le rappel du livrable : celui qui rentre la caisse repart chercher les autres.
        Tracker.Accomplished += courier =>
        {
            Journal.Add($"mission remplie par {courier.Name}");
            if (Band.Find(courier.Id) is { } messenger) Recall.Begin(messenger);
        };

        Recall.Said += Journal.Add;

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

    /// <summary>Cellule representative d'un lieu : celle que la memoire retiendra pour lui.</summary>
    public GridPosition Spawn(ZoneKind kind)
    {
        var zone = Map.GetZone(kind) ?? throw new InvalidOperationException($"Aucune zone {kind} sur la carte.");
        return Map.FindWalkableIn(zone) ?? zone.Center;
    }

    /// <summary>
    /// Une passe de la boucle, dans l'ordre de <c>GameRoot</c> : le monde avance, le groupe percoit,
    /// se parle, decide — puis on demande ou en est la mission, et le rappel fait sa tournee.
    /// </summary>
    public void Tick()
    {
        World.Advance(Step);
        Band.Advance(Step);

        Tracker.Evaluate(World.Time);
        Recall.Advance(World.Time);
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

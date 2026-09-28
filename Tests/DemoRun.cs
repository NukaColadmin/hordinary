using Hordinary.AI.Instructions;
using Hordinary.AI.Planning;
using Hordinary.AI.Planning.Pddl;
using Hordinary.Agents;
using Hordinary.Config;
using Hordinary.Game;
using Hordinary.World;
using Hordinary.World.MapGeneration;

namespace Hordinary.Tests;

/// <summary>
/// Un scenario de demonstration deroule sans Godot, horde comprise.
///
/// C'est la mise en place de <c>GameRoot.BeginDemo</c> — terrain de la grille, paquetage, consignes,
/// departs, horde sur ses reperes — et la boucle de <c>GameRoot._Process</c> <b>a la cadence du jeu</b> :
/// une frame de 1/60 s, les corps bougent a chaque frame, les zombies decident toutes les
/// <c>zombies.update_interval</c> secondes, le groupe percoit toutes les
/// <c>perception.update_interval</c> secondes, se parle et decide a chaque frame, puis on demande ou
/// en est la mission.
///
/// La cadence n'est pas un detail. Un premier harnais au pas de 0,1 s, percevant a chaque pas,
/// donnait raison aux douze scenarios pendant que le jeu en ratait six : un combat se joue a
/// quelques dixiemes pres, et un zombie vu une passe plus tot change qui frappe le premier. Contrairement a
/// <see cref="MissionRun"/>, la horde est la : un scenario de combat ou de fuite ne se verifie pas
/// sans elle.
///
/// Ce qui est enregistre est ce que les attentes de <c>config/demo_scenarios.json</c> peuvent citer :
/// les actions executees, et les evenements de <see cref="DemoEvents"/>.
/// </summary>
internal sealed class DemoRun
{
    /// <summary>Une frame du jeu.</summary>
    public const float Frame = 1f / 60f;

    /// <summary>La frame de ce deroule : celle du jeu, sauf pour eprouver un scenario a une autre cadence.</summary>
    public float Step { get; }

    public static DemoCatalogue Catalogue { get; } = DemoCatalogue.FromJson(DemoMap.ReadConfig("demo_scenarios.json"));

    /// <summary>La ceinture de la grille, celle que le jeu prend dans <c>config/mapgen.json</c>.</summary>
    public static BorderKind Border { get; } =
        MapGenConfig.FromJson(DemoMap.ReadConfig("mapgen.json")).Border.Find("fog")
        ?? throw new InvalidOperationException("Aucune ceinture « fog » dans config/mapgen.json.");

    /// <summary>
    /// La configuration livree, avec les limites du planificateur qu'une prise joue dans le jeu
    /// (<see cref="DemoSettings.PlannerFor"/>, <c>demo.planner_max_time_ms</c>).
    ///
    /// Le harnais desserrait autrefois son horloge a lui seul, a 60 s, en supposant que les plans
    /// d'une grille de demonstration tiendraient toujours dans les 50 ms du jeu. Sous trois consignes
    /// ce n'etait plus vrai : <c>grande_manoeuvre</c> passait ici et echouait dans le moteur, sur des plans
    /// coupes a 50 ms. Lire la
    /// meme valeur que le jeu ferme cet ecart — et la garder large tient la suite a l'abri de sa
    /// propre charge, ce qui etait la raison d'origine.
    /// </summary>
    private static GameConfig Config { get; } =
        MissionRun.Config with { Planner = MissionRun.Config.Demo.PlannerFor(MissionRun.Config.Planner) };

    private readonly Random _noise;
    private double _sinceZombieThink;
    private double _sincePerception;

    public DemoStage Stage { get; }
    public WorldState World { get; }
    public SurvivorBand Band { get; }
    public EntityManager Horde { get; }
    public MissionTracker Tracker { get; }

    /// <summary>Actions executees jusqu'au bout, par leur nom d'origine, et evenements.</summary>
    public List<string> Happened { get; } = [];

    public List<string> Journal { get; } = [];

    /// <param name="withInstructions">A <c>false</c>, le scenario part sans ses consignes : c'est le contraste.</param>
    /// <param name="player">
    /// Les consignes du joueur, pour une carte blanche : elles remplacent celles du fichier, comme
    /// <c>GameRoot.BeginDemo</c> le fait avec celles de l'ecran.
    /// </param>
    public DemoRun(string scenarioId, bool withInstructions = true, float step = Frame, IReadOnlyList<string>? player = null)
        : this(
            Catalogue.Find(scenarioId)
                ?? throw new InvalidOperationException($"Scenario « {scenarioId} » absent de config/demo_scenarios.json."),
            withInstructions,
            step,
            player)
    {
    }

    /// <summary>Un scenario hors du fichier — une variante d'un scenario livre, sur une grille livree.</summary>
    public DemoRun(DemoScenario scenario, bool withInstructions = true, float step = Frame, IReadOnlyList<string>? player = null)
    {
        Step = step;
        Stage = Catalogue.Stage(scenario, Border);
        // Le generateur que GameRoot seme sur zombies.seed et donne a chaque passe de perception.
        _noise = new Random(Config.Zombies.Seed);

        var map = Stage.District.Map;
        World = new WorldState(map, Config.Noise);

        int index = 0;
        foreach (var item in Stage.District.Items)
            World.SetItem($"{item.ItemId}-{++index}".ToLowerInvariant(), item.ItemId, item.Position);

        Band = new SurvivorBand(World, Config, catalogue: DemoMap.Items);

        // Comme GameRoot : la prise se tourne sous le regime du fichier livre, sauf quand le scenario
        // dit le sien. Un scenario verifie avec la panique ne dirait rien d'une partie jouee sans.
        Band.PanicEnabled = scenario.Panic ?? Config.Debug.PanicEnabled;
        var instructions = scenario.TakesPlayerInstructions ? player ?? [] : withInstructions ? scenario.Instructions : [];
        Band.Instruct(Instruct(instructions, map));

        var loadout = Stage.Loadout(DemoMap.Items, Config.Inventory);
        var mission = Stage.Mission;

        var van = map.FindWalkableIn(mission.CampIn(map)!)!.Value;
        var scattered = BandSpawn.Scatter(World, van, scenario.Team.Count, Stage.District.Seed);

        var objective = mission.ObjectiveIn(map);
        var site = objective is null ? null : new ObjectiveSite(objective.Name, objective.Kind);

        for (int i = 0; i < scenario.Team.Count; i++)
        {
            var profile = MissionRun.Roster.Find(scenario.Team[i].Survivor)!;
            var agent = Band.Enlist(profile, Stage.StartOf(profile.Id) ?? scattered[i], MissionRun.Goals, MissionRun.Domain);

            agent.Survivor.ObjectiveItem = mission.Item;
            agent.Survivor.ObjectiveZone = site;

            loadout.HandOver(agent.Survivor);
            Stage.Prepare(agent.Survivor);

            Watch(agent);
        }

        Band.Brief([.. mission.BriefedZones()]);
        if (objective is not null) Band.Brief(objective);
        Band.KnowBorder();
        if (scenario.Mapped) Band.KnowTerrain();

        Band.Sharing.Exchanged += exchange =>
        {
            if (exchange.Learned <= 0) return;
            Happened.Add(DemoEvents.Sharing);
            if (exchange.ByRadio) Happened.Add(DemoEvents.RadioSharing);
            Journal.Add(exchange.ToString());
        };

        Horde = new EntityManager(World, Config.Zombies, Config.Knowledge, Config.Perception);
        Horde.SpawnHorde(Stage.District.ZombieSpawns);
        Band.Horde = Horde;

        Horde.Attacked += Bite;
        Horde.Removed += _ => Happened.Add(DemoEvents.ZombieDown);

        Tracker = new MissionTracker(World, Band, mission, Config.Mission);
        Tracker.Recalling += _ => Happened.Add(DemoEvents.Recall);
        Tracker.Finished += verdict =>
        {
            Journal.Add(verdict.ToString());
            if (verdict.Outcome == MissionOutcome.Victory) Happened.Add(DemoEvents.Victory);
        };

        Band.Perceive(0d, _noise);
    }

    /// <summary>Vrai si l'action ou l'evenement s'est produit au moins une fois.</summary>
    public bool Saw(string name) => Happened.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Deroule la mission jusqu'au verdict, ou jusqu'a la limite.</summary>
    public void Run(double seconds = 400d)
    {
        int ticks = (int)(seconds / Step);

        for (int tick = 0; tick < ticks && !Tracker.IsOver; tick++) Tick();
    }

    /// <summary>Deroule jusqu'a ce que la condition soit remplie, ou jusqu'a la limite.</summary>
    public bool RunUntil(Func<bool> done, double seconds = 400d)
    {
        int ticks = (int)(seconds / Step);

        for (int tick = 0; tick < ticks && !Tracker.IsOver; tick++)
        {
            if (done()) return true;
            Tick();
        }

        return done();
    }

    public void Tick()
    {
        World.Advance(Step);
        Horde.Advance(Step);

        Band.Move(Step);

        // Les pas s'entendent, comme en jeu (SurvivorBand.EmitFootsteps) : c'est ce qui attire la horde.
        Band.EmitFootsteps();

        _sinceZombieThink += Step;
        if (_sinceZombieThink >= Config.Zombies.UpdateInterval)
        {
            _sinceZombieThink = 0d;
            Horde.Think(World.Time);
        }

        _sincePerception += Step;
        if (_sincePerception >= Config.Perception.UpdateInterval)
        {
            double elapsed = _sincePerception;
            _sincePerception = 0d;

            Band.Perceive(elapsed, _noise);
            NoticeImportantEvents();
        }

        Band.ShareKnowledge(World.Time);
        Band.Think(Step);

        Tracker.Evaluate(World.Time);
    }

    public string Trace()
        => $"Scenario {Stage.Scenario.Id} : {string.Join(", ", Happened)}. "
         + $"Journal : {string.Join(" | ", Journal.TakeLast(25))}";

    private void Watch(SurvivorAgent agent)
    {
        string who = agent.Survivor.Name;

        agent.Executor.ActionCompleted += action => Happened.AddRange(DemoEvents.Of(action.Schema.Name, action.Name));
        agent.Executor.ActionFailed += (action, failure) => Journal.Add($"{who} echec {action.Schema.Name} : {failure.Reason}");
        agent.Brain.Journal.Written += entry => Journal.Add($"{who} : {entry.Message}");
        agent.Fought += Journal.Add;
    }

    /// <summary>La morsure, comme <c>GameRoot.Bite</c> : on sait ce qui vous touche, meme dans le dos.</summary>
    private void Bite(Zombie zombie, string targetId)
    {
        if (Band.Find(targetId) is not { } agent) return;

        var victim = agent.Survivor;
        if (!victim.Alive) return;

        victim.Knowledge.RememberEntity(zombie.Id, EntityKind.Zombie, zombie.Position, World.Time);
        victim.TakeDamage(Config.Zombies.AttackDamage);
        Happened.Add(DemoEvents.Bite);
        Journal.Add($"{zombie.Id} mord {victim.Name} ({victim.Health:0} PV)");

        if (victim.Alive)
        {
            agent.Utility.Request("survivant blesse");
            return;
        }

        agent.Stop("survivant hors de combat");
        Tracker.Evaluate(World.Time);
    }

    /// <summary>Un bruit fort ne peut pas attendre la reevaluation, comme <c>GameRoot.NoticeImportantEvents</c>.</summary>
    private void NoticeImportantEvents()
    {
        foreach (var agent in Band.Standing)
        {
            foreach (var heard in agent.LastHeard)
            {
                if (heard.PerceivedIntensity < Config.UtilityAi.LoudNoiseIntensity) continue;

                agent.Utility.Request($"bruit fort entendu vers {heard.BelievedPosition}");
                break;
            }
        }
    }

    private static TacticalInstructions? Instruct(IReadOnlyList<string> texts, GridMap map)
    {
        if (texts.Count == 0) return null;

        var instructions = new TacticalInstructions(PddlDomainBuilder.From(MissionRun.Domain), Config.Instructions);

        var kinds = new HashSet<string>(
            map.Zones.Select(zone => SymbolicStateBuilder.KindNameOf(zone.Kind)).OfType<string>(),
            StringComparer.OrdinalIgnoreCase);

        var context = new CompilationContext(Config.Planner.Search, kinds);

        foreach (string text in texts)
        {
            var entry = instructions.Add(text, context);

            if (!entry.Accepted)
                throw new InvalidOperationException($"« {text} » refusee par le portillon {entry.Gate} : {entry.Reason}");
        }

        return instructions;
    }
}

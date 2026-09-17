using Hordinary.AI.Planning;
using Hordinary.AI.Replanning;
using Hordinary.AI.Utility;
using Hordinary.Agents;
using Hordinary.Config;
using Hordinary.World;

namespace Hordinary.Tests;

/// <summary>
/// Livrable 6. Le critere de validation est joue en premier, dans le monde : « un survivant change de
/// plan lorsqu'il decouvre qu'une porte est verrouillee ». Les tests suivants isolent chacun des
/// mecanismes qui le rendent possible — detection, invalidation, journalisation — pour qu'un echec
/// designe une cause et non un symptome.
/// </summary>
public class ReplanningTests
{
    private static readonly PlanningDomain Domain = MissionRun.Domain;

    // --- Critere de validation, joue dans le monde -----------------------------------------------

    [Fact]
    public void Alice_change_de_plan_en_decouvrant_la_porte_verrouillee()
    {
        var mission = Corridor();

        // On attend qu'elle ait vu la porte — fermee — et qu'elle ait un plan qui compte fouiller la
        // pharmacie : c'est ce plan-la, bati sur une porte franchissable, que la decouverte va faire
        // tomber.
        bool ready = mission.RunUntil(
            () => mission.Plan is { } plan
                && plan.Actions.Any(action => action.Schema.Name == "SearchRoom")
                && mission.Alice.Knowledge.GetCell(Door)?.Value == CellType.DoorClosed,
            maxTicks: 400);

        Assert.True(ready, $"Alice n'a jamais planifie de fouiller la pharmacie. {mission.Trace()}");

        // Son plan est optimiste : la porte qu'elle a vue est fermee, pas verrouillee, donc rien ne
        // lui dit qu'il faudra la forcer.
        var optimistic = mission.Plan!;
        Assert.DoesNotContain(optimistic.Actions, action => action.Schema.Name == "ForceDoor");
        Assert.Equal(CellType.DoorClosed, mission.Alice.Knowledge.GetCell(Door)!.Value);

        // Quelqu'un verrouille la porte. Alice n'en sait rien encore : le monde a change, pas sa
        // memoire — c'est sa prochaine observation qui le lui apprendra.
        mission.World.Grid.SetCell(Door, CellType.DoorLocked);
        Assert.Equal(CellType.DoorClosed, mission.Alice.Knowledge.GetCell(Door)!.Value);

        bool replanned = mission.RunUntil(
            () => mission.Replans.Any(record => record.Trigger.Cause == ReplanCause.DoorLocked),
            maxTicks: 400);

        Assert.True(replanned, $"Alice n'a pas replanifie devant la porte verrouillee. {mission.Trace()}");

        // La decouverte vient de sa perception, pas d'un savoir donne.
        var belief = mission.Alice.Knowledge.GetCell(Door);
        Assert.Equal(CellType.DoorLocked, belief!.Value);
        Assert.Equal(KnowledgeSource.DirectObservation, belief.Source);

        // Et le plan a change : la reponse adaptee a une porte verrouillee est de la forcer.
        var record = mission.Replans.First(r => r.Trigger.Cause == ReplanCause.DoorLocked);
        Assert.NotSame(optimistic, record.Plan);
        Assert.Contains("porte est verrouillee", record.ReasonLine, StringComparison.Ordinal);
        Assert.NotNull(record.Plan);
        Assert.Contains(record.Plan.Actions, action => action.Schema.Name == "ForceDoor");
    }

    /// <summary>Porte de la pharmacie du couloir de test, et le poste d'observation d'Alice.</summary>
    private static readonly GridPosition Door = new(6, 2);

    private static readonly GridPosition Watchpost = new(4, 2);

    /// <summary>
    /// Un couloir, une porte, une pharmacie derriere : le decor minimal du critere de validation.
    ///
    /// La carte livree ne convient pas ici, et c'est instructif : Alice y apercoit les medicaments
    /// par la porte avant d'avoir eu besoin de fouiller, si bien qu'aucun de ses plans ne demande
    /// jamais que la pharmacie soit accessible. Il faut une pharmacie dont on ne voit pas le contenu
    /// pour que sa porte compte.
    ///
    /// L'ordre du joueur vise <see cref="Watchpost"/>, a deux cases de la porte. Une fois sur place
    /// la consigne est remplie, la Utility AI passe a « chercher les medicaments », et Alice regarde
    /// la porte de loin — ce qui laisse a la surveillance le temps de constater que le plan est
    /// tombe avant que le corps ne se cogne au battant.
    /// </summary>
    private static MissionRun Corridor()
    {
        var map = TestMaps.Parse(
            "############",
            "#.....#PPP.#",
            "#.....DPPP.#",
            "#.....#PPP.#",
            "############");

        map.AddZone(new MapZone("Depart", ZoneKind.Start, new GridPosition(1, 2), new GridPosition(1, 2)));
        map.AddZone(new MapZone("Pharmacie", ZoneKind.Pharmacy, new GridPosition(7, 1), new GridPosition(9, 3)));

        // Sans medicaments a apercevoir, « chercher » reste un objectif : c'est ce qui met la
        // pharmacie — et donc sa porte — dans le plan.
        // Seule la cadence de verification est resserree : a 4 cases par seconde, l'intervalle livre
        // de 0,5 s laisse Alice franchir deux cases entre deux regards, soit toute la distance qui la
        // separe de la porte. Le reste des reglages est celui du jeu.
        var mission = new MissionRun(
            map,
            replanning: new ReplanningSettings { CheckInterval = 0.2f },
            withObjective: false);

        mission.Alice.Mission = new MissionOrder(
            "surveiller la pharmacie", Watchpost, MissionRun.Config.UtilityAi.PlayerOrderPriority);

        return mission;
    }

    /// <summary>
    /// Le meme critere sur la carte livree, ou la porte de la pharmacie est verrouillee des le
    /// depart — et ou la pharmacie n'a pas d'autre entree. Alice n'y decouvre pas le verrou en le
    /// voyant changer mais en s'y heurtant : la cause nommee est alors l'obstacle ou l'itineraire
    /// impossible, et non la porte. Le comportement, lui, est le meme — elle abandonne « entrer »
    /// pour « forcer », et cette fois elle va jusqu'au bout.
    /// </summary>
    [Fact]
    public void Sur_la_carte_livree_elle_force_la_porte_et_ressort_avec_les_medicaments()
    {
        var mission = new MissionRun(lockedDoors: [MissionRun.PharmacyDoor]);

        mission.Alice.Mission = new MissionOrder(
            "recuperer les medicaments",
            mission.Spawn(ZoneKind.Pharmacy),
            MissionRun.Config.UtilityAi.PlayerOrderPriority);

        mission.RunUntil(() => mission.Plan is not null, maxTicks: 100);

        var first = mission.Plan;
        Assert.NotNull(first);
        Assert.DoesNotContain(first.Actions, action => action.Schema.Name == "ForceDoor");

        // Elle finit par entrer, et pas par magie : c'est l'action adaptee qui a ouvert la porte.
        bool collected = mission.RunUntil(
            () => mission.Alice.Carries(Survivor.DefaultObjectiveItem), maxTicks: MissionRun.DefaultMaxTicks);

        Assert.True(collected, $"Alice n'a jamais recupere les medicaments. {mission.Trace()}");
        Assert.Contains("ForceDoor", mission.Executed);

        Assert.Equal(CellType.DoorOpen, mission.World.Grid.GetCell(MissionRun.PharmacyDoor));

        Assert.Contains(
            mission.Replans,
            record => record.Trigger.Cause
                is ReplanCause.DoorLocked or ReplanCause.ObstacleDiscovered or ReplanCause.PathImpossible);

        // Forcer s'entend, et s'entend pour ce que c'est : le journal ne parle plus de distraction.
        Assert.Contains(mission.Journal, line => line.Contains("ForceDoor (6", StringComparison.Ordinal));
    }

    [Fact]
    public void La_replanification_produit_le_journal_attendu()
    {
        var mission = Corridor();

        mission.RunUntil(
            () => mission.Plan is { } plan
                && plan.Actions.Any(action => action.Schema.Name == "SearchRoom")
                && mission.Alice.Knowledge.GetCell(Door)?.Value == CellType.DoorClosed,
            maxTicks: 400);

        mission.World.Grid.SetCell(Door, CellType.DoorLocked);
        mission.RunUntil(() => mission.Replans.Count > 0, maxTicks: 400);

        var record = Assert.Single(mission.Replans.Take(1));

        // Le bloc impose par la specification : horodatage, raison, objectif precedent, nouvel objectif.
        Assert.StartsWith("[", record.Header, StringComparison.Ordinal);
        Assert.Contains("Alice replanifie", record.Header, StringComparison.Ordinal);
        Assert.StartsWith("Raison : ", record.ReasonLine, StringComparison.Ordinal);
        Assert.StartsWith("Objectif precedent : ", record.PreviousGoalLine, StringComparison.Ordinal);
        Assert.StartsWith("Nouvel objectif : ", record.NewGoalLine, StringComparison.Ordinal);

        // Il part bien dans le journal des decisions de l'IA, ligne par ligne.
        var lines = mission.Brain.Journal.Entries.Where(entry => entry.Kind == DecisionKind.Replan).ToList();
        Assert.Contains(lines, entry => entry.Message == record.Header);
        Assert.Contains(lines, entry => entry.Message == record.ReasonLine);
    }

    // --- Journalisation -------------------------------------------------------------------------

    [Fact]
    public void Le_journal_horodate_au_format_de_la_specification()
    {
        // 10 h 42 min 13 s de temps de jeu : « [10:42:13] » dans l'exemple de la specification.
        Assert.Equal("[10:42:13]", DecisionJournal.Clock(10 * 3600 + 42 * 60 + 13));
        Assert.Equal("[00:00:00]", DecisionJournal.Clock(0));
        Assert.Equal("[00:00:00]", DecisionJournal.Clock(-5));
    }

    [Fact]
    public void Le_journal_est_borne_et_compte_les_causes()
    {
        var journal = new DecisionJournal(capacity: 3);

        for (int i = 0; i < 10; i++) journal.Add(i, "Alice", DecisionKind.Event, $"ligne {i}");

        Assert.Equal(3, journal.Entries.Count);
        Assert.Equal(["ligne 7", "ligne 8", "ligne 9"], journal.Tail(5).Select(line => line[^7..]));

        journal.AddReplan(Record(ReplanCause.DoorLocked));
        journal.AddReplan(Record(ReplanCause.DoorLocked));
        journal.AddReplan(Record(ReplanCause.ZombieSpotted));

        Assert.Equal(3, journal.ReplanCount);
        Assert.Equal(2, journal.CountsByCause()[ReplanCause.DoorLocked]);
        Assert.Equal(1, journal.CountsByCause()[ReplanCause.ZombieSpotted]);
    }

    private static ReplanRecord Record(ReplanCause cause) => new(
        At: 1d,
        Agent: "Alice",
        Trigger: new ReplanTrigger(cause, "detail", 1d),
        PreviousGoal: "PickUpObjective",
        NewGoal: "ExploreUnknownArea",
        Plan: null,
        PlanReason: "sans solution");

    // --- Invalidation d'un plan -------------------------------------------------------------------

    [Fact]
    public void Un_plan_dont_les_preconditions_tiennent_reste_valide()
    {
        var plan = PharmacyPlan(out var initial);

        Assert.Null(PlanValidity.Check(plan, initial, now: 1d));
    }

    [Fact]
    public void Une_porte_devenue_verrouillee_invalide_le_plan()
    {
        var plan = PharmacyPlan(out var initial);

        // Ce qu'apprend la perception : la porte de la pharmacie est fermee a clef, donc le lieu
        // n'est plus accessible. Aucune action du plan ne prevoit de la forcer.
        var discovered = initial
            .With("door_locked(door_1)", "!door_closed(door_1)", "!accessible(objective_zone)");

        var trigger = PlanValidity.Check(plan, discovered, now: 12d);

        Assert.NotNull(trigger);
        Assert.Equal(ReplanCause.DoorLocked, trigger.Value.Cause);
        Assert.Contains("SearchRoom", trigger.Value.Detail, StringComparison.Ordinal);
        Assert.Contains("porte est verrouillee", trigger.Value.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_objet_disparu_invalide_le_plan_et_le_dit()
    {
        var plan = PharmacyPlan(out var initial);

        // Alice est sur place, mais les medicaments qu'elle croyait avoir repere n'y sont pas :
        // c'est « un objet attendu est absent », le huitieme declencheur.
        var state = initial.With("at(agent, objective_zone)", "searched(objective_zone)");
        var pickUp = new Plan(plan.Problem, [plan.Actions.Last()], createdAt: 0d);

        var trigger = PlanValidity.Check(pickUp, state, now: 3d);

        Assert.NotNull(trigger);
        Assert.Equal(ReplanCause.ItemMissing, trigger.Value.Cause);
    }

    [Fact]
    public void Un_plan_qui_ne_mene_plus_au_but_est_invalide()
    {
        // Le plan n'a plus d'action a executer et le but n'est pas atteint : rien ne le rattrapera.
        var problem = new PlanningProblem(
            "PickUpObjective",
            new SymbolicState(["at(agent, pharmacy)"]),
            [Condition.Parse("has(agent, objective)")],
            [new PlanningObject("pharmacy", PlanningObjectTypes.Location, new GridPosition(0, 0))],
            [],
            new GridPosition(0, 0));

        var plan = new Plan(problem, []);
        var trigger = PlanValidity.Check(plan, problem.Initial, now: 5d);

        Assert.NotNull(trigger);
        Assert.Equal(ReplanCause.PreconditionFalse, trigger.Value.Cause);
        Assert.Contains("ne mene plus au but", trigger.Value.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// Le plan de reference : aller au lieu de la mission, ouvrir, fouiller, ramasser. Il est construit
    /// a la main pour que les tests d'invalidation ne dependent pas de ce que GBFS aura trouve ce
    /// jour-la.
    ///
    /// <para>
    /// Le lieu s'appelle <c>objective_zone</c> et pas <c>pharmacy</c> : <c>LocateObjective</c> et
    /// <c>PickUpObjective</c> citent ce symbole en <b>constante</b> dans
    /// <c>config/planner_domain.json</c>, et non par un parametre <c>?location</c>. Un fixture qui
    /// nommait son lieu autrement produisait un plan dont les preconditions ne pouvaient pas tenir,
    /// quelle que soit la suite d'actions — la mission designe un lieu, pas une categorie de commerce.
    /// </para>
    /// </summary>
    private static Plan PharmacyPlan(out SymbolicState initial)
    {
        initial = new SymbolicState([
            "known(objective_zone)",
            "accessible(objective_zone)",
            "door_of(door_1, objective_zone)",
            "door_closed(door_1)",
        ]);

        var objects = new List<PlanningObject>
        {
            new("objective_zone", PlanningObjectTypes.Location, new GridPosition(5, 0)),
            new("door_1", PlanningObjectTypes.Door, new GridPosition(4, 0)),
            new("objective", PlanningObjectTypes.Item, new GridPosition(5, 0)),
        };

        var problem = new PlanningProblem(
            "PickUpObjective",
            initial,
            [Condition.Parse("has(agent, objective)")],
            objects,
            [],
            new GridPosition(0, 0));

        var actions = new[] { "MoveToLocation", "SearchRoom", "LocateObjective", "PickUpObjective" }
            .Select(name => Domain.Actions.First(schema => schema.Name == name))
            .Select(schema => schema.Ground(objects).First())
            .ToList();

        return new Plan(problem, actions);
    }

    // --- Detection ------------------------------------------------------------------------------

    [Fact]
    public void Un_zombie_ne_declenche_qu_a_la_premiere_rencontre()
    {
        var (survivor, monitor) = Watcher();

        // Rien en vue : rien a signaler.
        Assert.Null(Inspect(monitor, survivor, now: 1d));

        // Assez loin pour que la rencontre soit la seule nouvelle : au contact, c'est le seuil de
        // danger qui parlerait, et ce test-la est le suivant.
        survivor.Knowledge.RememberEntity("zombie-3", EntityKind.Zombie, new GridPosition(1, 1), 1d);

        var trigger = Inspect(monitor, survivor, now: 2d);
        Assert.NotNull(trigger);
        Assert.Equal(ReplanCause.ZombieSpotted, trigger.Value.Cause);
        Assert.Contains("zombie-3", trigger.Value.Detail, StringComparison.Ordinal);

        // Le meme zombie, toujours la : ce n'est plus une nouvelle.
        Assert.Null(Inspect(monitor, survivor, now: 3d));
    }

    [Fact]
    public void Le_seuil_de_danger_ne_declenche_qu_au_franchissement()
    {
        var settings = new ReplanningSettings { CheckInterval = 0f, DangerThreshold = 0.5f, DangerHysteresis = 0.2f };
        var (survivor, monitor) = Watcher(settings);

        // Un zombie au contact : le danger ressenti sature.
        survivor.Knowledge.RememberEntity("zombie-1", EntityKind.Zombie, survivor.Position, 1d);

        // La premiere passe signale la rencontre ; la seconde, le franchissement du seuil.
        Assert.Equal(ReplanCause.ZombieSpotted, Inspect(monitor, survivor, 2d)!.Value.Cause);
        Assert.Equal(ReplanCause.DangerThreshold, Inspect(monitor, survivor, 3d)!.Value.Cause);

        // Tant que la pression reste haute, le seuil ne se franchit pas deux fois.
        Assert.Null(Inspect(monitor, survivor, 4d));
    }

    [Fact]
    public void Un_evenement_signale_par_le_moteur_passe_devant_les_deductions()
    {
        var (survivor, monitor) = Watcher();
        survivor.Knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(9, 9), 1d);

        monitor.Report(ReplanCause.PathImpossible, "aucun itineraire vers (4, 4)", 2d);
        Assert.True(monitor.HasReport);

        var trigger = Inspect(monitor, survivor, now: 2d);

        Assert.NotNull(trigger);
        Assert.Equal(ReplanCause.PathImpossible, trigger.Value.Cause);
        Assert.False(monitor.HasReport);
    }

    [Fact]
    public void Un_plan_identique_sans_itineraire_est_mis_en_attente()
    {
        var map = TestMaps.Parse("........");
        var knowledge = new AgentKnowledge("alice", map.Width, map.Height, map.Origin);

        foreach (var cell in map.AllPositions())
            knowledge.ObserveCell(cell, map.GetCell(cell), map.IsIndoor(cell), 0d);

        var alice = new Survivor(
            "alice", "Alice", knowledge,
            settings: MissionRun.Config.Survivor,
            catalogue: DemoMap.Items)
        {
            Position = new GridPosition(0, 0),
        };

        knowledge.RememberItem(
            "objective-1", Survivor.DefaultObjectiveItem, new GridPosition(7, 0), 0d);

        var runtime = new NoRouteRuntime(alice.Position);
        var executor = new PlanExecutor(runtime, MissionRun.Config.Planner);
        var planner = new Planner(Domain, MissionRun.Config.Planner);
        var utility = new UtilitySelector(MissionRun.Goals, MissionRun.Config.UtilityAi);
        var brain = new Replanner(
            alice,
            utility,
            planner,
            executor,
            new ReplanningSettings { CheckInterval = 0f, MinimumInterval = 0f });

        for (int tick = 0; tick < 40; tick++)
        {
            double now = tick * 0.1d;
            brain.Advance(new UtilityContext(alice, now, MissionRun.Config.UtilityAi), 0.1f);
        }

        Assert.Equal(1, runtime.MoveAttempts);
        Assert.Equal(2, planner.SearchCount);
        Assert.Equal(1, brain.ReplanCount);
        Assert.Contains("suspendu", brain.LastReason, StringComparison.Ordinal);

        // Une nouvelle information rouvre bien la tentative : la mise en attente n'est pas un
        // abandon definitif de l'objectif.
        knowledge.RememberLocation(
            "Sortie", ZoneKind.Extraction, new GridPosition(1, 0), 4d, KnowledgeSource.DirectObservation);
        brain.Advance(new UtilityContext(alice, 5d, MissionRun.Config.UtilityAi), 0.1f);

        Assert.Equal(2, runtime.MoveAttempts);
        Assert.Equal(3, planner.SearchCount);
    }

    [Theory]
    [InlineData(ActionFailureKind.Precondition, ReplanCause.PreconditionFalse)]
    [InlineData(ActionFailureKind.Verb, ReplanCause.ActionFailed)]
    [InlineData(ActionFailureKind.NoRoute, ReplanCause.PathImpossible)]
    [InlineData(ActionFailureKind.UnknownDestination, ReplanCause.PathImpossible)]
    [InlineData(ActionFailureKind.RouteBlocked, ReplanCause.ObstacleDiscovered)]
    public void Chaque_nature_d_echec_a_son_declencheur(ActionFailureKind kind, ReplanCause expected)
    {
        var action = Domain.Actions.First(schema => schema.Name == "Wait").Ground([]).First();
        var trigger = ReplanTrigger.FromFailure(action, new ActionFailure(kind, "raison"), 4d);

        Assert.Equal(expected, trigger.Cause);
        Assert.Contains("raison", trigger.Detail, StringComparison.Ordinal);
    }

    private static (Survivor Survivor, ReplanMonitor Monitor) Watcher(ReplanningSettings? settings = null)
    {
        var knowledge = new AgentKnowledge("alice", 24, 24);
        var survivor = new Survivor("alice", "Alice", knowledge) { Position = new GridPosition(9, 9) };

        return (survivor, new ReplanMonitor(settings ?? new ReplanningSettings { CheckInterval = 0f }));
    }

    /// <summary>Une passe du moniteur, sans plan a surveiller : seules les deductions comptent.</summary>
    private static ReplanTrigger? Inspect(ReplanMonitor monitor, Survivor survivor, double now)
        => monitor.Inspect(new UtilityContext(survivor, now), plan: null, SymbolicState.Empty);

    /// <summary>A* de test qui refuse toute destination et compte les tentatives.</summary>
    private sealed class NoRouteRuntime(GridPosition start) : IPlanRuntime
    {
        public GridPosition AgentCell { get; } = start;

        public int MoveAttempts { get; private set; }

        public bool BeginMove(GridPosition destination)
        {
            MoveAttempts++;
            return false;
        }

        public MoveState PollMove(GridPosition destination) => MoveState.Blocked;
        public GridPosition? WhereIs(string item) => null;
        public bool Take(string item) => false;
        public bool Put(string item, GridPosition where) => false;
        public bool SetFlag(string fact, bool value, GridPosition? where) => false;
        public bool Damage(string target, float amount) => false;
        public bool Push(string target) => false;
        public bool ThrowNoise(string target, float amount) => false;
        public void CancelMove() { }
        public void EmitNoise(float level, GridPosition where, string? label = null) { }
    }

    // --- Reconciliation de l'etat symbolique --------------------------------------------------------

    [Fact]
    public void L_observation_tranche_sur_le_monde_mais_pas_sur_l_avancement_du_plan()
    {
        // Ce que le plan a accompli, plus ce qu'il croyait du monde.
        var progress = new SymbolicState(["searched(pharmacy)", "explored", "door_closed(door_1)", "accessible(pharmacy)"]);

        // Ce qu'Alice voit maintenant : la porte est verrouillee, le lieu ne l'est plus.
        var observed = new SymbolicState(["door_locked(door_1)", "known(pharmacy)"]);

        var state = progress.Reconcile(observed, SymbolicStateBuilder.ObservablePredicates);

        // L'observation a le dernier mot sur les portes et les lieux.
        Assert.True(state.Holds("door_locked(door_1)"));
        Assert.False(state.Holds("door_closed(door_1)"));
        Assert.False(state.Holds("accessible(pharmacy)"));

        // Personne ne peut voir qu'une piece a deja ete fouillee : l'avancement survit.
        Assert.True(state.Holds("searched(pharmacy)"));
        Assert.True(state.Holds("explored"));
    }

    // --- Annulation de l'action en cours ------------------------------------------------------------

    [Fact]
    public void Abandonner_un_plan_arrete_le_survivant()
    {
        var mission = new MissionRun();
        mission.Alice.Mission = new MissionOrder(
            "rejoindre la pharmacie",
            mission.Spawn(ZoneKind.Pharmacy),
            MissionRun.Config.UtilityAi.PlayerOrderPriority);

        // Un plan en cours, et un corps effectivement en train de marcher.
        mission.RunUntil(() => mission.Executor.IsBusy && !mission.Follower.IsFinished, maxTicks: 200);

        Assert.True(mission.Executor.IsBusy, $"Aucun plan en cours a abandonner. {mission.Trace()}");
        Assert.False(mission.Follower.IsFinished, $"Alice ne marche pas. {mission.Trace()}");

        mission.Executor.Abandon("test");

        // « Annulation de l'action actuelle » : le corps ne finit pas le trajet d'un plan mort.
        Assert.True(mission.Follower.IsFinished);
    }

    // --- Configuration ------------------------------------------------------------------------------

    [Fact]
    public void Les_reglages_de_replanification_sont_lus_integralement()
    {
        const string json = """
            {
              "replanning": {
                "check_interval": 0.25,
                "minimum_interval": 0.75,
                "danger_threshold": 0.8,
                "danger_hysteresis": 0.05,
                "ally_danger_threshold": 0.4,
                "journal_capacity": 64,
                "watch_plan_validity": false
              }
            }
            """;

        var settings = GameConfig.FromJson(json).Replanning;

        Assert.Equal(0.25f, settings.CheckInterval, 3);
        Assert.Equal(0.75f, settings.MinimumInterval, 3);
        Assert.Equal(0.8f, settings.DangerThreshold, 3);
        Assert.Equal(0.05f, settings.DangerHysteresis, 3);
        Assert.Equal(0.4f, settings.AllyDangerThreshold, 3);
        Assert.Equal(64, settings.JournalCapacity);
        Assert.False(settings.WatchPlanValidity);

        // La configuration livree doit rester lisible, elle aussi.
        Assert.True(MissionRun.Config.Replanning.CheckInterval > 0f);
        Assert.True(MissionRun.Config.Replanning.WatchPlanValidity);
    }

    /// <summary>
    /// Les dix evenements declencheurs de la specification ont chacun leur valeur, et chacune une
    /// phrase lisible : c'est ce qui rend verifiable qu'aucun n'a ete oublie en chemin.
    /// </summary>
    [Fact]
    public void Les_dix_evenements_declencheurs_sont_couverts()
    {
        var causes = Enum.GetValues<ReplanCause>().Where(cause => cause != ReplanCause.None).ToList();

        Assert.Equal(10, causes.Count);
        Assert.All(causes, cause => Assert.NotEqual("aucune cause", ReplanCauses.Label(cause)));
    }
}

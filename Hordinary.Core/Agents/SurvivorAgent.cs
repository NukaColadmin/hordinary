using Hordinary.AI.Navigation;
using Hordinary.AI.Planning;
using Hordinary.AI.Replanning;
using Hordinary.AI.Utility;
using Hordinary.Config;
using Hordinary.World;

namespace Hordinary.Agents;

/// <summary>
/// Un survivant complet : son corps, sa memoire et sa boucle de decision.
///
/// Jusqu'au livrable 6, ce cablage — carte mentale, A*, Utility AI, GBFS, executeur, moniteur de
/// replanification — vivait en double, une fois dans <c>GameRoot</c> et une fois dans le harnais de
/// test. Avec trois survivants il aurait vecu en six exemplaires. Il vit donc ici, dans le coeur, et
/// les deux couches n'ont plus qu'a le faire tourner : c'est aussi ce qui garantit que le jeu et les
/// tests suivent la meme boucle et non deux copies qui divergent.
///
/// Chaque agent a <b>sa</b> carte mentale, <b>son</b> planificateur et <b>son</b> domaine specialise
/// par son profil. Rien n'est partage entre survivants sauf le monde lui-meme — et le monde n'est lu
/// que par la perception.
/// </summary>
public sealed class SurvivorAgent
{
    private readonly WorldState _world;
    private readonly PerceptionSystem _perception;
    private readonly GameConfig _config;

    private float _pursuit;

    public Survivor Survivor { get; }
    public SurvivorProfile Profile => Survivor.Profile;

    public NavigationGrid Navigation { get; }
    public AStar Astar { get; }
    public PathFollower Follower { get; } = new();
    public RearGlance Glance { get; }

    /// <summary>Domaine de planification specialise par les competences du profil (livrable 7).</summary>
    public PlanningDomain Domain { get; }

    public UtilitySelector Utility { get; }
    public Planner Planner { get; }
    public WorldPlanRuntime Runtime { get; }
    public PlanExecutor Executor { get; }
    public Replanner Brain { get; }

    /// <summary>Derniere situation percue. C'est elle qu'on donne au cerveau et au HUD.</summary>
    public UtilityContext? Context { get; private set; }

    public PerceptionResult? LastPerception { get; private set; }
    public IReadOnlyList<HeardNoise> LastHeard { get; private set; } = [];

    public Plan? Plan => Brain.Plan;

    /// <summary>Journal d'execution du plan, tel que le moteur le raconte.</summary>
    public event Action<string>? Logged;

    public SurvivorAgent(
        SurvivorProfile profile,
        GridPosition spawn,
        WorldState world,
        GameConfig config,
        UtilityGoalSet goals,
        PlanningDomain domain,
        PerceptionSystem perception,
        IReadOnlyList<Survivor> band,
        KnowledgeSharing sharing,
        IHorde? horde = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(goals);
        ArgumentNullException.ThrowIfNull(domain);

        _world = world ?? throw new ArgumentNullException(nameof(world));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _perception = perception ?? throw new ArgumentNullException(nameof(perception));

        // Comme tout agent, un survivant nait sans rien savoir du terrain : seules ses dimensions.
        var knowledge = new AgentKnowledge(
            profile.Id, world.Grid.Width, world.Grid.Height, world.Grid.Origin, config.Knowledge);

        Survivor = Survivor.FromProfile(profile, knowledge, config.Perception, config.Survivor);
        Survivor.Position = spawn;

        Glance = new RearGlance(Survivor.Settings);

        // A* travaille sur la carte mentale, jamais sur la carte reelle, et paie plus cher les cases
        // dont ce survivant-la garde un mauvais souvenir. La prudence du profil module ces couts :
        // Claire fait un detour la ou Bob passe (specification section 16.2).
        Navigation = new NavigationGrid(knowledge.MentalMap, config.Navigation)
        {
            Sensed = cell => knowledge.Danger.LevelAt(cell, _world.Time),
            Forbidden = Survivor.Orders.IsForbidden,
            Profile = NavigationProfile.FromCaution(Survivor.Personality.Caution),
        };

        Astar = new AStar(Navigation);

        Follower.Speed = Survivor.Settings.WalkSpeed;
        Follower.Teleport(spawn);

        Domain = domain.Specialize(profile.Skills);

        Utility = new UtilitySelector(goals, config.UtilityAi);
        Planner = new Planner(Domain, config.Planner);

        Runtime = new WorldPlanRuntime(
            Survivor, world, Follower, Astar, Survivor.Settings, band, sharing, horde);

        Runtime.Logged += message => Logged?.Invoke(message);
        Runtime.WorldChanged += _ => Perceive();

        // Le suiveur previent quand la case suivante devient infranchissable. Avec la carte mentale,
        // cela arrive des que la perception revele un mur sur l'itineraire prevu : c'est « un
        // obstacle est decouvert », le quatrieme declencheur du livrable 6.
        Follower.Blocked += _ => Runtime.NotifyBlocked();

        Executor = new PlanExecutor(Runtime, config.Planner);
        Brain = new Replanner(Survivor, Utility, Planner, Executor, config.Replanning);

        _world.SetEntity(Survivor.Id, EntityKind.Survivor, spawn);
    }

    // --- Boucle ---------------------------------------------------------------------------------

    /// <summary>Fait glisser le corps. A appeler a chaque frame.</summary>
    public void Move(float delta)
        => Follower.Advance(delta, cell => Survivor.Knowledge.MentalMap.IsWalkable(cell));

    /// <summary>
    /// Une passe de perception (5 a 10 fois par seconde) : le survivant regarde, ecoute, oublie ce
    /// qui s'est efface, et la situation qui en resulte devient une emotion.
    /// </summary>
    /// <param name="elapsed">Secondes ecoulees depuis la derniere passe, pour la panique.</param>
    /// <param name="rng">Porte l'erreur de localisation des bruits ; une graine connue rend le test reproductible.</param>
    public void Perceive(double elapsed = 0d, Random? rng = null)
    {
        Survivor.Position = Follower.CurrentCell;

        // Fuir sans jamais regarder derriere soi, c'est perdre de vue ce qui vous poursuit.
        Survivor.Facing = Glance.Gaze(Follower.Facing, _pursuit, _world.Time);

        _world.SetEntity(Survivor.Id, EntityKind.Survivor, Survivor.Position);

        LastPerception = _perception.Observe(Survivor, _world);
        LastHeard = _perception.Listen(Survivor, _world, rng);
        Survivor.Knowledge.ForgetFadedNoises(_world.Time);

        Context = new UtilityContext(Survivor, _world.Time, _config.UtilityAi, _config.Exploration);
        _pursuit = Context.PursuitPressure;

        if (elapsed <= 0d) return;

        Survivor.UpdatePanic(Context.PerceivedDanger, (float)elapsed);

        // La motivation suit la panique, donc apres elle : ce qui use un survivant, c'est la peur
        // qu'il vient d'avoir, pas celle de la passe precedente.
        Survivor.UpdateSanity((float)elapsed);
    }

    /// <summary>
    /// Une passe de decision : guetter les evenements, reevaluer les priorites, replanifier si
    /// besoin, faire avancer l'action en cours. Puis annoncer ou l'on va, ce qui est la seule facon
    /// pour les autres d'eviter d'y aller aussi.
    /// </summary>
    public void Think(float delta)
    {
        var context = Context ?? new UtilityContext(Survivor, _world.Time, _config.UtilityAi, _config.Exploration);

        Brain.Advance(context, delta);
        DeclareIntent(context);
    }

    /// <summary>
    /// « Je m'occupe du nord. » L'annonce entre dans sa propre memoire, d'ou le prochain partage la
    /// portera aux allies a portee (livrable 7) — et ou elle deviendra, chez eux, la penalite de
    /// duplication qui les enverra ailleurs.
    ///
    /// Elle n'est pas reservee a l'exploration : savoir qu'un allie se dirige vers la pharmacie vaut
    /// aussi la peine d'etre dit. Un objectif sans lieu — se soigner, faire diversion — ne s'annonce
    /// pas : il n'y a rien a repartir.
    /// </summary>
    private void DeclareIntent(UtilityContext context)
    {
        if (Survivor.CurrentGoal is not { } goal) return;
        if (Utility.Goals.Find(goal)?.Target is not { } target) return;
        if (context.Locate(target) is not { } cell) return;

        Survivor.Knowledge.DeclareIntent(Survivor.Id, goal, cell, context.Now);
    }

    /// <summary>
    /// Demande une replanification depuis l'exterieur — une consigne du joueur, un danger pose a la
    /// main. Les couts de navigation ou le domaine ont change sous les pieds du plan : c'est le plan
    /// qu'il faut reprendre, pas seulement l'itineraire.
    /// </summary>
    public void Replan(string reason)
    {
        if (!Survivor.Alive) return;

        Brain.Report(ReplanCause.ObstacleDiscovered, reason, _world.Time);
    }

    /// <summary>Tout oublier : survivant hors de combat, ou nouvelle partie.</summary>
    public void Stop(string reason)
    {
        Brain.Reset(reason);
        Follower.Clear();
        Glance.Reset();
    }

    /// <summary>
    /// Le survivant est rentre au camp : il quitte le terrain. Il sort du monde reel — sans quoi la
    /// horde continuerait de le poursuivre jusque dans le camp, et les allies restes dehors le
    /// verraient encore la ou il n'est plus.
    ///
    /// Ce qu'il a appris, lui, reste : c'est le meme survivant qui repartira, avec la meme carte
    /// mentale, la sante et la motivation dans l'etat ou il les rapporte.
    /// </summary>
    public bool ComeHome(string reason)
    {
        if (!Survivor.ComeHome(_world.Time)) return false;

        _world.RemoveEntity(Survivor.Id);
        Stop(reason);
        return true;
    }
}

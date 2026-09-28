using Hordinary.AI.Instructions;
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
/// Chaque agent a <b>sa</b> carte mentale, <b>son</b> planificateur et <b>son</b> domaine, specialise
/// par ce qu'il porte. Rien n'est partage entre survivants sauf le monde lui-meme — et le monde n'est
/// lu que par la perception.
/// </summary>
public sealed class SurvivorAgent
{
    private readonly WorldState _world;
    private readonly PerceptionSystem _perception;
    private readonly GameConfig _config;

    /// <summary>Le domaine commun, avant toute specialisation : la base de chaque recalcul.</summary>
    private PlanningDomain _baseDomain;

    private float _pursuit;

    public Survivor Survivor { get; }
    public SurvivorProfile Profile => Survivor.Profile;

    public NavigationGrid Navigation { get; }
    public AStar Astar { get; }
    public PathFollower Follower { get; } = new();
    public RearGlance Glance { get; }

    /// <summary>
    /// Domaine de planification specialise par ce que le survivant a en main (livrable 11). Il se
    /// recalcule quand l'equipement change, et pas autrement : les survivants pratiquent tous le meme
    /// repertoire au meme prix.
    /// </summary>
    public PlanningDomain Domain { get; private set; }

    /// <summary>
    /// Les contraintes actives du joueur (livrable 15), quand la mission en porte. Le survivant ne
    /// les lit jamais pour decider : elles ont deja reecrit son domaine avant qu'il ne planifie. Il
    /// les garde pour une seule raison — verifier apres coup que le plan produit dit bien ce que le
    /// joueur a demande (portillon 4).
    /// </summary>
    public TacticalInstructions? Instructions { get; set; }

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

    /// <summary>Ce que le survivant porte comme coups, relaye depuis <see cref="Runtime"/>.</summary>
    public event Action<string>? Fought;

    /// <summary>
    /// Un plan produit qui trahit une contrainte du joueur (portillon 4). C'est un defaut du
    /// compilateur, pas du survivant : il a son propre canal parce qu'il ne doit jamais se perdre
    /// dans le journal d'execution, qui est ferme par defaut.
    /// </summary>
    public event Action<string>? Violated;

    /// <summary>
    /// Plans passes au portillon 4, et combien l'ont echoue. Le bilan de fin de mission les montre au
    /// joueur : « deux consignes, quatorze plans verifies, aucun ecart » est la seule trace visible
    /// qu'une consigne a pese sur la sortie. Une violation part deja au journal, mais un journal ne
    /// se lit pas depuis l'ecran de camp.
    /// </summary>
    public int PlansVerified { get; private set; }

    /// <inheritdoc cref="PlansVerified"/>
    public int PlansViolated { get; private set; }

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
        IHorde? horde = null,
        ItemCatalogue? catalogue = null)
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

        Survivor = Survivor.FromProfile(
            profile, knowledge, config.Perception, config.Survivor, catalogue, config.Inventory);
        Survivor.Position = spawn;

        Glance = new RearGlance(Survivor.Settings);

        // A* travaille sur la carte mentale, jamais sur la carte reelle, et paie plus cher les cases
        // dont ce survivant-la garde un mauvais souvenir (specification section 16.2).
        Navigation = new NavigationGrid(knowledge.MentalMap, config.Navigation)
        {
            Sensed = cell => knowledge.Danger.LevelAt(cell, _world.Time),
            Forbidden = Survivor.Orders.IsForbidden,
            Profile = NavigationProfile.Neutral,
        };

        Astar = new AStar(Navigation);

        Follower.Teleport(spawn);
        SetPace(running: false);

        // Ce qu'on porte pese : la vitesse se recalcule des que le sac change, sans quoi un survivant
        // surcharge continuerait de courir jusqu'a ce que quelqu'un pense a le ralentir.
        Survivor.Inventory.Changed += () => SetPace(_running);
        Survivor.Died += DropCarried;

        _baseDomain = domain;
        Domain = domain;

        Utility = new UtilitySelector(goals, config.UtilityAi);
        Planner = new Planner(Domain, config.Planner);

        // Prendre un pied-de-biche en main change le prix d'une effraction, et c'est la seule chose
        // qui le change : le domaine est respecialise quand l'equipement bouge, et GBFS decouvre le
        // nouveau plan de lui-meme — aucun code ne teste ce qu'on porte.
        Survivor.Inventory.EquipmentChanged += RefreshDomain;

        Runtime = new WorldPlanRuntime(
            Survivor, world, Follower, Astar, Survivor.Settings, band, sharing, horde, catalogue,
            config.UtilityAi.Cooperation);

        Runtime.Logged += message => Logged?.Invoke(message);
        Runtime.Fought += message => Fought?.Invoke(message);
        Runtime.WorldChanged += _ => Perceive();

        // Le suiveur previent quand la case suivante devient infranchissable. Avec la carte mentale,
        // cela arrive des que la perception revele un mur sur l'itineraire prevu : c'est « un
        // obstacle est decouvert », le quatrieme declencheur du livrable 6.
        Follower.Blocked += _ => Runtime.NotifyBlocked();

        Executor = new PlanExecutor(Runtime, config.Planner);
        Brain = new Replanner(Survivor, Utility, Planner, Executor, config.Replanning);

        // Le portillon 4 se branche sur la production d'un plan, et non sur son execution : ce qu'il
        // verifie est la compilation, pas le monde. Sans contrainte active il ne coute rien.
        Brain.Replanned += _ => VerifyTrajectory();

        // Un camarade vient de donner un ordre : reconsiderer, et pas seulement demander a
        // reflechir, pour la meme raison que le rappel du van — l'ordre doit pouvoir deloger un
        // objectif installe, que l'hysteresis protegerait sinon (UtilitySelector.Reconsider).
        Survivor.Ordered += order =>
        {
            Utility.Reconsider(order.Label);
            Logged?.Invoke($"{Survivor.Name} recoit un {order}");
        };

        _world.SetEntity(Survivor.Id, EntityKind.Survivor, spawn);
    }

    /// <summary>Vrai quand le survivant court. Sa charge s'applique par-dessus, dans les deux cas.</summary>
    private bool _running;

    /// <summary>
    /// Regle l'allure. C'est le seul endroit qui fixe la vitesse du corps, pour que la penalite de
    /// surcharge ne puisse pas etre oubliee par un appelant : marcher charge est plus lent que
    /// marcher, et courir charge est plus lent que courir.
    /// </summary>
    public void SetPace(bool running)
    {
        _running = running;

        float pace = running ? Survivor.Settings.RunSpeed : Survivor.Settings.WalkSpeed;
        Follower.Speed = pace * Survivor.Inventory.SpeedMultiplier;
    }

    /// <summary>
    /// Ce qu'un corps laisse derriere lui (<c>inventory.drop_on_death</c>). Les objets tombent la ou
    /// le survivant est tombe : une mort coute alors autre chose qu'un nom en moins, et le groupe a
    /// une raison d'aller rechercher un corps.
    /// </summary>
    private void DropCarried(Survivor fallen)
    {
        if (!Survivor.Inventory.Settings.DropOnDeath) return;

        var where = Follower.CurrentCell;
        int index = 0;

        foreach (string itemId in Survivor.Inventory.DropAll())
        {
            _world.SetItem($"{itemId}-{fallen.Id}-{++index}", itemId, where);
        }

        if (index > 0) Logged?.Invoke($"{fallen.Name} tombe en {where} et laisse {index} objet(s) au sol");
    }

    /// <summary>
    /// Recompose le domaine a partir de ce que le survivant a en main. Deux outils qui portent sur la
    /// meme action se composent : une caisse a outils et un pied-de-biche font une effraction moins
    /// chere que l'un des deux seul.
    ///
    /// C'est desormais la seule chose qui change le prix d'une action. Un survivant n'a pas de talent
    /// propre : ce qu'il sait faire moins cher, il le doit a ce qu'il porte.
    /// </summary>
    private void RefreshDomain()
    {
        var modifiers = new Dictionary<string, ActionModifier>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in Survivor.Inventory.EquippedItems)
        {
            foreach (var (action, modifier) in item.ActionModifiers)
            {
                var current = modifiers.TryGetValue(action, out var existing) ? existing : ActionModifier.Neutral;

                modifiers[action] = new ActionModifier
                {
                    Cost = current.Cost * modifier.Cost,
                    Noise = current.Noise * modifier.Noise,
                    Duration = current.Duration * modifier.Duration,
                    Enabled = current.Enabled && modifier.Enabled,
                };
            }
        }

        Domain = _baseDomain.Specialize(modifiers);
        Planner.Respecialize(Domain);
    }

    /// <summary>
    /// Change le repertoire commun : c'est ce que fait une instruction tactique du joueur
    /// (livrable 15). Le domaine recu est celui que <c>DomainCompiler</c> a reecrit — des
    /// preconditions en plus, un schema scinde, un schema retire — et il remplace celui d'origine
    /// pour la duree de la mission.
    ///
    /// <para>
    /// La specialisation par l'equipement se rejoue par-dessus, dans cet ordre et pas l'autre : ce
    /// que le joueur interdit, un pied-de-biche ne le rend pas permis. Le survivant replanifie
    /// ensuite, sans quoi il continuerait de jouer le plan que la consigne vient d'invalider.
    /// </para>
    /// </summary>
    public void Rebase(PlanningDomain domain, string reason = "consigne tactique")
    {
        ArgumentNullException.ThrowIfNull(domain);

        _baseDomain = domain;
        RefreshDomain();

        // La cause est celle que le livrable 6 reserve a ce qui rend le plan impraticable sans etre
        // un echec d'action, comme un ordre du joueur : les dix causes sont exactement les dix
        // evenements de la specification, et une contrainte n'en est pas un onzieme.
        if (Survivor.Alive) Brain.Report(ReplanCause.ObstacleDiscovered, reason, _world.Time);
    }

    /// <summary>
    /// Portillon 4, sur un plan reellement produit. Le plan vient d'un domaine reecrit ; la question
    /// posee ici est celle que personne d'autre ne pose — « ce plan respecte-t-il ce que le joueur a
    /// demande, et non ce que la compilation en a fait ? ».
    ///
    /// Une violation n'est pas une faute du survivant mais du compilateur : elle part au journal
    /// plutot qu'a la corbeille, parce qu'un plan refuse ici laisserait le groupe sans plan du tout,
    /// et qu'un bug de compilation doit se voir, pas paralyser une mission.
    /// </summary>
    private void VerifyTrajectory()
    {
        if (Instructions is not { Active.Count: > 0 } instructions) return;
        if (Plan is not { } plan) return;

        var verdict = instructions.Verify(TrajectoryTrace.Of(plan));
        PlansVerified++;

        if (verdict.Satisfied) return;

        PlansViolated++;

        foreach (string violation in verdict.Violations) Violated?.Invoke(violation);
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
        LastHeard = _perception.Listen(Survivor, _world, rng, seen: LastPerception.SeenEntities);
        Survivor.Knowledge.ForgetFadedNoises(_world.Time);

        Context = NewContext();
        _pursuit = Context.PursuitPressure;

        if (elapsed <= 0d) return;

        Survivor.UpdatePanic(Context.PerceivedDanger, (float)elapsed);
    }

    /// <summary>
    /// Une passe de decision : guetter les evenements, reevaluer les priorites, replanifier si
    /// besoin, faire avancer l'action en cours. Puis annoncer ou l'on va, ce qui est la seule facon
    /// pour les autres d'eviter d'y aller aussi.
    /// </summary>
    public void Think(float delta)
    {
        var context = Context ?? NewContext();

        SettleOrder();
        Brain.Advance(context, delta);
        DeclareIntent(context);
    }

    /// <summary>
    /// Clot l'ordre d'un camarade quand il est resolu, dans un sens ou dans l'autre : rempli — le
    /// survivant est a destination —, ou impossible — il l'a pris pour objectif et le planificateur
    /// n'en a rien tire, ou l'a suspendu faute d'itineraire.
    ///
    /// <para>
    /// C'est ce qui fait d'un ordre de camarade une parenthese et non une laisse. Tant qu'il est
    /// pendant, l'entree <c>obeying</c> ferme les objectifs propres du survivant ; s'il ne se
    /// resolvait jamais, un ordre impossible le laisserait plante pour le reste de la mission, ses
    /// seuls reflexes pour l'animer. Il ne s'efface pas pour autant a la premiere difficulte : un
    /// danger qui fait passer la fuite devant ne le resout pas, il le fait attendre.
    /// </para>
    ///
    /// <para>
    /// Le verdict vient avant la decision de la passe, pour qu'un survivant arrive decide aussitot de
    /// la suite plutot que de rester une passe sans objectif — l'ordre rempli ferme
    /// <c>CompletePlayerMission</c>, et « obeying » tous les autres.
    /// </para>
    /// </summary>
    private void SettleOrder()
    {
        if (Survivor.Mission is not { FromAlly: true } order) return;

        // Un rappel au van se tient une fois rempli (MissionOrder.Holds) : on n'efface que ce qui
        // est impossible.
        bool arrived = order.ReachedFrom(Survivor.Position);
        bool reached = arrived && !order.Holds;
        bool hopeless = !arrived && PursuesOrder() && (Plan is null || Brain.Suspended);

        if (!reached && !hopeless) return;

        Survivor.Settle();
        Utility.Reconsider(reached ? "ordre rempli" : "ordre impossible");

        Logged?.Invoke(reached
            ? $"{Survivor.Name} a rempli l'{order.Label}"
            : $"{Survivor.Name} renonce a l'{order.Label} : aucun plan n'y mene");
    }

    /// <summary>
    /// Vrai quand l'objectif courant est celui qui vise l'ordre — la cible <c>mission</c> de
    /// <c>config/utility_goals.json</c>. Le code ne nomme pas l'objectif : il lit sa cible.
    /// </summary>
    private bool PursuesOrder()
        => Survivor.CurrentGoal is { } goal
           && Utility.Goals.Find(goal)?.Target is { } target
           && target.Equals(MissionTarget, StringComparison.OrdinalIgnoreCase);

    /// <summary>Nom de cible sous lequel un objectif poursuit l'ordre courant (<c>UtilityContext.Locate</c>).</summary>
    private const string MissionTarget = "mission";

    /// <summary>
    /// La situation telle que ce survivant-la la voit, a l'instant present.
    ///
    /// C'est aussi le seul endroit ou la decision recoit un mot a dire de la navigation, et il est
    /// etroit a dessein : non pas des couts ni des itineraires, mais la seule question « puis-je y
    /// aller ? ». Sans elle, l'exploration choisit des destinations qu'A* refusera, et un plan dont
    /// la premiere action echoue toujours se rebatit a l'identique — le survivant reste sur place,
    /// son objectif affiche.
    /// </summary>
    private UtilityContext NewContext() => new(
        Survivor,
        _world.Time,
        _config.UtilityAi,
        _config.Exploration,
        Reachability.LazyFrom(Navigation, Follower.CurrentCell));

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

}

using Hordinary.AI.Zombies;
using Hordinary.Agents;
using Hordinary.Config;

namespace Hordinary.World;

/// <summary>
/// Gere la population de zombies (specification section A, <c>World/EntityManager</c>) : leur
/// apparition, leur cadence de decision et le deplacement de leurs corps.
///
/// Les deux rythmes de la section A sont separes : <see cref="Advance"/> fait glisser les corps a
/// chaque frame, <see cref="Think"/> fait percevoir et decider 2 a 5 fois par seconde.
/// </summary>
public sealed class EntityManager : IHorde
{
    private readonly List<Zombie> _zombies = [];
    private readonly WorldState _world;
    private readonly PerceptionSystem _perception;
    private readonly ZombieStateMachine _brain;
    private readonly HordeBehaviour _horde;
    private readonly KnowledgeSettings _knowledgeSettings;

    public ZombieSettings Settings { get; }

    public IReadOnlyList<Zombie> Zombies => _zombies;

    /// <summary>Emis quand un zombie change d'etat. Alimente le journal des decisions.</summary>
    public event Action<Zombie, ZombieDecision>? StateChanged;

    /// <summary>
    /// Emis a chaque coup porte. Les degats arrivent avec la sante (section 19).
    ///
    /// Jamais sur quelqu'un qui se tient au van : voir <see cref="Sheltered"/>.
    /// </summary>
    public event Action<Zombie, string>? Attacked;

    public EntityManager(
        WorldState world,
        ZombieSettings? zombies = null,
        KnowledgeSettings? knowledge = null,
        PerceptionSettings? perception = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        Settings = zombies ?? ZombieSettings.Default;
        _knowledgeSettings = knowledge ?? new KnowledgeSettings();

        // Les zombies voient moins loin et moins large que les survivants : leurs propres reglages
        // priment sur la section « perception » du fichier de configuration.
        _perception = new PerceptionSystem(new PerceptionSettings
        {
            VisionRange = Settings.VisionRange,
            FieldOfViewDegrees = Settings.FieldOfViewDegrees,
            UpdateInterval = perception?.UpdateInterval ?? Settings.UpdateInterval,
        });

        _brain = new ZombieStateMachine(Settings);
        _horde = new HordeBehaviour(Settings);
    }

    // --- Apparition -----------------------------------------------------------------------

    public Zombie Spawn(string id, GridPosition cell)
    {
        // Comme tout agent, un zombie nait sans rien savoir du terrain : seules ses dimensions.
        var knowledge = new AgentKnowledge(
            id, _world.Grid.Width, _world.Grid.Height, _world.Grid.Origin, _knowledgeSettings);

        var zombie = new Zombie(id, knowledge, Settings, Settings.Seed + _zombies.Count);
        zombie.PlaceAt(cell);
        zombie.Follower.Speed = Settings.WanderSpeed;

        _zombies.Add(zombie);
        _world.SetEntity(id, EntityKind.Zombie, cell);
        return zombie;
    }

    /// <summary>
    /// Pose la horde aux emplacements deja choisis. C'est la voie des quartiers generes (livrable
    /// 13) : le generateur a tire ces cases avec sa propre graine, en meme temps que le reste du
    /// quartier, et refaire ici un tirage sur une autre graine reviendrait a decider deux fois.
    ///
    /// Une case injouable est ignoree plutot que corrigee : le generateur a deja garanti qu'elles
    /// sont praticables et a distance, et rattraper silencieusement une erreur de sa part la
    /// rendrait invisible.
    /// </summary>
    /// <returns>Le nombre de zombies reellement poses.</returns>
    public int SpawnHorde(IReadOnlyList<GridPosition> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);

        int spawned = 0;

        foreach (var cell in cells)
        {
            if (!_world.Grid.IsWalkable(cell)) continue;

            Spawn($"zombie-{spawned + 1}", cell);
            spawned++;
        }

        return spawned;
    }

    /// <summary>
    /// Repartit <see cref="ZombieSettings.Count"/> zombies sur des cases praticables distinctes, a
    /// bonne distance du point de depart des survivants. Le tirage suit
    /// <see cref="ZombieSettings.Seed"/> : la meme carte donne toujours la meme horde.
    /// </summary>
    public int SpawnHorde(GridPosition awayFrom, int? count = null)
    {
        int wanted = count ?? Settings.Count;
        if (wanted <= 0) return 0;

        var rng = new Random(Settings.Seed);

        var candidates = _world.Grid.AllPositions()
            .Where(_world.Grid.IsWalkable)
            .Where(cell => Distance(cell, awayFrom) >= Settings.MinimumSpawnDistance)
            .ToList();

        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        int spawned = 0;
        foreach (var cell in candidates)
        {
            if (spawned >= wanted) break;

            Spawn($"zombie-{spawned + 1}", cell);
            spawned++;
        }

        return spawned;
    }

    public Zombie? Find(string id)
        => _zombies.FirstOrDefault(zombie => zombie.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Retire un zombie mis a terre (livrable 8). Il quitte la horde <b>et</b> le monde : sans le
    /// second, la perception continuerait a le voir la ou il est tombe, et les survivants
    /// contourneraient un cadavre pendant le reste de la partie.
    ///
    /// Ce que les agents en ont deja memorise n'est pas efface pour autant — une croyance perimee
    /// s'oublie toute seule, et c'est le comportement voulu.
    /// </summary>
    public bool Remove(string id)
    {
        if (Find(id) is not { } zombie) return false;

        _zombies.Remove(zombie);
        _world.RemoveEntity(zombie.Id);
        Removed?.Invoke(zombie);
        return true;
    }

    /// <summary>Emis quand un zombie quitte le jeu. La couche moteur y retire son noeud d'affichage.</summary>
    public event Action<Zombie>? Removed;

    // --- Boucle ---------------------------------------------------------------------------

    /// <summary>Deplace les corps. A appeler a chaque frame.</summary>
    public void Advance(float delta)
    {
        foreach (var zombie in _zombies)
        {
            zombie.Follower.Advance(delta, _world.Grid.IsWalkable);
            _world.SetEntity(zombie.Id, EntityKind.Zombie, zombie.Position);
        }
    }

    /// <summary>
    /// Une passe de reflexion : chaque zombie regarde, ecoute, choisit un etat puis un pas.
    /// A appeler 2 a 5 fois par seconde.
    /// </summary>
    public void Think(double now)
    {
        foreach (var zombie in _zombies)
        {
            _perception.Observe(zombie, _world, now, InVan);
            _perception.Listen(zombie, _world, zombie.Rng, now);
            zombie.Knowledge.ForgetFadedNoises(now);

            var decision = _brain.Decide(zombie, now);
            Apply(zombie, decision, now);
        }
    }

    private void Apply(Zombie zombie, ZombieDecision decision, double now)
    {
        if (decision.State != zombie.State) StateChanged?.Invoke(zombie, decision);

        zombie.State = decision.State;
        zombie.Reason = decision.Reason;
        zombie.Destination = decision.Destination;
        zombie.Follower.Speed = Settings.SpeedFor(decision.State);

        if (decision.State == ZombieState.Attack)
        {
            zombie.Follower.Clear();

            // Le van protege. La horde se presse contre le vehicule, elle n'atteint pas ce qui est
            // dedans : rentrer est une fin, pas une pause. Sans cette regle, le premier rentre passait
            // le reste de la mission a se faire entamer en attendant les autres, et le groupe etait
            // puni de la seule chose que la victoire lui demande — s'attendre.
            if (Sheltered(zombie.TargetId)) return;

            if (now - zombie.LastAttackAt >= Settings.AttackInterval)
            {
                zombie.LastAttackAt = now;
                Attacked?.Invoke(zombie, zombie.TargetId ?? "?");
            }

            return;
        }

        var step = _horde.ChooseStep(zombie, decision.Destination, _zombies, _world.Grid);

        if (step is { } cell) zombie.Follower.SetPath([zombie.Position, cell]);
        else zombie.Follower.Clear();
    }

    /// <summary>
    /// Cette cible-la est-elle a l'abri ? Le van, et lui seul : la zone <see cref="ZoneKind.Extraction"/>
    /// de la carte, dessinee ou generee. C'est le seul endroit du quartier ou l'on ne peut pas etre
    /// frappe, et c'est ce qui fait du retour une conclusion plutot qu'une station debout sous les
    /// coups — les survivants se conduisent deja comme si le van etait sur, encore fallait-il qu'il le
    /// soit.
    ///
    /// La regle est posee ici, sur le monde, et non dans la decision du zombie : sa machine a etats ne
    /// lit que sa propre memoire (invariant 2), et lui apprendre ou est le van reviendrait a lui donner
    /// la carte. Il decide donc d'attaquer comme avant, et se heurte a la tole.
    ///
    /// Une carte sans zone d'extraction n'abrite personne. C'est le bon defaut : un abri se declare
    /// dans <c>config/map_locations.json</c>, il ne s'invente pas.
    /// </summary>
    public bool Sheltered(string? targetId)
        => targetId is { Length: > 0 }
        && _world.Grid.GetZone(ZoneKind.Extraction) is { } van
        && _world.GetEntity(targetId) is { } target
        && van.Contains(target.Position);

    /// <summary>
    /// Un survivant dans le van ne se voit pas du dehors. C'est la moitie de l'abri qui manquait :
    /// le coup s'arretait sur la tole, mais le zombie voyait toujours sa proie derriere et restait a
    /// cogner — indefiniment. Tant qu'il cognait, il se tenait a l'entree du van, et le camarade qui
    /// rentrait apres les autres le voyait, fuyait, revenait au rappel, fuyait encore : la mission ne
    /// se concluait jamais. C'est le mode demonstration qui l'a montre (« Faites diversion avant
    /// d'entrer », sans sa consigne). Desormais le zombie perd sa proie de vue, fouille les abords
    /// comme apres n'importe quelle piste froide, puis repart errer.
    ///
    /// La decision du zombie n'apprend toujours rien du van : c'est son regard qui ne traverse pas
    /// la tole, comme il ne traverse pas un mur.
    /// </summary>
    private bool InVan(WorldEntity entity)
        => entity.Kind == EntityKind.Survivor && Sheltered(entity.Id);

    private static float Distance(GridPosition a, GridPosition b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}

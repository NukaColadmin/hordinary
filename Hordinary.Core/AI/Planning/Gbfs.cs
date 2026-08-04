using System.Diagnostics;
using Hordinary.Config;

namespace Hordinary.AI.Planning;

/// <summary>Pourquoi une recherche n'a pas abouti.</summary>
public enum PlanningFailure
{
    None = 0,

    /// <summary>L'objectif n'est traduit en conditions symboliques dans aucun fichier de domaine.</summary>
    UnknownGoal,

    /// <summary>L'espace atteignable a ete epuise sans trouver le but : le plan demande est impossible.</summary>
    NoPlanFound,

    /// <summary>Plafond de noeuds atteint (<see cref="PlannerSettings.MaxNodes"/>).</summary>
    NodeLimitReached,

    /// <summary>Le but n'est atteignable qu'au-dela de <see cref="PlannerSettings.MaxDepth"/> actions.</summary>
    DepthLimitReached,

    /// <summary>Budget de temps epuise (<see cref="PlannerSettings.MaxTimeMs"/>).</summary>
    TimeLimitReached,
}

/// <summary>Ce que rapporte une recherche : le plan trouve, ou la raison de l'echec, et son cout.</summary>
public sealed record GbfsResult(
    bool Found,
    IReadOnlyList<PlanningAction> Actions,
    float Cost,
    int ExploredNodes,
    int GeneratedNodes,
    double ElapsedMs,
    PlanningFailure Failure)
{
    public static GbfsResult Success(IReadOnlyList<PlanningAction> actions, float cost, int explored, int generated, double elapsed)
        => new(true, actions, cost, explored, generated, elapsed, PlanningFailure.None);

    public static GbfsResult Fail(PlanningFailure failure, int explored, int generated, double elapsed)
        => new(false, [], 0f, explored, generated, elapsed, failure);

    public string Describe() => Found
        ? $"plan de {Actions.Count} actions, cout {Cost:0.##}, {ExploredNodes} noeuds explores en {ElapsedMs:0.##} ms"
        : $"echec ({Reason}), {ExploredNodes} noeuds explores en {ElapsedMs:0.##} ms";

    public string Reason => Failure switch
    {
        PlanningFailure.None => "aucun",
        PlanningFailure.UnknownGoal => "objectif inconnu du domaine",
        PlanningFailure.NoPlanFound => "aucune suite d'actions ne mene au but",
        PlanningFailure.NodeLimitReached => "plafond de noeuds atteint",
        PlanningFailure.DepthLimitReached => "profondeur maximale atteinte",
        PlanningFailure.TimeLimitReached => "temps de planification depasse",
        _ => Failure.ToString(),
    };

    public override string ToString() => Describe();
}

/// <summary>
/// Greedy Best-First Search sur l'etat symbolique (specification, livrable 5).
///
/// La priorite d'un noeud est <b>uniquement</b> son heuristique — pas <c>g + h</c> : c'est ce qui
/// distingue GBFS de A*. La recherche est donc rapide mais non optimale ; le cout accumule ne sert
/// qu'a rendre compte du plan retenu, jamais a le choisir.
///
/// Trois garde-fous, tous configurables (section <c>planner</c> de <c>config/game_config.json</c>) :
/// un plafond de noeuds, une profondeur maximale et un budget de temps. Un agent ne doit jamais
/// pouvoir geler une frame en cherchant un plan qui n'existe pas.
/// </summary>
public sealed class Gbfs
{
    private readonly PlanningProblem _problem;
    private readonly Heuristic _heuristic;
    private readonly PlannerSettings _settings;

    public Gbfs(PlanningProblem problem, PlannerSettings? settings = null, Heuristic? heuristic = null)
    {
        _problem = problem ?? throw new ArgumentNullException(nameof(problem));
        _settings = settings ?? new PlannerSettings();
        _heuristic = heuristic ?? new Heuristic(problem, _settings);
    }

    public GbfsResult Search()
    {
        var stopwatch = Stopwatch.StartNew();

        // Le but est deja atteint : le plan vide est le bon plan.
        if (_problem.IsSatisfiedBy(_problem.Initial))
            return GbfsResult.Success([], 0f, 0, 0, stopwatch.Elapsed.TotalMilliseconds);

        var open = new PriorityQueue<Node, (float Estimate, int Order)>();
        var closed = new HashSet<string>(StringComparer.Ordinal);

        int explored = 0;
        int generated = 1;
        bool depthLimited = false;

        open.Enqueue(new Node(_problem.Initial, null, null, 0, 0f, 0f), (_heuristic.Estimate(_problem.Initial, 0f), 0));

        while (open.TryDequeue(out var current, out _))
        {
            // Un meme etat peut avoir ete atteint par plusieurs chemins : on ne le developpe qu'une fois.
            if (!closed.Add(current.State.Hash)) continue;

            explored++;

            if (_problem.IsSatisfiedBy(current.State))
            {
                return GbfsResult.Success(
                    Reconstruct(current), current.Cost, explored, generated, stopwatch.Elapsed.TotalMilliseconds);
            }

            if (explored >= _settings.MaxNodes)
                return GbfsResult.Fail(PlanningFailure.NodeLimitReached, explored, generated, stopwatch.Elapsed.TotalMilliseconds);

            if (stopwatch.Elapsed.TotalMilliseconds >= _settings.MaxTimeMs)
                return GbfsResult.Fail(PlanningFailure.TimeLimitReached, explored, generated, stopwatch.Elapsed.TotalMilliseconds);

            // Le plan ne doit pas depasser la profondeur autorisee : on cesse de developper, sans
            // pour autant conclure a l'impossibilite — une branche plus courte reste peut-etre ouverte.
            if (current.Depth >= _settings.MaxDepth)
            {
                depthLimited = true;
                continue;
            }

            foreach (var action in _problem.Actions)
            {
                if (!action.IsApplicable(current.State)) continue;

                var successor = action.ApplyTo(current.State);

                // Une action qui ne change rien ne fait pas avancer le plan, elle l'allonge.
                if (successor.Hash == current.State.Hash) continue;
                if (closed.Contains(successor.Hash)) continue;

                float noise = current.Noise + action.NoiseLevel;
                var node = new Node(
                    successor,
                    current,
                    action,
                    current.Depth + 1,
                    current.Cost + _problem.TransitionCost(current.State, action),
                    noise);

                open.Enqueue(node, (_heuristic.Estimate(successor, noise), generated));
                generated++;
            }
        }

        var failure = depthLimited ? PlanningFailure.DepthLimitReached : PlanningFailure.NoPlanFound;
        return GbfsResult.Fail(failure, explored, generated, stopwatch.Elapsed.TotalMilliseconds);
    }

    private static List<PlanningAction> Reconstruct(Node node)
    {
        var actions = new List<PlanningAction>();

        for (var step = node; step.Action is not null; step = step.Parent!) actions.Add(step.Action);

        actions.Reverse();
        return actions;
    }

    /// <summary>
    /// Un noeud de la recherche. <c>Order</c> n'y figure pas : il est porte par la priorite, ou il
    /// departage deterministiquement deux estimations egales — sans quoi deux planifications sur la
    /// meme memoire pourraient rendre deux plans differents.
    /// </summary>
    private sealed record Node(
        SymbolicState State,
        Node? Parent,
        PlanningAction? Action,
        int Depth,
        float Cost,
        float Noise);
}

using Hordinary.World;

namespace Hordinary.AI.Planning;

/// <summary>
/// Un probleme de planification : d'ou l'agent part, ou il veut arriver, avec quels objets et quelles
/// actions concretes. C'est l'unite de travail de <see cref="Gbfs"/>.
///
/// Tout y est deja derive de la memoire de l'agent par <see cref="SymbolicStateBuilder"/> : la
/// recherche n'a plus aucun moyen d'aller consulter le monde reel, ce qui garantit qu'un plan ne peut
/// pas s'appuyer sur une porte, un lieu ou un zombie que le survivant n'a jamais percu.
/// </summary>
public sealed class PlanningProblem
{
    /// <summary>Nom de l'agent dans les faits symboliques : <c>at(agent, pharmacy)</c>.</summary>
    public const string Agent = "agent";

    private readonly Dictionary<string, PlanningObject> _objects;
    private readonly Dictionary<string, float> _danger;

    public string GoalName { get; }

    public SymbolicState Initial { get; }

    public IReadOnlyList<Condition> Goal { get; }

    public IReadOnlyList<PlanningObject> Objects { get; }

    /// <summary>Actions deja instanciees sur les objets connus : c'est l'espace de branchement de GBFS.</summary>
    public IReadOnlyList<PlanningAction> Actions { get; }

    /// <summary>Position reelle de l'agent au moment ou le probleme a ete construit.</summary>
    public GridPosition AgentPosition { get; }

    public PlanningProblem(
        string goalName,
        SymbolicState initial,
        IReadOnlyList<Condition> goal,
        IReadOnlyList<PlanningObject> objects,
        IReadOnlyList<PlanningAction> actions,
        GridPosition agentPosition,
        IReadOnlyDictionary<string, float>? danger = null)
    {
        GoalName = goalName;
        Initial = initial;
        Goal = goal;
        Objects = objects;
        Actions = actions;
        AgentPosition = agentPosition;

        _objects = new Dictionary<string, PlanningObject>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in objects)
        {
            // Un nom d'objet est une cle dans les faits : deux objets homonymes rendraient
            // « at(agent, medicine) » ambigu. Mieux vaut le refuser que planifier au hasard.
            if (_objects.TryAdd(candidate.Name, candidate)) continue;

            throw new InvalidOperationException(
                $"Deux objets portent le nom « {candidate.Name} » ({_objects[candidate.Name].Type} et {candidate.Type}).");
        }

        _danger = danger is null
            ? new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, float>(danger, StringComparer.OrdinalIgnoreCase);

        GoalPositions = ResolveGoalPositions();
    }

    public PlanningObject? Find(string? name)
        => name is not null && _objects.TryGetValue(name, out var found) ? found : null;

    public GridPosition? PositionOf(string? name) => Find(name)?.Position;

    /// <summary>Danger memorise sur un lieu, entre 0 et 1. Un objet sans position n'est jamais dangereux.</summary>
    public float DangerOf(string? name)
        => name is not null && _danger.TryGetValue(name, out float danger) ? danger : 0f;

    public bool IsSatisfiedBy(SymbolicState state) => state.Satisfies(Goal);

    /// <summary>
    /// Ou se trouve l'agent dans un etat donne, d'apres le fait <c>at(agent, ...)</c>. Un etat qui ne
    /// le dit pas laisse l'agent la ou il etait : la planification part toujours d'un corps quelque part.
    /// </summary>
    public GridPosition AgentPositionIn(SymbolicState state) => PositionOf(AgentLocationIn(state)) ?? AgentPosition;

    /// <summary>Nom du lieu ou l'agent se tient dans un etat donne, ou <c>null</c> s'il est entre deux lieux.</summary>
    public string? AgentLocationIn(SymbolicState state)
    {
        foreach (var fact in state.Facts)
        {
            if (!Fact.PredicateOf(fact).Equals("at", StringComparison.OrdinalIgnoreCase)) continue;

            var arguments = Fact.ArgumentsOf(fact);
            if (arguments.Length == 2 && arguments[0].Equals(Agent, StringComparison.OrdinalIgnoreCase))
                return arguments[1];
        }

        return null;
    }

    /// <summary>
    /// Cout reel d'une transition : le cout fixe de l'action, augmente du trajet a parcourir pour les
    /// actions de deplacement. GBFS n'ordonne pas la recherche dessus — sa priorite est purement
    /// heuristique — mais le cout total du plan retenu doit rester une information honnete pour le
    /// journal et le panneau debug.
    /// </summary>
    public float TransitionCost(SymbolicState state, PlanningAction action)
    {
        if (action.Schema.DistanceCost <= 0f || action.TargetObject is not { } target) return action.Cost;
        if (PositionOf(target) is not { } destination) return action.Cost;

        int distance = AgentPositionIn(state).ChebyshevDistanceTo(destination);
        return action.Cost + action.Schema.DistanceCost * distance;
    }

    /// <summary>
    /// Lieux nommes dans les conditions du but : les cibles vers lesquelles tire le terme de distance
    /// de l'heuristique. Resolus une fois a la construction, car l'heuristique est evaluee des
    /// milliers de fois par planification.
    /// </summary>
    public IReadOnlyList<GridPosition> GoalPositions { get; }

    private List<GridPosition> ResolveGoalPositions()
    {
        var positions = new List<GridPosition>();

        foreach (var condition in Goal)
        {
            if (!condition.Expected) continue;

            foreach (var argument in Fact.ArgumentsOf(condition.Pattern))
            {
                if (PositionOf(argument) is { } cell && !positions.Contains(cell)) positions.Add(cell);
            }
        }

        return positions;
    }

    public override string ToString()
        => $"{GoalName} : {string.Join(" et ", Goal)} — {Objects.Count} objets, {Actions.Count} actions instanciees";
}

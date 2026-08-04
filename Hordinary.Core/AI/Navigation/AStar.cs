using Hordinary.World;

namespace Hordinary.AI.Navigation;

/// <summary>
/// A* sur la grille. Le modele de cout appartient a <see cref="NavigationGrid"/> : cette classe
/// ne connait ni les murs, ni le danger, ni la personnalite.
///
/// Livrable 1 : la recherche s'effectue sur la carte reelle. Au livrable 2 il suffira de construire
/// la <see cref="NavigationGrid"/> depuis la carte mentale de l'agent (<c>AgentKnowledge</c>) :
/// cette classe reste inchangee.
/// </summary>
public sealed class AStar(NavigationGrid grid)
{
    private readonly NavigationGrid _grid = grid ?? throw new ArgumentNullException(nameof(grid));

    /// <summary>
    /// Cellules fermees lors de la derniere recherche. Conservees telles quelles (aucun cout
    /// supplementaire) pour que l'overlay de debug puisse afficher l'espace explore.
    /// </summary>
    public IReadOnlyCollection<GridPosition> LastExploredNodes { get; private set; } = [];

    public PathResult FindPath(GridPosition start, GridPosition goal)
    {
        LastExploredNodes = [];

        if (!_grid.Map.Contains(start)) return PathResult.Failure(PathFailureReason.StartOutsideMap, 0);
        if (!_grid.Map.Contains(goal)) return PathResult.Failure(PathFailureReason.GoalOutsideMap, 0);
        if (!_grid.IsTraversable(goal)) return PathResult.Failure(PathFailureReason.GoalBlocked, 0);
        if (start == goal) return PathResult.Success([start], 0f, 0);

        var open = new PriorityQueue<GridPosition, float>();
        var cameFrom = new Dictionary<GridPosition, GridPosition>();
        var gScore = new Dictionary<GridPosition, float> { [start] = 0f };
        var closed = new HashSet<GridPosition>();
        int explored = 0;

        LastExploredNodes = closed;
        open.Enqueue(start, Heuristic(start, goal));

        while (open.TryDequeue(out var current, out _))
        {
            // Une position peut avoir ete empilee plusieurs fois : on ignore les doublons perimes.
            if (!closed.Add(current)) continue;
            explored++;

            if (current == goal) return Reconstruct(cameFrom, goal, gScore[goal], explored);

            if (explored >= _grid.Settings.MaxExploredNodes)
                return PathResult.Failure(PathFailureReason.SearchLimitReached, explored);

            float currentScore = gScore[current];

            foreach (var (neighbour, stepMultiplier) in _grid.GetNeighbours(current))
            {
                if (closed.Contains(neighbour)) continue;

                float enterCost = _grid.GetEnterCost(neighbour);
                if (float.IsPositiveInfinity(enterCost)) continue;

                float tentative = currentScore + enterCost * stepMultiplier;
                if (gScore.TryGetValue(neighbour, out float best) && tentative >= best) continue;

                gScore[neighbour] = tentative;
                cameFrom[neighbour] = current;
                open.Enqueue(neighbour, tentative + Heuristic(neighbour, goal));
            }
        }

        return PathResult.Failure(PathFailureReason.NoRouteFound, explored);
    }

    /// <summary>
    /// Distance de grille multipliee par le cout minimal d'une cellule : jamais surestimee,
    /// donc A* reste optimal.
    /// </summary>
    private float Heuristic(GridPosition from, GridPosition to)
    {
        int distance = _grid.Settings.AllowDiagonal
            ? from.ChebyshevDistanceTo(to)
            : from.ManhattanDistanceTo(to);

        return distance * _grid.MinimumCellCost;
    }

    private static PathResult Reconstruct(
        Dictionary<GridPosition, GridPosition> cameFrom,
        GridPosition goal,
        float cost,
        int explored)
    {
        var cells = new List<GridPosition> { goal };
        var current = goal;
        while (cameFrom.TryGetValue(current, out var previous))
        {
            current = previous;
            cells.Add(current);
        }

        cells.Reverse();
        return PathResult.Success(cells, cost, explored);
    }
}

using Hordinary.World;

namespace Hordinary.AI.Navigation;

public enum PathFailureReason
{
    None = 0,
    StartOutsideMap,
    GoalOutsideMap,
    GoalBlocked,
    NoRouteFound,
    SearchLimitReached,
}

/// <summary>Resultat d'une recherche A*. <see cref="ExploredNodeCount"/> alimente l'overlay de debug.</summary>
public sealed class PathResult
{
    private PathResult(IReadOnlyList<GridPosition> cells, float cost, int explored, PathFailureReason reason)
    {
        Cells = cells;
        Cost = cost;
        ExploredNodeCount = explored;
        FailureReason = reason;
    }

    /// <summary>Chemin complet, case de depart incluse et case d'arrivee incluse.</summary>
    public IReadOnlyList<GridPosition> Cells { get; }

    public float Cost { get; }
    public int ExploredNodeCount { get; }
    public PathFailureReason FailureReason { get; }

    public bool Found => Cells.Count > 0;

    /// <summary>Nombre de deplacements a effectuer (une case de moins que <see cref="Cells"/>).</summary>
    public int StepCount => Math.Max(0, Cells.Count - 1);

    public static PathResult Success(IReadOnlyList<GridPosition> cells, float cost, int explored)
        => new(cells, cost, explored, PathFailureReason.None);

    public static PathResult Failure(PathFailureReason reason, int explored)
        => new([], 0f, explored, reason);

    public override string ToString() => Found
        ? $"chemin de {Cells.Count} cases, cout {Cost:0.##}, {ExploredNodeCount} noeuds explores"
        : $"echec ({FailureReason}), {ExploredNodeCount} noeuds explores";
}

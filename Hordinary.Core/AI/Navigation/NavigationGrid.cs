using Hordinary.World;

namespace Hordinary.AI.Navigation;

/// <summary>
/// Vue « navigable » d'une <see cref="GridMap"/> : elle transforme un type de cellule et un niveau
/// de danger en cout de franchissement, et fournit les voisins. C'est le seul endroit qui connait
/// le modele de cout ; A* ne fait qu'appliquer une recherche dessus.
/// </summary>
public sealed class NavigationGrid(
    GridMap map,
    NavigationSettings? settings = null,
    NavigationProfile? profile = null)
{
    private readonly Dictionary<GridPosition, DangerLevel> _danger = [];

    private const float DiagonalStepMultiplier = 1.41421356f;

    public GridMap Map { get; } = map ?? throw new ArgumentNullException(nameof(map));
    public NavigationSettings Settings { get; } = settings ?? NavigationSettings.Default;
    public NavigationProfile Profile { get; set; } = profile ?? NavigationProfile.Neutral;

    /// <summary>Cout minimal possible d'une cellule : garantit une heuristique admissible.</summary>
    public float MinimumCellCost => Settings.KnownCellCost;

    /// <summary>
    /// Danger que l'agent se rappelle, en plus de celui pose a la main. C'est par la que la carte de
    /// chaleur de <see cref="Agents.DangerMemory"/> devient un cout de trajet : A* contourne alors
    /// l'endroit ou le survivant a vu un zombie, meme s'il ne le voit plus.
    ///
    /// La navigation ne lit pas la memoire directement — elle ne connait ni les agents ni l'horloge.
    /// C'est l'assemblage qui branche les deux, et qui peut donc ne pas le faire.
    /// </summary>
    public Func<GridPosition, DangerLevel>? Sensed { get; set; }

    /// <summary>Le plus grave des deux : ce qu'on a pose a la main, ce dont l'agent se souvient.</summary>
    public DangerLevel GetDanger(GridPosition p)
    {
        var marked = _danger.TryGetValue(p, out var level) ? level : DangerLevel.None;
        var sensed = Sensed?.Invoke(p) ?? DangerLevel.None;

        return marked >= sensed ? marked : sensed;
    }

    public void SetDanger(GridPosition p, DangerLevel level)
    {
        if (level == DangerLevel.None) _danger.Remove(p);
        else _danger[p] = level;
    }

    public void ClearDanger() => _danger.Clear();

    public IReadOnlyDictionary<GridPosition, DangerLevel> DangerCells => _danger;

    /// <summary>
    /// Cellules que le joueur a mises hors limites (livrable 8, « definir une zone interdite »).
    /// C'est la seule interdiction absolue de la navigation : le danger, lui, ne fait que rencherir.
    /// Une consigne du joueur ne se paie pas plus cher, elle ne s'emprunte pas.
    ///
    /// Comme <see cref="Sensed"/>, la grille ne lit pas les consignes elle-meme — elle ne connait ni
    /// les agents ni le joueur. C'est l'assemblage qui branche les deux.
    /// </summary>
    public Func<GridPosition, bool>? Forbidden { get; set; }

    public bool IsTraversable(GridPosition p) => Map.IsWalkable(p) && !(Forbidden?.Invoke(p) ?? false);

    /// <summary>
    /// Cout d'entree dans une cellule, ou <see cref="float.PositiveInfinity"/> si elle est bloquee.
    /// Le danger remplace le cout de base plutot que de s'y ajouter, conformement au bareme de la
    /// specification (une cellule dangereuse coute 8, pas 8 + 1).
    /// </summary>
    public float GetEnterCost(GridPosition p)
    {
        if (!IsTraversable(p)) return float.PositiveInfinity;

        var cell = Map.GetCell(p);

        float baseCost = cell == CellType.Unknown
            ? Settings.UnknownCellCost * Profile.UnknownCostMultiplier
            : Settings.KnownCellCost;

        float dangerCost = GetDanger(p) switch
        {
            DangerLevel.Suspected => Settings.SuspectedDangerCellCost * Profile.DangerCostMultiplier,
            DangerLevel.ZombieNearby => Settings.DangerousCellCost * Profile.DangerCostMultiplier,
            _ => 0f,
        };

        float cost = Math.Max(baseCost, dangerCost);

        if (cell == CellType.DoorClosed) cost += Settings.ClosedDoorCost;

        return cost;
    }

    /// <summary>
    /// Voisins franchissables, avec le multiplicateur de distance du pas (1 en orthogonal,
    /// racine de 2 en diagonale). Le passage « en coin » entre deux obstacles est interdit.
    /// </summary>
    public IEnumerable<(GridPosition Position, float StepMultiplier)> GetNeighbours(GridPosition from)
    {
        foreach (var offset in GridPosition.OrthogonalOffsets)
        {
            var neighbour = from + offset;
            if (IsTraversable(neighbour)) yield return (neighbour, 1f);
        }

        if (!Settings.AllowDiagonal) yield break;

        foreach (var offset in GridPosition.DiagonalOffsets)
        {
            var neighbour = from + offset;
            if (!IsTraversable(neighbour)) continue;

            // Pas de coupe de coin : les deux cellules orthogonales adjacentes doivent etre libres.
            if (!IsTraversable(new GridPosition(from.X + offset.X, from.Y))) continue;
            if (!IsTraversable(new GridPosition(from.X, from.Y + offset.Y))) continue;

            yield return (neighbour, DiagonalStepMultiplier);
        }
    }
}

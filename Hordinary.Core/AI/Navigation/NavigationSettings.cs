namespace Hordinary.AI.Navigation;

/// <summary>
/// Couts de deplacement (specification section 16.2). Charges depuis <c>config/game_config.json</c>,
/// section <c>navigation</c> : aucune de ces valeurs n'est codee en dur ailleurs.
/// </summary>
public sealed class NavigationSettings
{
    /// <summary>Cellule libre connue.</summary>
    public float KnownCellCost { get; init; } = 1f;

    /// <summary>Cellule jamais observee : franchissable, mais jamais aussi sure qu'une cellule connue.</summary>
    public float UnknownCellCost { get; init; } = 4f;

    /// <summary>Cellule avec danger suspecte.</summary>
    public float SuspectedDangerCellCost { get; init; } = 8f;

    /// <summary>Cellule proche d'un zombie.</summary>
    public float DangerousCellCost { get; init; } = 15f;

    /// <summary>Surcout d'ouverture d'une porte fermee (mais non verrouillee).</summary>
    public float ClosedDoorCost { get; init; } = 3f;

    /// <summary>
    /// Deplacements en diagonale (8 voisins). Actives : les trajets suivent la ligne de vue au lieu
    /// de monter en escalier. Le passage « en coin » entre deux obstacles reste interdit, un pas
    /// diagonal coute racine de 2 fois le cout de la cellule.
    /// </summary>
    public bool AllowDiagonal { get; init; } = true;

    /// <summary>Garde-fou de recherche A* (evite de geler une frame sur une carte sans solution).</summary>
    public int MaxExploredNodes { get; init; } = 4000;

    public static NavigationSettings Default { get; } = new();
}

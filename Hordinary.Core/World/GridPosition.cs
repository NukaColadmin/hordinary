namespace Hordinary.World;

/// <summary>
/// Coordonnee entiere sur la grille de jeu.
/// Les coordonnees sont celles affichees par l'editeur Godot (elles peuvent etre negatives) :
/// <see cref="GridMap.Origin"/> fait la traduction vers l'indexation interne.
/// </summary>
public readonly record struct GridPosition(int X, int Y)
{
    public static readonly GridPosition Zero = new(0, 0);

    /// <summary>Les quatre voisins orthogonaux, dans l'ordre haut / droite / bas / gauche.</summary>
    public static readonly GridPosition[] OrthogonalOffsets =
    [
        new(0, -1), new(1, 0), new(0, 1), new(-1, 0),
    ];

    /// <summary>Les quatre voisins diagonaux.</summary>
    public static readonly GridPosition[] DiagonalOffsets =
    [
        new(1, -1), new(1, 1), new(-1, 1), new(-1, -1),
    ];

    public int ManhattanDistanceTo(GridPosition other)
        => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);

    public int ChebyshevDistanceTo(GridPosition other)
        => Math.Max(Math.Abs(X - other.X), Math.Abs(Y - other.Y));

    public static GridPosition operator +(GridPosition a, GridPosition b) => new(a.X + b.X, a.Y + b.Y);

    public static GridPosition operator -(GridPosition a, GridPosition b) => new(a.X - b.X, a.Y - b.Y);

    public override string ToString() => $"({X},{Y})";
}

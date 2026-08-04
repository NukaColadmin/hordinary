namespace Hordinary.World;

/// <summary>
/// Grille de jeu : type de chaque cellule, marqueur interieur/exterieur, et lieux nommes.
/// C'est la verite du monde ; a partir du livrable 2 les agents raisonneront sur leur propre
/// copie partielle (<c>AgentKnowledge</c>) et non sur cette carte.
/// </summary>
public sealed class GridMap
{
    private readonly CellType[] _cells;
    private readonly bool[] _indoor;
    private readonly Dictionary<string, MapZone> _zones = new(StringComparer.OrdinalIgnoreCase);

    public int Width { get; }
    public int Height { get; }

    /// <summary>Coin superieur gauche en coordonnees de tuiles (peut etre negatif).</summary>
    public GridPosition Origin { get; }

    public GridPosition Max => new(Origin.X + Width - 1, Origin.Y + Height - 1);
    public int CellCount => _cells.Length;
    public IReadOnlyCollection<MapZone> Zones => _zones.Values;

    public GridMap(int width, int height, GridPosition origin = default, CellType fill = CellType.Unknown)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        Width = width;
        Height = height;
        Origin = origin;
        _cells = new CellType[width * height];
        _indoor = new bool[width * height];
        if (fill != CellType.Unknown) Array.Fill(_cells, fill);
    }

    public bool Contains(GridPosition p)
        => p.X >= Origin.X && p.X < Origin.X + Width
        && p.Y >= Origin.Y && p.Y < Origin.Y + Height;

    /// <summary>Hors carte renvoie <see cref="CellType.Unknown"/> ; utiliser <see cref="Contains"/> pour distinguer.</summary>
    public CellType GetCell(GridPosition p)
        => Contains(p) ? _cells[Index(p)] : CellType.Unknown;

    public void SetCell(GridPosition p, CellType type)
    {
        if (Contains(p)) _cells[Index(p)] = type;
    }

    public bool IsIndoor(GridPosition p) => Contains(p) && _indoor[Index(p)];

    public void SetIndoor(GridPosition p, bool indoor)
    {
        if (Contains(p)) _indoor[Index(p)] = indoor;
    }

    /// <summary>Praticable physiquement. Hors carte est toujours faux.</summary>
    public bool IsWalkable(GridPosition p) => Contains(p) && !_cells[Index(p)].BlocksMovement();

    public void AddZone(MapZone zone) => _zones[zone.Name] = zone;

    public MapZone? GetZone(string name)
        => _zones.TryGetValue(name, out var zone) ? zone : null;

    public MapZone? GetZone(ZoneKind kind)
        => _zones.Values.FirstOrDefault(z => z.Kind == kind);

    public IEnumerable<MapZone> ZonesAt(GridPosition p) => _zones.Values.Where(z => z.Contains(p));

    public IEnumerable<GridPosition> AllPositions()
    {
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                yield return new GridPosition(Origin.X + x, Origin.Y + y);
            }
        }
    }

    public int CountCells(CellType type) => _cells.Count(c => c == type);

    /// <summary>Premiere cellule praticable d'une zone, en partant de son centre. Utile pour un point d'apparition.</summary>
    public GridPosition? FindWalkableIn(MapZone zone)
    {
        var center = zone.Center;
        if (IsWalkable(center)) return center;
        return zone.Positions().Where(IsWalkable).Cast<GridPosition?>().FirstOrDefault();
    }

    private int Index(GridPosition p) => (p.X - Origin.X) + (p.Y - Origin.Y) * Width;
}

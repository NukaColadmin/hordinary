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

    /// <summary>
    /// Cellules dont l'ouverture par la force est impossible : rideau metallique de la quincaillerie,
    /// fenetre a barreaux du poste de police (livrable 11).
    ///
    /// C'est un ensemble creux et non un tableau par cellule : une carte en compte une poignee, et
    /// une carte qui n'en compte aucune ne paie rien. Le blindage n'est pas un type de cellule parce
    /// qu'il ne change ni le passage ni la vue — il retire une action, ce qui est autre chose.
    /// </summary>
    private readonly HashSet<GridPosition> _unbreachable = [];

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

    /// <summary>
    /// Emis quand une cellule change de terrain, et seulement alors : une porte qu'on ouvre, une
    /// vitre qu'on brise. C'est ce qui permet au decor de rester d'accord avec le monde sans le
    /// relire case par case a chaque image — le peintre repeint la case annoncee, et elle seule.
    ///
    /// La grille n'en tire rien elle-meme : comme <c>WorldState</c> pour les objets, elle annonce.
    /// </summary>
    public event Action<GridPosition, CellType>? CellChanged;

    public void SetCell(GridPosition p, CellType type)
    {
        if (!Contains(p)) return;

        int index = Index(p);
        if (_cells[index] == type) return;

        _cells[index] = type;
        CellChanged?.Invoke(p, type);
    }

    public bool IsIndoor(GridPosition p) => Contains(p) && _indoor[Index(p)];

    public void SetIndoor(GridPosition p, bool indoor)
    {
        if (Contains(p)) _indoor[Index(p)] = indoor;
    }

    /// <summary>
    /// Vrai si cette cellule refuse d'etre ouverte par la force : ni <c>ForceDoor</c>, ni
    /// <c>BreakWindow</c>. Une porte blindee reste ouvrable si elle n'est pas verrouillee — c'est le
    /// passage en force qu'elle interdit, pas le passage.
    /// </summary>
    public bool IsUnbreachable(GridPosition p) => _unbreachable.Contains(p);

    public void SetUnbreachable(GridPosition p, bool unbreachable = true)
    {
        if (!Contains(p)) return;

        if (unbreachable) _unbreachable.Add(p);
        else _unbreachable.Remove(p);
    }

    /// <summary>Les cellules qu'on n'ouvre pas en force, pour un debrief ou un test.</summary>
    public IReadOnlyCollection<GridPosition> Unbreachable => _unbreachable;

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

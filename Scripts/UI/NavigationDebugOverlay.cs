using Godot;
using Hordinary.AI.Navigation;
using Hordinary.World;

// Godot expose un noeud 3D nomme GridMap : on desambigue en faveur de notre grille de jeu.
using GridMap = Hordinary.World.GridMap;

namespace Hordinary.UI;

/// <summary>
/// Affichage debug de la navigation (specification section 20.3) : chemin A*, noeuds explores,
/// cellules dangereuses et lieux nommes. Bascule avec F1.
/// </summary>
public partial class NavigationDebugOverlay : Node2D
{
    private static readonly Color PathColor = new(1f, 0.85f, 0.25f);
    private static readonly Color ExploredColor = new(0.35f, 0.55f, 1f, 0.16f);
    private static readonly Color GoalColor = new(0.30f, 1f, 0.55f);
    private static readonly Color SuspectedColor = new(1f, 0.63f, 0.10f, 0.35f);
    private static readonly Color ZombieColor = new(1f, 0.20f, 0.25f, 0.40f);
    private static readonly Color ZoneColor = new(0.65f, 0.85f, 1f, 0.75f);
    private static readonly Color FrontierColor = new(0.55f, 1f, 0.90f);
    private static readonly Color ForbiddenColor = new(1f, 0.35f, 0.85f);

    private int _tileSize = 16;
    private GridMap? _map;
    private NavigationGrid? _nav;
    private PathResult? _path;
    private GridPosition? _goal;

    /// <summary>Noeuds explores par la derniere recherche, reconstruits pour l'affichage.</summary>
    private IReadOnlyCollection<GridPosition> _explored = [];

    /// <summary>
    /// La carte de chaleur du survivant : ou il se souvient d'avoir vu ou entendu quelque chose. Le
    /// souvenir s'efface, donc l'affichage palit tout seul — c'est la lecture la plus directe de
    /// « le niveau de menace decroit avec le temps ».
    /// </summary>
    private Func<IEnumerable<(GridPosition Cell, float Value)>>? _remembered;

    /// <summary>
    /// Les frontieres d'exploration du survivant affiche, de la mieux notee a la moins bonne. La
    /// specification les exige au mode debug : ce sont elles qui expliquent pourquoi trois survivants
    /// partent dans trois directions au lieu de se suivre.
    /// </summary>
    private Func<IEnumerable<(GridPosition Cell, bool Chosen)>>? _frontiers;

    /// <summary>Les zones que le joueur a mises hors limites (livrable 8).</summary>
    private Func<IEnumerable<MapZone>>? _forbidden;

    public bool ShowExploredNodes { get; set; } = true;
    public bool ShowZones { get; set; } = true;
    public bool ShowRememberedDanger { get; set; } = true;
    public bool ShowFrontiers { get; set; } = true;

    /// <summary>Branche la carte de chaleur d'un agent, sans que l'overlay ait a connaitre l'agent.</summary>
    public void SetDangerMemory(Func<IEnumerable<(GridPosition Cell, float Value)>> remembered)
        => _remembered = remembered;

    public void SetFrontiers(Func<IEnumerable<(GridPosition Cell, bool Chosen)>> frontiers)
        => _frontiers = frontiers;

    public void SetForbiddenZones(Func<IEnumerable<MapZone>> forbidden) => _forbidden = forbidden;

    public void Configure(GridMap map, NavigationGrid nav, int tileSize)
    {
        _map = map;
        _nav = nav;
        _tileSize = tileSize;
        QueueRedraw();
    }

    public void SetPath(PathResult? path, GridPosition? goal, IReadOnlyCollection<GridPosition>? explored = null)
    {
        _path = path;
        _goal = goal;
        _explored = explored ?? [];
        QueueRedraw();
    }

    public void Refresh() => QueueRedraw();

    public override void _Draw()
    {
        if (_map is null || _nav is null) return;

        if (ShowExploredNodes) DrawExplored();
        if (ShowRememberedDanger) DrawRememberedDanger();
        DrawDangerCells();
        DrawForbiddenZones();
        if (ShowZones) DrawZones();
        if (ShowFrontiers) DrawFrontiers();
        DrawPath();
        DrawGoal();
    }

    /// <summary>
    /// Les frontieres candidates, celle retenue en plein. Une frontiere est le bord du connu : c'est
    /// la que le survivant ira si rien de plus urgent ne se presente.
    /// </summary>
    private void DrawFrontiers()
    {
        if (_frontiers is null) return;

        foreach (var (cell, chosen) in _frontiers())
        {
            var rect = CellRect(cell);

            if (chosen)
            {
                DrawRect(rect, FrontierColor with { A = 0.45f });
                DrawRect(rect, FrontierColor, filled: false, width: 1.4f);
            }
            else
            {
                DrawRect(rect, FrontierColor with { A = 0.18f }, filled: false, width: 1f);
            }
        }
    }

    /// <summary>
    /// Les zones interdites par le joueur. Hachurees en croix, pour qu'on ne les confonde pas avec
    /// une zone dangereuse : le danger est un cout, l'interdiction est un mur.
    /// </summary>
    private void DrawForbiddenZones()
    {
        if (_forbidden is null) return;

        foreach (var zone in _forbidden())
        {
            var rect = new Rect2(
                zone.Min.X * _tileSize,
                zone.Min.Y * _tileSize,
                zone.Width * _tileSize,
                zone.Height * _tileSize);

            DrawRect(rect, ForbiddenColor with { A = 0.22f });
            DrawRect(rect, ForbiddenColor, filled: false, width: 1.5f);

            DrawLine(rect.Position, rect.Position + rect.Size, ForbiddenColor, 1.2f);
            DrawLine(
                rect.Position + new Vector2(rect.Size.X, 0f),
                rect.Position + new Vector2(0f, rect.Size.Y),
                ForbiddenColor,
                1.2f);
        }
    }

    private void DrawExplored()
    {
        foreach (var cell in _explored)
        {
            DrawRect(CellRect(cell), ExploredColor);
        }
    }

    /// <summary>
    /// Le souvenir du danger, en degrade : plus la case a ete marquee fort et recemment, plus elle
    /// est rouge. Une case qui palit sous les yeux du joueur, c'est un survivant qui oublie.
    /// </summary>
    private void DrawRememberedDanger()
    {
        if (_remembered is null) return;

        foreach (var (cell, value) in _remembered())
        {
            if (value <= 0f) continue;
            DrawRect(CellRect(cell), ZombieColor with { A = 0.45f * Math.Clamp(value, 0f, 1f) });
        }
    }

    private void DrawDangerCells()
    {
        foreach (var (cell, level) in _nav!.DangerCells)
        {
            var color = level == DangerLevel.ZombieNearby ? ZombieColor : SuspectedColor;
            DrawRect(CellRect(cell), color);
            DrawRect(CellRect(cell), color with { A = 0.9f }, filled: false, width: 1f);
        }
    }

    private void DrawZones()
    {
        var font = ThemeDB.FallbackFont;

        foreach (var zone in _map!.Zones)
        {
            var rect = new Rect2(
                zone.Min.X * _tileSize,
                zone.Min.Y * _tileSize,
                zone.Width * _tileSize,
                zone.Height * _tileSize);

            var color = zone.Kind switch
            {
                ZoneKind.Start => new Color(0.45f, 1f, 0.65f, 0.85f),
                ZoneKind.Pharmacy => new Color(1f, 0.45f, 0.65f, 0.85f),
                ZoneKind.Extraction => new Color(0.45f, 0.85f, 1f, 0.85f),
                _ => ZoneColor with { A = 0.35f },
            };

            DrawRect(rect, color, filled: false, width: 1f);
            DrawString(font, rect.Position + new Vector2(2f, 9f), zone.Name,
                HorizontalAlignment.Left, -1, 7, color);
        }
    }

    private void DrawPath()
    {
        if (_path is not { Found: true } path || path.Cells.Count < 2) return;

        var points = new Vector2[path.Cells.Count];
        for (int i = 0; i < path.Cells.Count; i++) points[i] = CellCenter(path.Cells[i]);

        DrawPolyline(points, new Color(0f, 0f, 0f, 0.6f), 3.5f);
        DrawPolyline(points, PathColor, 1.8f);

        foreach (var point in points) DrawCircle(point, 1.6f, PathColor);
    }

    private void DrawGoal()
    {
        if (_goal is not { } goal) return;

        var rect = CellRect(goal);
        DrawRect(rect, GoalColor with { A = 0.22f });
        DrawRect(rect, GoalColor, filled: false, width: 1.2f);
    }

    private Rect2 CellRect(GridPosition cell)
        => new(cell.X * _tileSize, cell.Y * _tileSize, _tileSize, _tileSize);

    private Vector2 CellCenter(GridPosition cell)
        => new(cell.X * _tileSize + _tileSize * 0.5f, cell.Y * _tileSize + _tileSize * 0.5f);
}

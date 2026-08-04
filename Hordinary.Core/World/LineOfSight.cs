namespace Hordinary.World;

/// <summary>
/// Ligne de vue discrete entre deux cellules (trace de Bresenham). La vision est arretee par les
/// murs, les portes fermees et les obstacles opaques (specification section 9.2).
/// </summary>
public static class LineOfSight
{
    /// <summary>
    /// Cellules traversees en allant de <paramref name="from"/> (exclue) a <paramref name="to"/>
    /// (incluse). Une origine egale a la destination ne traverse rien.
    /// </summary>
    public static IEnumerable<GridPosition> Trace(GridPosition from, GridPosition to)
    {
        if (from == to) yield break;

        int x = from.X, y = from.Y;
        int dx = Math.Abs(to.X - x), dy = -Math.Abs(to.Y - y);
        int stepX = x < to.X ? 1 : -1, stepY = y < to.Y ? 1 : -1;
        int error = dx + dy;

        while (true)
        {
            int doubled = 2 * error;
            if (doubled >= dy) { error += dy; x += stepX; }
            if (doubled <= dx) { error += dx; y += stepY; }

            yield return new GridPosition(x, y);
            if (x == to.X && y == to.Y) yield break;
        }
    }

    /// <summary>
    /// Vrai si <paramref name="to"/> est visible depuis <paramref name="from"/>. La cellule
    /// bloquante est elle-meme visible — on voit le mur qui cache la suite — mais rien derriere.
    /// </summary>
    public static bool IsVisible(GridMap map, GridPosition from, GridPosition to)
    {
        foreach (var cell in Trace(from, to))
        {
            if (cell == to) return true;
            if (map.GetCell(cell).BlocksSight()) return false;
        }

        return true;
    }
}

namespace Hordinary.World;

/// <summary>Categories de lieux exigees par la specification (section 7.3).</summary>
public enum ZoneKind
{
    Other = 0,
    Start,
    Pharmacy,
    Extraction,
    Building,
    Street,
    Alley,
}

/// <summary>
/// Lieu nomme de la carte, decrit par un rectangle inclusif en coordonnees de tuiles.
/// Les zones sont declarees dans <c>config/map_locations.json</c> et non codees en dur.
/// </summary>
public sealed class MapZone(string name, ZoneKind kind, GridPosition min, GridPosition max)
{
    public string Name { get; } = name;
    public ZoneKind Kind { get; } = kind;
    public GridPosition Min { get; } = new(Math.Min(min.X, max.X), Math.Min(min.Y, max.Y));
    public GridPosition Max { get; } = new(Math.Max(min.X, max.X), Math.Max(min.Y, max.Y));

    public int Width => Max.X - Min.X + 1;
    public int Height => Max.Y - Min.Y + 1;
    public GridPosition Center => new((Min.X + Max.X) / 2, (Min.Y + Max.Y) / 2);

    public bool Contains(GridPosition p)
        => p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y;

    public IEnumerable<GridPosition> Positions()
    {
        for (int y = Min.Y; y <= Max.Y; y++)
        {
            for (int x = Min.X; x <= Max.X; x++)
            {
                yield return new GridPosition(x, y);
            }
        }
    }

    public override string ToString() => $"{Name} [{Kind}] {Min}-{Max}";
}

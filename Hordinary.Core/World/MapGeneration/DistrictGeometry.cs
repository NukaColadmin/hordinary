namespace Hordinary.World.MapGeneration;

/// <summary>Les quatre bords du quartier. Un seul porte la route d'arrivee, et le van.</summary>
public enum Side
{
    North = 0,
    East,
    South,
    West,
}

/// <summary>
/// Rectangle inclusif en coordonnees de tuiles. C'est <see cref="MapZone"/> sans le nom ni la
/// categorie : la generation manipule des centaines de rectangles anonymes — ilots, rues, pieces —
/// et seuls quelques-uns deviendront des lieux.
/// </summary>
public readonly record struct Rect(GridPosition Min, GridPosition Max)
{
    public static Rect FromBounds(int minX, int minY, int maxX, int maxY)
        => new(new GridPosition(minX, minY), new GridPosition(maxX, maxY));

    public int Width => Max.X - Min.X + 1;
    public int Height => Max.Y - Min.Y + 1;
    public int Area => Width * Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public GridPosition Center => new((Min.X + Max.X) / 2, (Min.Y + Max.Y) / 2);

    public bool Contains(GridPosition p)
        => p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y;

    /// <summary>Retrait uniforme de <paramref name="margin"/> cases sur les quatre cotes.</summary>
    public Rect Shrink(int margin)
        => FromBounds(Min.X + margin, Min.Y + margin, Max.X - margin, Max.Y - margin);

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

    /// <summary>Le pourtour sans les coins : les seules cases ou une porte a un sens.</summary>
    public IEnumerable<GridPosition> Perimeter()
    {
        for (int x = Min.X + 1; x < Max.X; x++)
        {
            yield return new GridPosition(x, Min.Y);
            yield return new GridPosition(x, Max.Y);
        }

        for (int y = Min.Y + 1; y < Max.Y; y++)
        {
            yield return new GridPosition(Min.X, y);
            yield return new GridPosition(Max.X, y);
        }
    }

    public IEnumerable<GridPosition> Interior()
    {
        for (int y = Min.Y + 1; y < Max.Y; y++)
        {
            for (int x = Min.X + 1; x < Max.X; x++)
            {
                yield return new GridPosition(x, y);
            }
        }
    }

    public MapZone ToZone(string name, ZoneKind kind, string? profile = null)
        => new(name, kind, Min, Max, profile);

    public override string ToString() => $"{Min}-{Max} ({Width} x {Height})";
}

/// <summary>Ce qu'une voie est : ce qui la distingue n'est pas sa forme mais son role.</summary>
public enum RoadKind
{
    /// <summary>La traversante. Elle porte le van et rejoint deux bords opposes.</summary>
    Main = 0,

    /// <summary>Une rue secondaire : ce que le decoupage en ilots a laisse entre eux.</summary>
    Street,

    /// <summary>Une ruelle d'une case, fendue dans un ilot.</summary>
    Alley,
}

/// <summary>Une voie, telle que la passe 2 la pose : un rectangle et un role.</summary>
public sealed record RoadStrip(Rect Bounds, RoadKind Kind, bool Vertical);

/// <summary>
/// Un batiment tel que la passe 3 le laisse : son empreinte, ses acces, et ses pieces.
///
/// Les acces sont conserves separement des murs parce que le contrat de jouabilite compte les
/// <b>acces distincts</b> d'un batiment : sans cette liste il faudrait les redecouvrir en relisant
/// la grille, c'est-a-dire recalculer ce que la passe savait deja.
/// </summary>
public sealed class Building(Rect bounds)
{
    public Rect Bounds { get; } = bounds;

    /// <summary>Portes d'entree percees dans le mur d'enceinte, verrouillees ou non.</summary>
    public List<GridPosition> Doors { get; } = [];

    /// <summary>Fenetres de facade. Une fenetre compte comme un acces : elle se brise.</summary>
    public List<GridPosition> Windows { get; } = [];

    /// <summary>Les pieces obtenues par decoupage binaire, empreinte comprise.</summary>
    public List<Rect> Rooms { get; } = [];

    /// <summary>Nom donne par la passe 4, quand la zone a ete enregistree.</summary>
    public MapZone? Zone { get; set; }

    /// <summary>Le nombre de facons distinctes d'entrer. Deux au moins pour porter l'objectif.</summary>
    public int Accesses => Doors.Count + Windows.Count;

    public override string ToString()
        => $"{Bounds} — {Doors.Count} porte(s), {Windows.Count} fenetre(s), {Rooms.Count} piece(s)";
}

/// <summary>
/// Un objet pose au sol par la generation. <c>ItemId</c> est un identifiant du catalogue : le
/// quartier ne nomme aucun objet, il designe une entree de <c>config/items.json</c>.
/// </summary>
public readonly record struct DistrictItem(string ItemId, GridPosition Position);

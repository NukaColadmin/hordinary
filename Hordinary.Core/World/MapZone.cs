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
public sealed class MapZone(string name, ZoneKind kind, GridPosition min, GridPosition max, string? profile = null)
{
    public string Name { get; } = name;
    public ZoneKind Kind { get; } = kind;

    /// <summary>
    /// Profil de <c>config/zones.json</c>, quand la zone en declare un : ce qu'elle <b>contient</b> et
    /// comment elle est agencee, par-dessus ce qu'elle <b>est</b>.
    ///
    /// <see cref="Kind"/> reste la categorie structurelle, celle que la navigation et le planificateur
    /// connaissent ; le profil est une chaine, et une chaine ne se recompile pas. La carte de
    /// demonstration n'en declare aucun et reste jouable : le champ est facultatif par construction.
    /// </summary>
    public string? Profile { get; } = string.IsNullOrWhiteSpace(profile) ? null : profile;
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

    public override string ToString()
        => $"{Name} [{Kind}{(Profile is { } profile ? $"/{profile}" : string.Empty)}] {Min}-{Max}";
}

/// <summary>
/// Ce que la categorie d'une zone dit de son nom.
///
/// La pharmacie, le camp et l'extraction sont uniques sur une carte : leur categorie suffit a les
/// designer, et le planificateur les connait sous ce nom-la. Un batiment, une rue ou une ruelle ne
/// se distinguent que par leur nom propre — « allez fouiller le batiment » ne designe rien quand il
/// y en a deux. C'est toute la difference, et elle vaut a la fois pour la memoire d'un survivant et
/// pour le vocabulaire du domaine de planification.
/// </summary>
public static class ZoneKinds
{
    public static bool IsCommon(this ZoneKind kind)
        => kind is ZoneKind.Building or ZoneKind.Street or ZoneKind.Alley;

    /// <summary>
    /// Vrai pour les lieux qu'aucune ouverture ne ferme. Une rue n'a pas de porte : ce qu'on y voit
    /// de portes garde les batiments qui la bordent, jamais elle.
    /// </summary>
    public static bool IsOpenAir(this ZoneKind kind)
        => kind is ZoneKind.Street or ZoneKind.Alley;
}

namespace Hordinary.AI.Planning;

/// <summary>
/// Ce qui change le prix d'une action pour un survivant donne, sans changer ce que l'action fait.
///
/// Trois coefficients et un interrupteur : le cout que GBFS minimise, le bruit produit, la duree
/// d'execution, et le droit de planifier l'action du tout. Les preconditions et les effets, eux, ne
/// bougent jamais — une porte forcee est une porte forcee, que ce soit avec un pied-de-biche ou a
/// l'epaule. Seul le prix differe, et c'est ce prix que la recherche compare.
///
/// C'est <b>l'equipement</b> qui les produit, et lui seul : une caisse a outils qui divise par deux
/// le cout de <c>ForceDoor</c> (voir <c>config/items.json</c>). Les survivants, eux, pratiquent tous
/// le meme domaine au meme prix — ce qui les distingue est ce que le joueur leur dit, pas une fiche
/// de personnage.
/// </summary>
public sealed class ActionModifier
{
    /// <summary>Multiplie le cout de l'action. Sous 1, le planificateur la choisit plus volontiers.</summary>
    public float Cost { get; init; } = 1f;

    /// <summary>Multiplie le bruit produit.</summary>
    public float Noise { get; init; } = 1f;

    /// <summary>Multiplie la duree d'execution.</summary>
    public float Duration { get; init; } = 1f;

    /// <summary>A <c>false</c>, l'action sort du domaine : elle ne peut pas etre planifiee.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Ni aide ni penalite : le prix du domaine commun.</summary>
    public static ActionModifier Neutral { get; } = new();

    public override string ToString()
        => $"cout x{Cost:0.##}, bruit x{Noise:0.##}, duree x{Duration:0.##}{(Enabled ? string.Empty : ", desactivee")}";
}

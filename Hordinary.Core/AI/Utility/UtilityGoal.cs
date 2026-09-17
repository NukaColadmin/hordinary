namespace Hordinary.AI.Utility;

/// <summary>Facon d'agreger les considerations d'un objectif.</summary>
public enum CombineMode
{
    /// <summary>Produit : une seule consideration a 0 annule l'objectif. Mode par defaut.</summary>
    Multiply = 0,

    /// <summary>Moyenne ponderee : les considerations se compensent.</summary>
    Average,

    /// <summary>Le maillon faible.</summary>
    Min,

    /// <summary>La meilleure raison suffit.</summary>
    Max,
}

/// <summary>
/// Condition d'existence d'un objectif : l'entree doit tomber dans <c>[Min, Max]</c>. Un objectif
/// dont une precondition echoue n'est pas score, il est ecarte — inutile de calculer la valeur de
/// « ramasser les medicaments » quand on les a deja.
/// </summary>
public sealed class GoalPrecondition
{
    private const float Tolerance = 1e-4f;

    public string Input { get; init; } = string.Empty;
    public float Min { get; init; } = 0f;
    public float Max { get; init; } = 1f;

    public bool IsSatisfiedBy(float value) => value >= Min - Tolerance && value <= Max + Tolerance;

    public override string ToString() => $"{Input} dans [{Min:0.##} ; {Max:0.##}]";
}

/// <summary>
/// Un objectif de la Utility AI (specification section 11.2) : un nom, une cible facultative, des
/// preconditions et des considerations ponderees.
///
/// Il n'existe volontairement aucune classe ni methode par objectif : <c>FleeDanger</c> et
/// <c>Hide</c> sont deux instances de ce meme type, decrites dans
/// <c>config/utility_goals.json</c>. Ajouter, retirer ou repondérer un objectif ne demande pas de
/// recompilation.
/// </summary>
public sealed class UtilityGoal
{
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Ou va le survivant quand cet objectif l'emporte, exprime dans le vocabulaire de
    /// <see cref="UtilityContext.Locate"/> (<c>extraction</c>, <c>away_from_danger</c>, <c>ally</c>…).
    /// <c>null</c> signifie « sur place ». C'est un raccourci en attendant le planificateur du
    /// livrable 5, qui transformera l'objectif en suite d'actions.
    /// </summary>
    public string? Target { get; init; }

    public List<GoalPrecondition> Preconditions { get; init; } = [];

    public List<Consideration> Considerations { get; init; } = [];

    public CombineMode Combine { get; init; } = CombineMode.Multiply;

    /// <summary>Poids global de l'objectif, applique au score agrege. Sert a arbitrer sans toucher aux considerations.</summary>
    public float Weight { get; init; } = 1f;

    /// <summary>Passer a <c>false</c> retire l'objectif du jeu — utile pour isoler un comportement en test.</summary>
    public bool Enabled { get; init; } = true;

    public override string ToString() => $"{Name} ({Considerations.Count} considerations)";
}

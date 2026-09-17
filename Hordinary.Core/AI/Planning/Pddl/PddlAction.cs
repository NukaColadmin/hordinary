namespace Hordinary.AI.Planning.Pddl;

/// <summary>Un parametre type : <c>?location - location</c>.</summary>
public readonly record struct PddlParameter(string Name, string Type)
{
    /// <summary>Le nom sans son <c>?</c>, tel que le domaine JSON le declare.</summary>
    public string Variable => Name.StartsWith('?') ? Name[1..] : Name;

    public override string ToString() => $"{Name} - {Type}";
}

/// <summary>
/// Un predicat declare : son nom et le type de chacun de ses arguments. L'arite et les types sont
/// deduits mecaniquement des preconditions et des effets de toutes les actions du domaine (spec G) —
/// il n'existe nulle part une liste de predicats tenue a la main, et c'est ce qui fait que la
/// grammaire du livrable 15 ne peut pas prendre du retard sur le domaine.
/// </summary>
public sealed record PddlPredicate(string Name, IReadOnlyList<string> ParameterTypes)
{
    public int Arity => ParameterTypes.Count;

    /// <summary>Declaration PDDL : <c>(at ?arg0 - object ?arg1 - location)</c>.</summary>
    public string Canonical
        => ParameterTypes.Count == 0
            ? $"({Name})"
            : $"({Name} {string.Join(' ', ParameterTypes.Select((type, index) => $"?arg{index} - {type}"))})";

    public override string ToString() => Canonical;
}

/// <summary>
/// Un schema d'action PDDL : des parametres types, une precondition, un effet, un cout.
///
/// <para>
/// <see cref="Origin"/> est la couche execution — verbe, parametres de verbe, duree, bruit. PDDL ne
/// la decrit pas et <see cref="PddlWriter"/> ne l'ecrit jamais : elle voyage ici parce que le second
/// rendu de la spec G, celui qui alimente GBFS, en a besoin pour rendre une action <b>jouable</b>.
/// Sans elle, un domaine compile par le livrable 15 serait un joli PDDL que le jeu ne saurait pas
/// executer, et il faudrait deux compilateurs a maintenir d'accord — exactement ce que la
/// representation intermediaire existe pour eviter.
/// </para>
/// </summary>
public sealed class PddlAction
{
    public PddlAction(
        string name,
        IReadOnlyList<PddlParameter> parameters,
        PddlFormula precondition,
        PddlFormula effect,
        float cost,
        ActionSchema? origin = null,
        string? generatedBy = null)
    {
        Name = name;
        Parameters = parameters;
        Precondition = precondition;
        Effect = effect;
        Cost = cost;
        Origin = origin;
        GeneratedBy = generatedBy;
    }

    public string Name { get; }

    public IReadOnlyList<PddlParameter> Parameters { get; }

    public PddlFormula Precondition { get; }

    /// <summary>
    /// Les effets logiques seuls. Le cout n'y figure pas : il vit dans <see cref="Cost"/>, et
    /// <see cref="PddlWriter"/> le rend en <c>(increase (total-cost) …)</c> au moment d'ecrire. Une
    /// seule source pour une seule valeur — <c>penalize(A, k)</c> multiplie un nombre au lieu de
    /// retrouver un litteral au milieu d'une conjonction.
    /// </summary>
    public PddlFormula Effect { get; }

    public float Cost { get; }

    /// <summary>Le schema JSON dont l'action est issue, si elle en a un. Jamais ecrit en PDDL.</summary>
    public ActionSchema? Origin { get; }

    /// <summary>La contrainte du joueur qui a produit ce schema, quand il est ne d'une scission.</summary>
    public string? GeneratedBy { get; }

    public PddlAction With(
        string? name = null,
        PddlFormula? precondition = null,
        PddlFormula? effect = null,
        float? cost = null,
        string? generatedBy = null)
        => new(
            name ?? Name,
            Parameters,
            precondition ?? Precondition,
            effect ?? Effect,
            cost ?? Cost,
            Origin,
            generatedBy ?? GeneratedBy);

    /// <summary>Le parametre de ce type, s'il y en a exactement un — la question que pose une classe de schemas.</summary>
    public PddlParameter? SoleParameterOfType(string type)
    {
        var matching = Parameters.Where(parameter => parameter.Type.Equals(type, StringComparison.OrdinalIgnoreCase)).ToList();
        return matching.Count == 1 ? matching[0] : null;
    }

    public override string ToString() => $"{Name}({string.Join(", ", Parameters)})";
}

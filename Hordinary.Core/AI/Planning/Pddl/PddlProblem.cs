namespace Hordinary.AI.Planning.Pddl;

/// <summary>
/// Un probleme PDDL : les objets, l'etat initial, le but et la metrique (spec G).
///
/// <para>
/// C'est la moitie mobile de l'export. Le domaine ne change pas d'une partie a l'autre — il est
/// versionne dans <c>tools/pddl/domain.pddl</c> — alors qu'un probleme est un <b>instantane</b> : ce
/// qu'un survivant donne croyait a un instant donne. Deux survivants au meme endroit produisent deux
/// problemes differents, et c'est la propriete qui compte : l'observabilite partielle survit a
/// l'export.
/// </para>
///
/// <para>
/// Aucun planificateur tiers ne tourne pendant une partie. Ce type existe pour le portillon 3, hors
/// ligne, et pour les instantanes versionnes.
/// </para>
/// </summary>
public sealed record PddlProblem
{
    public required string Name { get; init; }

    /// <summary>Le domaine dont ce probleme est une instance : <c>(:domain hordinary)</c>.</summary>
    public required string Domain { get; init; }

    /// <summary>Les objets du probleme — jamais ceux que le domaine declare deja en constantes.</summary>
    public IReadOnlyList<PddlParameter> Objects { get; init; } = [];

    /// <summary>Les faits vrais au depart, et les valeurs des fonctions.</summary>
    public IReadOnlyList<PddlFormula> Init { get; init; } = [];

    public PddlFormula Goal { get; init; } = new PddlConjunction([]);

    /// <summary>Ce que le planificateur minimise. C'est ce qui donne un sens a <c>penalize</c>.</summary>
    public string Metric { get; init; } = $"(:metric minimize ({PddlIncrease.TotalCost}))";

    /// <summary>D'ou vient l'instantane, en commentaire de tete. Un fichier doit pouvoir s'ouvrir seul.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    public override string ToString()
        => $"{Name} — {Objects.Count} objets, {Init.Count} faits initiaux";
}

/// <summary>
/// Traduit un <see cref="PlanningProblem"/> — celui que GBFS resout reellement — en
/// <see cref="PddlProblem"/>.
///
/// <para>
/// Le point qui compte : <b>rien n'est ajoute</b>. Les objets sont ceux que
/// <see cref="SymbolicStateBuilder"/> a derives de la memoire de l'agent, les faits sont son etat
/// initial tel quel. Un lieu jamais percu n'a pas d'objet, donc pas de fait, donc aucun plan ne peut
/// s'appuyer dessus — ni sous GBFS, ni sous Fast Downward. C'est ce qui rend la corroboration
/// honnete : le planificateur tiers ne voit pas plus que le survivant.
/// </para>
/// </summary>
public static class PddlProblemBuilder
{
    public static PddlProblem From(
        PlanningProblem problem,
        PddlDomain domain,
        string? name = null,
        IEnumerable<string>? notes = null)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(domain);

        var constants = new HashSet<string>(
            domain.Constants.Select(constant => constant.Name), StringComparer.OrdinalIgnoreCase);

        var objects = problem.Objects
            .Where(candidate => !constants.Contains(candidate.Name))
            .OrderBy(candidate => candidate.Type, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
            .Select(candidate => new PddlParameter(candidate.Name, candidate.Type))
            .ToList();

        return new PddlProblem
        {
            Name = name ?? Named(problem),
            Domain = domain.Name,
            Objects = objects,
            Init = Init(problem),
            Goal = Goal(problem),
            Notes = [.. notes ?? Describe(problem)],
        };
    }

    /// <summary>
    /// L'etat initial : les faits de l'agent, et le compteur de cout a zero. Rien d'autre.
    ///
    /// <para>
    /// Il y avait ici une table des distances entre lieux — un <c>(= (distance a b) n)</c> par paire,
    /// soit les deux tiers de chaque instantane. Elle est partie : aucune action du domaine ne lit
    /// cette fonction, et aucune ne le peut, faute d'un parametre d'origine sur
    /// <c>MoveToLocation</c> (voir <see cref="PddlDomainBuilder"/>). Une valeur que personne ne
    /// consulte ne rend pas le cout exporte plus juste ; elle le fait seulement paraitre tel.
    /// </para>
    /// </summary>
    private static List<PddlFormula> Init(PlanningProblem problem)
    {
        var init = new List<PddlFormula>();

        foreach (string fact in problem.Initial.Facts.OrderBy(fact => fact, StringComparer.Ordinal))
            init.Add(PddlAtom.Parse(fact));

        init.Add(new PddlAssign(PddlIncrease.TotalCost, 0f));

        return init;
    }

    /// <summary>
    /// Le but, tel que <c>config/planner_domain.json</c> le decrit pour l'objectif courant. Une
    /// condition attendue fausse devient une negation : le vocabulaire est le meme des deux cotes.
    /// </summary>
    private static PddlFormula Goal(PlanningProblem problem)
        => new PddlConjunction(problem.Goal.Select(condition => condition.Expected
            ? PddlAtom.Parse(condition.Pattern)
            : (PddlFormula)new PddlNegation(PddlAtom.Parse(condition.Pattern))));

    private static string Named(PlanningProblem problem)
        => SymbolicStateBuilder.ObjectNameOf(problem.GoalName);

    private static IEnumerable<string> Describe(PlanningProblem problem)
    {
        yield return $"Instantane : objectif « {problem.GoalName} », agent en {problem.AgentPosition}.";
        yield return $"{problem.Objects.Count} objets connus, {problem.Initial.Facts.Count} faits, "
                   + $"{problem.Actions.Count} actions instanciees.";
        yield return "Ce que l'agent n'a jamais percu n'y figure pas : c'est voulu.";
    }
}

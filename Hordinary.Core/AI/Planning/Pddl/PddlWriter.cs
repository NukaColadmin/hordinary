using System.Text;

namespace Hordinary.AI.Planning.Pddl;

/// <summary>
/// Rend un <see cref="PddlDomain"/> en texte PDDL. Le rendu ne decide de rien : tout ce qu'il ecrit
/// existe deja dans la structure, et c'est ce qui rend verifiable la propriete « le texte rendu
/// correspond a la representation ».
/// </summary>
public static class PddlWriter
{
    public static string Render(PddlDomain domain, bool includeConstraints = false)
    {
        ArgumentNullException.ThrowIfNull(domain);

        var text = new StringBuilder();
        text.Append("(define (domain ").Append(domain.Name).AppendLine(")");

        // `:constraints` n'est ajoute que si le bloc PDDL 3 est rendu, et il ne l'est jamais pour un
        // planificateur : le domaine donne a Fast Downward garde donc exactement les exigences de la
        // spec G. VAL, lui, accepte le bloc sans le drapeau, mais un fichier qui existe pour etre lu
        // par des outils tiers n'a pas a parier sur leur indulgence.
        var requirements = includeConstraints && domain.Constraints.Count > 0
            ? domain.Requirements.Append(":constraints")
            : domain.Requirements;

        text.Append("  (:requirements ").Append(string.Join(' ', requirements)).AppendLine(")");
        text.Append("  (:types ").Append(string.Join(' ', domain.Types)).AppendLine(" - object)");

        if (domain.Constants.Count > 0)
        {
            text.Append("  (:constants ").Append(string.Join("  ", GroupByType(domain.Constants))).AppendLine(")");
        }

        text.AppendLine("  (:predicates");
        foreach (var predicate in domain.Predicates) text.Append("    ").AppendLine(predicate.Canonical);
        text.AppendLine("  )");

        if (domain.Functions.Count > 0)
        {
            text.AppendLine("  (:functions");
            foreach (var function in domain.Functions) text.Append("    ").AppendLine(function.Canonical);
            text.AppendLine("  )");
        }

        // Le bloc PDDL 3 n'est rendu que sur demande, et jamais pour un planificateur : c'est la forme
        // declarative des contraintes du joueur, celle que le portillon 4 et VAL relisent.
        if (includeConstraints && domain.Constraints.Count > 0)
        {
            text.AppendLine("  (:constraints (and");
            foreach (var constraint in domain.Constraints) text.Append("    ").AppendLine(constraint.Canonical);
            text.AppendLine("  ))");
        }

        int scale = CostScale(domain.Actions);

        if (scale != 1)
        {
            text.Append("  ; Couts multiplies par ").Append(scale)
                .AppendLine(" : PDDL n'admet que des entiers. L'ordre des plans est inchange.");
        }

        foreach (var action in domain.Actions) Render(text, action, scale);

        return text.AppendLine(")").ToString();
    }

    /// <summary>
    /// Rend un probleme. Meme regle que pour le domaine : rien n'est decide ici, tout existe deja
    /// dans la structure.
    /// </summary>
    public static string Render(PddlProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);

        var text = new StringBuilder();

        foreach (string note in problem.Notes) text.Append("; ").AppendLine(note);
        if (problem.Notes.Count > 0) text.AppendLine();

        text.Append("(define (problem ").Append(problem.Name).AppendLine(")");
        text.Append("  (:domain ").Append(problem.Domain).AppendLine(")");

        if (problem.Objects.Count > 0)
        {
            text.AppendLine("  (:objects");
            foreach (string line in GroupByType(problem.Objects)) text.Append("    ").AppendLine(line);
            text.AppendLine("  )");
        }

        text.AppendLine("  (:init");
        foreach (var fact in problem.Init) text.Append("    ").AppendLine(fact.Canonical);
        text.AppendLine("  )");

        text.Append("  (:goal ").Append(problem.Goal.Canonical).AppendLine(")");

        if (!string.IsNullOrWhiteSpace(problem.Metric)) text.Append("  ").AppendLine(problem.Metric);

        return text.AppendLine(")").ToString();
    }

    /// <summary>
    /// Le facteur par lequel multiplier les couts pour qu'ils deviennent tous entiers.
    ///
    /// <para>
    /// PDDL n'admet pas de cout fractionnaire, et Fast Downward le dit sans detour :
    /// <c>Fractional numbers are not supported. Got: 2.5</c>, sur <c>PushBackZombie</c>. Le domaine
    /// livre etait donc irrecevable pour cette seule action, et le defaut n'avait jamais ete vu parce
    /// que le binaire n'avait jamais ete lance.
    /// </para>
    ///
    /// <para>
    /// Multiplier plutot qu'arrondir n'est pas une coquetterie : un arrondi change l'ordre des plans
    /// en silence — 2,5 devenu 3 rend soudain <c>PushBackZombie</c> aussi cher qu'une action a 3 —
    /// alors qu'un facteur commun le laisse exactement ou il etait. Le cout absolu exporte n'a de
    /// toute facon pas de sens comparable a celui de GBFS, qui compte en plus la part de distance ;
    /// ce qui doit survivre a l'export, c'est l'<b>ordre</b>, et c'est ce que le facteur preserve.
    /// </para>
    ///
    /// <para>
    /// La structure, elle, garde ses flottants : <see cref="PlanningDomainRenderer"/> alimente GBFS
    /// avec les couts reels. La mise a l'echelle n'appartient qu'au texte, parce que la contrainte
    /// n'appartient qu'a PDDL.
    /// </para>
    /// </summary>
    private static int CostScale(IReadOnlyList<PddlAction> actions)
    {
        for (int scale = 1; scale <= MaxCostScale; scale++)
        {
            if (actions.All(action => IsWhole(action.Cost * scale))) return scale;
        }

        return MaxCostScale;
    }

    /// <summary>Au-dela, on renonce a etre exact et on arrondit : un cout a sept decimales n'est pas un cout.</summary>
    private const int MaxCostScale = 1000;

    private static bool IsWhole(float value) => MathF.Abs(value - MathF.Round(value)) < 1e-4f;

    private static void Render(StringBuilder text, PddlAction action, int scale)
    {
        text.AppendLine();

        if (action.GeneratedBy is { } origin) text.Append("  ; Genere par la contrainte ").AppendLine(origin);

        text.Append("  (:action ").AppendLine(action.Name);
        text.Append("    :parameters (").Append(string.Join(' ', action.Parameters.Select(parameter => parameter.ToString()))).AppendLine(")");
        text.Append("    :precondition ").AppendLine(action.Precondition.Canonical);

        // Le cout n'est pas un effet de la structure — il vit dans PddlAction.Cost — mais il en est
        // un en PDDL. C'est ici, et seulement ici, qu'il le redevient.
        var effect = action.Effect.Conjoin(
            new PddlIncrease(PddlIncrease.TotalCost, MathF.Round(action.Cost * scale)));
        text.Append("    :effect ").Append(effect.Canonical).AppendLine(")");
    }

    /// <summary>Regroupe les constantes par type : <c>objective remedy - item</c>.</summary>
    private static IEnumerable<string> GroupByType(IReadOnlyList<PddlParameter> constants)
        => constants
            .GroupBy(constant => constant.Type, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{string.Join(' ', group.Select(constant => constant.Name))} - {group.Key}");
}

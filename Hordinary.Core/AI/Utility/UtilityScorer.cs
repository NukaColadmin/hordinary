using System.Text;

namespace Hordinary.AI.Utility;

/// <summary>Detail d'une consideration : ce qui a ete lu, et ce que la courbe en a fait.</summary>
public readonly record struct ConsiderationScore(string Input, float Raw, float Value, float Weight)
{
    public override string ToString() => $"{Input} {Raw:0.00}->{Value:0.00}";
}

/// <summary>
/// Score d'un objectif, avec le detail qui l'explique. Le detail n'est pas un luxe : c'est ce que
/// le mode debug affiche (specification section 20.2) et ce qui rend une decision relisible.
/// </summary>
public sealed record GoalScore(
    string Goal,
    float Score,
    IReadOnlyList<ConsiderationScore> Breakdown,
    string? BlockedBy = null)
{
    /// <summary>Vrai si une precondition a ecarte l'objectif : il n'a pas ete score, il n'existe pas.</summary>
    public bool IsBlocked => BlockedBy is not null;

    public string Describe()
    {
        if (IsBlocked) return $"{Goal,-22} ecarte : {BlockedBy}";

        var builder = new StringBuilder();
        builder.Append($"{Goal,-22} {Score:0.00}");

        if (Breakdown.Count > 0)
        {
            builder.Append("  = ");
            builder.AppendJoin(" x ", Breakdown.Select(c => c.ToString()));
        }

        return builder.ToString();
    }

    public override string ToString() => IsBlocked ? $"{Goal} (ecarte)" : $"{Goal} {Score:0.00}";
}

/// <summary>
/// Moteur de score : il applique le meme mecanisme de composition a tous les objectifs
/// (specification section 11.3). <c>flee_score</c>, <c>hide_score</c> et <c>mission_score</c>
/// n'en sont que trois jeux de donnees.
/// </summary>
public sealed class UtilityScorer(UtilityGoalSet goals)
{
    public UtilityGoalSet Goals { get; } = goals ?? throw new ArgumentNullException(nameof(goals));

    /// <summary>Score tous les objectifs actifs, du meilleur au moins bon.</summary>
    public IReadOnlyList<GoalScore> ScoreAll(UtilityContext context)
        => Goals.Active
            .Select(goal => Score(goal, context))
            .OrderByDescending(score => score.Score)
            .ToList();

    public GoalScore Score(UtilityGoal goal, UtilityContext context)
    {
        foreach (var precondition in goal.Preconditions)
        {
            float value = UtilityInputs.Read(precondition.Input, context);
            if (precondition.IsSatisfiedBy(value)) continue;

            return new GoalScore(goal.Name, 0f, [], $"{precondition.Input} = {value:0.00}, attendu {precondition}");
        }

        var breakdown = new List<ConsiderationScore>(goal.Considerations.Count);

        foreach (var consideration in goal.Considerations)
        {
            float raw = UtilityInputs.Read(consideration.Input, context);
            breakdown.Add(new ConsiderationScore(
                consideration.Input, raw, consideration.Evaluate(raw), consideration.Weight));
        }

        float score = Math.Clamp(Combine(goal.Combine, breakdown) * goal.Weight, 0f, 1f);
        return new GoalScore(goal.Name, score, breakdown);
    }

    /// <summary>
    /// Agrege les considerations. En multiplication — le mode par defaut — le poids est un exposant :
    /// il durcit ou adoucit une consideration sans jamais lui retirer son droit de veto, une
    /// consideration a 0 annulant toujours l'objectif.
    /// </summary>
    private static float Combine(CombineMode mode, IReadOnlyList<ConsiderationScore> breakdown)
    {
        if (breakdown.Count == 0) return 0f;

        switch (mode)
        {
            case CombineMode.Average:
                float sum = 0f, weights = 0f;
                foreach (var c in breakdown)
                {
                    sum += c.Value * c.Weight;
                    weights += c.Weight;
                }
                return weights <= 0f ? 0f : sum / weights;

            case CombineMode.Min:
                return breakdown.Min(c => c.Value);

            case CombineMode.Max:
                return breakdown.Max(c => c.Value);

            default:
                float product = 1f;
                foreach (var c in breakdown)
                {
                    product *= c.Weight == 1f ? c.Value : MathF.Pow(c.Value, Math.Max(0f, c.Weight));
                }
                return product;
        }
    }
}

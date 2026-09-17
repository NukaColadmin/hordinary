namespace Hordinary.AI.Utility;

/// <summary>
/// Resultat d'une reevaluation : l'objectif retenu, son score, s'il a change, et la phrase qui
/// l'explique — celle qui part dans le journal des decisions de l'IA.
/// </summary>
public readonly record struct GoalDecision(
    string? Goal,
    float Score,
    bool Changed,
    string Reason,
    IReadOnlyList<GoalScore> Scores)
{
    public override string ToString() => Reason;
}

/// <summary>
/// Choisit et maintient l'objectif courant d'un survivant (specification section 11.4).
///
/// Trois garde-fous empechent l'oscillation :
/// un cooldown (<see cref="UtilityAiSettings.EvaluationInterval"/>) — on ne rejoue pas la decision
/// a chaque frame ; une hysteresis (<see cref="UtilityAiSettings.SwitchThreshold"/>) — un
/// pretendant doit faire nettement mieux que le titulaire ; un seuil minimum
/// (<see cref="UtilityAiSettings.MinimumScore"/>) — en dessous, rien ne vaut la peine.
///
/// <see cref="Request"/> permet aux evenements importants (zombie decouvert, bruit fort, plan
/// echoue, survivant blesse) de court-circuiter le cooldown sans court-circuiter l'hysteresis.
/// </summary>
public sealed class UtilitySelector
{
    private readonly UtilityScorer _scorer;

    public UtilityAiSettings Settings { get; }

    public UtilityGoalSet Goals => _scorer.Goals;

    /// <summary>Nom de l'objectif courant, ou <c>null</c> si aucun ne depasse le seuil minimum.</summary>
    public string? CurrentGoal { get; private set; }

    /// <summary>Definition complete de l'objectif courant (sa cible, notamment).</summary>
    public UtilityGoal? Current => Goals.Find(CurrentGoal);

    public float CurrentScore { get; private set; }

    /// <summary>Date de la derniere reevaluation, en secondes de jeu.</summary>
    public double LastEvaluationAt { get; private set; } = double.NegativeInfinity;

    /// <summary>Date du dernier changement d'objectif.</summary>
    public double LastChangeAt { get; private set; }

    /// <summary>Derniers scores calcules, du meilleur au moins bon. Alimente le mode debug.</summary>
    public IReadOnlyList<GoalScore> LastScores { get; private set; } = [];

    /// <summary>Evenement en attente qui declenchera une reevaluation immediate, le cas echeant.</summary>
    public string? PendingTrigger { get; private set; }

    public int EvaluationCount { get; private set; }

    /// <summary>Emis uniquement lorsque l'objectif change reellement.</summary>
    public event Action<GoalDecision>? GoalChanged;

    public UtilitySelector(UtilityGoalSet goals, UtilityAiSettings? settings = null)
    {
        _scorer = new UtilityScorer(goals);
        Settings = settings ?? UtilityAiSettings.Default;
    }

    /// <summary>
    /// Signale un evenement important (section 11.4). La prochaine occasion de reflechir devient
    /// immediate ; le premier evenement recu donne son nom a la decision, les suivants s'y greffent.
    /// </summary>
    public void Request(string reason) => PendingTrigger ??= reason;

    /// <summary>
    /// Remet la decision a plat : l'objectif courant cesse de tenir la place, et la prochaine
    /// occasion de reflechir devient immediate.
    ///
    /// <see cref="Request"/> ne court-circuite que le cooldown, et cela ne suffit pas a un ordre qui
    /// ne se discute plus. Un survivant terre dans la pharmacie scorait <c>Hide</c> a 0,25 et le
    /// rappel du van a 0,37 : le rappel est le meilleur score, mais l'ecart reste sous l'hysteresis
    /// de 0,15 — et il ne le franchira jamais, puisque se terrer entretient exactement le danger qui
    /// fait se terrer. Le van partait, le survivant restait, la mission ne se concluait pas.
    ///
    /// Ce n'est toujours pas du pilotage : la decision est refaite, pas imposee. Celui qu'une horde
    /// poursuit rescore la fuite en tete et fuit d'abord.
    /// </summary>
    public void Reconsider(string reason)
    {
        CurrentGoal = null;
        CurrentScore = 0f;
        PendingTrigger ??= reason;
    }

    /// <summary>Vrai si le cooldown est ecoule ou si un evenement important attend d'etre pris en compte.</summary>
    public bool ShouldEvaluate(double now)
        => PendingTrigger is not null || now - LastEvaluationAt >= Settings.EvaluationInterval;

    /// <summary>
    /// Rejoue la decision. A appeler quand <see cref="ShouldEvaluate"/> le permet : cette methode ne
    /// verifie pas le cooldown elle-meme, pour rester utilisable telle quelle dans un test.
    /// </summary>
    public GoalDecision Evaluate(UtilityContext context, double now)
    {
        string trigger = PendingTrigger ?? "reevaluation periodique";
        PendingTrigger = null;
        LastEvaluationAt = now;
        EvaluationCount++;

        var scores = _scorer.ScoreAll(context);
        LastScores = scores;

        var current = CurrentGoal is null
            ? null
            : scores.FirstOrDefault(score => score.Goal.Equals(CurrentGoal, StringComparison.OrdinalIgnoreCase));

        // Un objectif ecarte par une precondition, retire de la configuration ou retombe sous le
        // seuil ne « tient » plus la place : il ne beneficie pas de l'hysteresis.
        bool currentStillHolds = current is { IsBlocked: false } && current.Score >= Settings.MinimumScore;
        CurrentScore = currentStillHolds ? current!.Score : 0f;

        var best = scores.FirstOrDefault(score => !score.IsBlocked && score.Score >= Settings.MinimumScore);

        if (best is null)
        {
            if (CurrentGoal is null) return Keep(null, $"aucun objectif au-dessus du seuil ({trigger})", scores);

            string abandoned = CurrentGoal;
            return Switch(null, 0f, now, $"{abandoned} abandonne : plus aucun objectif au-dessus du seuil ({trigger})", scores);
        }

        if (CurrentGoal is null)
        {
            return Switch(best.Goal, best.Score, now, $"{best.Goal} choisi ({best.Score:0.00}) — {trigger}", scores);
        }

        if (best.Goal.Equals(CurrentGoal, StringComparison.OrdinalIgnoreCase))
        {
            return Keep(CurrentGoal, $"{CurrentGoal} maintenu ({best.Score:0.00})", scores);
        }

        if (!currentStillHolds)
        {
            string cause = current?.BlockedBy is { } blocked
                ? $"n'est plus applicable ({blocked})"
                : $"est retombe sous le seuil de {Settings.MinimumScore:0.00}";

            return Switch(
                best.Goal, best.Score, now,
                $"{CurrentGoal} {cause} : {best.Goal} prend la suite ({best.Score:0.00}) — {trigger}",
                scores);
        }

        if (best.Score > CurrentScore + Settings.SwitchThreshold)
        {
            return Switch(
                best.Goal, best.Score, now,
                $"{CurrentGoal} abandonne pour {best.Goal} "
                + $"({best.Score:0.00} > {CurrentScore:0.00} + {Settings.SwitchThreshold:0.00}) — {trigger}",
                scores);
        }

        return Keep(
            CurrentGoal,
            $"{CurrentGoal} maintenu ({CurrentScore:0.00}) : {best.Goal} ({best.Score:0.00}) "
            + $"ne depasse pas l'hysteresis de {Settings.SwitchThreshold:0.00}",
            scores);
    }

    /// <summary>Oublie l'objectif courant, par exemple quand le survivant est mis hors de combat.</summary>
    public void Reset()
    {
        CurrentGoal = null;
        CurrentScore = 0f;
        LastScores = [];
        PendingTrigger = null;
        LastEvaluationAt = double.NegativeInfinity;
    }

    private GoalDecision Keep(string? goal, string reason, IReadOnlyList<GoalScore> scores)
        => new(goal, CurrentScore, false, reason, scores);

    private GoalDecision Switch(string? goal, float score, double now, string reason, IReadOnlyList<GoalScore> scores)
    {
        CurrentGoal = goal;
        CurrentScore = score;
        LastChangeAt = now;

        var decision = new GoalDecision(goal, score, true, reason, scores);
        GoalChanged?.Invoke(decision);
        return decision;
    }
}

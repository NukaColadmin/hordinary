namespace Hordinary.AI.Planning;

/// <summary>Ou en est un plan. <see cref="Invalidated"/> est le point d'entree du livrable 6.</summary>
public enum PlanStatus
{
    /// <summary>Produit, pas encore commence.</summary>
    Ready = 0,

    /// <summary>Une action est en cours d'execution.</summary>
    Running,

    /// <summary>Toutes les actions ont reussi et le but est atteint.</summary>
    Completed,

    /// <summary>Une action a echoue en cours d'execution.</summary>
    Failed,

    /// <summary>Le monde a change : le plan n'est plus valable, meme si rien n'a echoue.</summary>
    Invalidated,
}

/// <summary>
/// Suite d'actions produite par le planificateur (modele de donnees du document A : <c>goal</c>,
/// <c>actions</c>, <c>current_action_index</c>, <c>created_from_state_hash</c>).
///
/// Le plan garde l'empreinte de l'etat symbolique qui l'a engendre. Ce n'est pas decoratif : c'est ce
/// qui permettra au livrable 6 de reconnaitre qu'un plan a ete concu dans un monde qui n'existe plus
/// — porte trouvee verrouillee, medicaments absents — sans avoir a rejouer la recherche.
/// </summary>
public sealed class Plan
{
    private readonly List<PlanningAction> _actions;

    public Plan(
        PlanningProblem problem,
        IReadOnlyList<PlanningAction> actions,
        double createdAt = 0d,
        bool isFallback = false)
    {
        Problem = problem ?? throw new ArgumentNullException(nameof(problem));
        _actions = [.. actions];
        CreatedAt = createdAt;
        IsFallback = isFallback;
        CreatedFromStateHash = problem.Initial.Hash;
        Status = _actions.Count == 0 ? PlanStatus.Completed : PlanStatus.Ready;
    }

    public PlanningProblem Problem { get; }

    /// <summary>Nom de l'objectif Utility AI a l'origine du plan.</summary>
    public string Goal => Problem.GoalName;

    public IReadOnlyList<PlanningAction> Actions => _actions;

    public int CurrentActionIndex { get; private set; }

    /// <summary>Empreinte de l'etat symbolique de depart (voir <see cref="SymbolicState.Hash"/>).</summary>
    public string CreatedFromStateHash { get; }

    public double CreatedAt { get; }

    /// <summary>Vrai si le plan est celui du repli sur l'exploration, faute d'avoir trouve mieux.</summary>
    public bool IsFallback { get; }

    public PlanStatus Status { get; private set; }

    /// <summary>Pourquoi le plan s'est arrete, quand il s'est arrete mal.</summary>
    public string? FailureReason { get; private set; }

    public PlanningAction? Current => CurrentActionIndex < _actions.Count ? _actions[CurrentActionIndex] : null;

    public bool IsFinished => CurrentActionIndex >= _actions.Count;

    public bool IsActive => Status is PlanStatus.Ready or PlanStatus.Running;

    public int StepCount => _actions.Count;

    /// <summary>Cout total, trajets compris (voir <see cref="PlanningProblem.TransitionCost"/>).</summary>
    public float TotalCost { get; internal init; }

    /// <summary>Bruit cumule du plan, au bareme de la section 18.1.</summary>
    public float TotalNoise => _actions.Sum(action => action.NoiseLevel);

    public float TotalDuration => _actions.Sum(action => action.Duration);

    /// <summary>
    /// Etat symbolique attendu apres les <paramref name="steps"/> premieres actions. Sert a verifier
    /// qu'un plan mene bien au but, et au livrable 6 a comparer le monde attendu au monde constate.
    /// </summary>
    public SymbolicState ProjectedState(int steps)
    {
        var state = Problem.Initial;
        for (int i = 0; i < Math.Min(steps, _actions.Count); i++) state = _actions[i].ApplyTo(state);
        return state;
    }

    /// <summary>Etat attendu a la fin du plan.</summary>
    public SymbolicState ExpectedFinalState => ProjectedState(_actions.Count);

    /// <summary>Vrai si l'enchainement des actions est realisable et mene reellement au but.</summary>
    public bool IsValid()
    {
        var state = Problem.Initial;

        foreach (var action in _actions)
        {
            if (!action.IsApplicable(state)) return false;
            state = action.ApplyTo(state);
        }

        return Problem.IsSatisfiedBy(state);
    }

    public void Begin()
    {
        if (Status == PlanStatus.Ready) Status = PlanStatus.Running;
    }

    /// <summary>Passe a l'action suivante. Renvoie <c>false</c> quand le plan est termine.</summary>
    public bool Advance()
    {
        if (IsFinished) return false;

        CurrentActionIndex++;

        if (!IsFinished)
        {
            Status = PlanStatus.Running;
            return true;
        }

        Status = PlanStatus.Completed;
        return false;
    }

    public void Fail(string reason)
    {
        Status = PlanStatus.Failed;
        FailureReason = reason;
    }

    /// <summary>Abandonne le plan sans qu'aucune action n'ait echoue : le monde a change sous lui.</summary>
    public void Invalidate(string reason)
    {
        Status = PlanStatus.Invalidated;
        FailureReason = reason;
    }

    /// <summary>Resume d'une ligne pour le journal des decisions de l'IA.</summary>
    public string Describe()
        => _actions.Count == 0
            ? $"{Goal} : deja atteint, aucune action necessaire"
            : $"{Goal} : {string.Join(" -> ", _actions.Select(action => action.Name))} "
            + $"({_actions.Count} actions, cout {TotalCost:0.##}, bruit {TotalNoise:0.##}, {TotalDuration:0.#} s)";

    /// <summary>Detail multiligne pour le panneau debug (specification section 20.2).</summary>
    public IEnumerable<string> DescribeSteps()
    {
        for (int i = 0; i < _actions.Count; i++)
        {
            string marker = i < CurrentActionIndex ? "x" : i == CurrentActionIndex ? ">" : " ";
            yield return $"{marker} {i + 1}. {_actions[i].Describe()}";
        }
    }

    public override string ToString() => Describe();
}

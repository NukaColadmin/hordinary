using System.Globalization;
using Hordinary.Config;
using Hordinary.World;

namespace Hordinary.AI.Planning;

/// <summary>Ou en est le deplacement demande par le verbe <c>toPos</c>.</summary>
public enum MoveState
{
    /// <summary>En route.</summary>
    Moving = 0,

    /// <summary>Arrive a destination.</summary>
    Arrived,

    /// <summary>L'itineraire ne tient plus : obstacle decouvert, porte refermee, chemin introuvable.</summary>
    Blocked,
}

/// <summary>
/// Nature d'un echec d'action. La distinction n'a pas d'interet pour l'executeur — il s'arrete dans
/// tous les cas — mais le livrable 6 en a besoin : la specification y separe « une action echoue »,
/// « une precondition devient fausse », « un obstacle est decouvert » et « le chemin A* est
/// impossible ». Les nommer ici evite de deduire la cause en relisant un message.
/// </summary>
public enum ActionFailureKind
{
    /// <summary>Une precondition n'etait plus vraie au moment de commencer.</summary>
    Precondition = 0,

    /// <summary>Le verbe primitif a refuse : porte deja ouverte, piece vide, objet absent.</summary>
    Verb,

    /// <summary>Aucun itineraire connu ne mene a la destination de l'action.</summary>
    NoRoute,

    /// <summary>Le trajet a ete interrompu : une cellule crue praticable ne l'etait pas.</summary>
    RouteBlocked,

    /// <summary>La cible de l'action n'est situable nulle part sur la carte mentale.</summary>
    UnknownDestination,
}

/// <summary>Echec d'une action : sa nature, et la phrase qui part dans le journal.</summary>
public readonly record struct ActionFailure(ActionFailureKind Kind, string Reason)
{
    public override string ToString() => Reason;
}

/// <summary>
/// Ce que le moteur de jeu doit savoir faire pour executer les verbes primitifs.
///
/// L'interface est volontairement minuscule, et surtout : elle ne comporte pas une methode par
/// action. <c>OpenDoor</c>, <c>SearchRoom</c>, <c>HealAlly</c> et <c>ShareKnowledge</c> passent tous
/// par <see cref="SetFlag"/>, avec un fait different. C'est ce qui permet d'ajouter une action au jeu
/// en editant <c>config/planner_domain.json</c>, sans toucher ni au coeur ni a la couche Godot.
/// </summary>
public interface IPlanRuntime
{
    GridPosition AgentCell { get; }

    /// <summary>Lance un trajet vers une cellule. <c>false</c> si la carte mentale n'y mene pas.</summary>
    bool BeginMove(GridPosition destination);

    MoveState PollMove(GridPosition destination);

    /// <summary>Ramasse un objet la ou l'agent se trouve.</summary>
    bool Take(string item);

    bool Put(string item, GridPosition where);

    /// <summary>
    /// Realise dans le monde ce qu'un effet symbolique affirme : ouvrir la porte designee par
    /// <paramref name="fact"/>, marquer une piece fouillee, soigner un allie.
    /// </summary>
    bool SetFlag(string fact, bool value, GridPosition? where);

    /// <summary>Reserve au combat (livrable 8) : aucune action livree n'utilise ce verbe.</summary>
    bool Damage(string target, float amount);

    /// <summary>
    /// Interrompt le trajet en cours. C'est « l'annulation de l'action actuelle » du flux de
    /// replanification (livrable 6) : sans elle, un survivant dont le plan vient d'etre abandonne
    /// continuerait tranquillement de marcher vers la destination de l'ancien plan.
    /// </summary>
    void CancelMove();

    /// <summary>
    /// Une action bruyante s'entend (bareme de la section 18.1). <paramref name="label"/> nomme
    /// l'evenement — forcer une porte ne s'entend pas comme briser une vitre — sans changer son
    /// niveau ; une action qui n'en donne pas produit un bruit generique.
    /// </summary>
    void EmitNoise(float level, GridPosition where, string? label = null);
}

/// <summary>
/// Execute les actions d'un plan, une par une (specification, livrable 5 : « Executer les actions du
/// plan »).
///
/// L'executeur est un petit automate par action : verification des preconditions, declenchement du
/// verbe, attente de sa duree, application des effets a l'etat symbolique courant. Il n'y a pas de
/// branche par action — seulement six, une par verbe primitif.
///
/// Il verifie les preconditions <b>au moment</b> de commencer chaque action, et non une fois pour
/// toutes a la planification : c'est ainsi qu'une porte trouvee verrouillee en chemin fait echouer
/// l'action au lieu de faire semblant de reussir. Le livrable 6 se branche sur
/// <see cref="ActionFailed"/>.
/// </summary>
public sealed class PlanExecutor
{
    private readonly IPlanRuntime _runtime;
    private readonly PlannerSettings _settings;

    private float _elapsedInStep;
    private bool _stepStarted;
    private GridPosition? _destination;

    public PlanExecutor(IPlanRuntime runtime, PlannerSettings? settings = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _settings = settings ?? new PlannerSettings();
        State = SymbolicState.Empty;
    }

    public Plan? Plan { get; private set; }

    /// <summary>Etat symbolique tel que l'execution l'a fait evoluer depuis le debut du plan.</summary>
    public SymbolicState State { get; private set; }

    public bool IsBusy => Plan is { IsActive: true, IsFinished: false };

    /// <summary>Temps passe sur l'action en cours, en secondes.</summary>
    public float ElapsedInStep => _elapsedInStep;

    public event Action<PlanningAction>? ActionStarted;
    public event Action<PlanningAction>? ActionCompleted;

    /// <summary>Action et cause de l'echec. C'est l'evenement declencheur de la replanification.</summary>
    public event Action<PlanningAction, ActionFailure>? ActionFailed;

    public event Action<Plan>? PlanFinished;

    public void Start(Plan plan)
    {
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        State = plan.Problem.Initial;
        ResetStep();

        plan.Begin();

        if (plan.IsFinished) Finish(plan);
    }

    /// <summary>Laisse tomber le plan courant sans qu'aucune action n'ait echoue.</summary>
    public void Abandon(string reason)
    {
        if (Plan is not { IsActive: true } plan) return;

        plan.Invalidate(reason);
        ResetStep();
        _runtime.CancelMove();
        PlanFinished?.Invoke(plan);
    }

    /// <summary>Fait avancer l'action courante de <paramref name="delta"/> secondes.</summary>
    public void Advance(float delta)
    {
        if (Plan is not { } plan || !plan.IsActive || plan.Current is not { } action) return;

        if (!_stepStarted && !BeginStep(plan, action)) return;

        _elapsedInStep += Math.Max(0f, delta);

        if (action.Verb == ActionVerb.ToPos)
        {
            AdvanceMove(plan, action);
            return;
        }

        // Les autres verbes sont instantanes une fois leur duree ecoulee : forcer une porte prend du
        // temps, mais rien ne se passe tant qu'elle n'est pas forcee.
        if (_elapsedInStep < action.Duration) return;

        if (Perform(action, _runtime.AgentCell)) Succeed(plan, action);
        else Fail(plan, action, ActionFailureKind.Verb, $"le verbe {ActionVerbs.NameOf(action.Verb)} a echoue");
    }

    // --- Deroulement d'une action ---------------------------------------------------------------

    private bool BeginStep(Plan plan, PlanningAction action)
    {
        // Le monde a pu changer depuis la planification : c'est ici qu'on s'en apercoit.
        foreach (var condition in action.Preconditions)
        {
            if (State.Satisfies(condition)) continue;

            Fail(plan, action, ActionFailureKind.Precondition, $"precondition « {condition} » non satisfaite");
            return false;
        }

        _stepStarted = true;
        _elapsedInStep = 0f;
        _destination = null;

        if (action.NoiseLevel > 0f) _runtime.EmitNoise(action.NoiseLevel, _runtime.AgentCell, action.NoiseAction);

        ActionStarted?.Invoke(action);

        if (action.Verb != ActionVerb.ToPos) return true;

        if (ResolveTarget(action) is not { } destination)
        {
            Fail(
                plan, action, ActionFailureKind.UnknownDestination,
                $"la destination « {action.TargetObject ?? "?"} » n'est pas situable");
            return false;
        }

        _destination = destination;

        if (destination.ChebyshevDistanceTo(_runtime.AgentCell) <= _settings.ArrivalRadius) return true;

        if (_runtime.BeginMove(destination)) return true;

        Fail(plan, action, ActionFailureKind.NoRoute, $"aucun itineraire connu vers {destination}");
        return false;
    }

    private void AdvanceMove(Plan plan, PlanningAction action)
    {
        if (_destination is not { } destination)
        {
            Fail(plan, action, ActionFailureKind.UnknownDestination, "deplacement sans destination");
            return;
        }

        if (destination.ChebyshevDistanceTo(_runtime.AgentCell) <= _settings.ArrivalRadius)
        {
            Succeed(plan, action);
            return;
        }

        switch (_runtime.PollMove(destination))
        {
            case MoveState.Arrived:
                Succeed(plan, action);
                break;

            case MoveState.Blocked:
                Fail(plan, action, ActionFailureKind.RouteBlocked, $"itineraire vers {destination} interrompu");
                break;
        }
    }

    /// <summary>Traduit un verbe primitif en appel au moteur. Six branches, pour dix-sept actions.</summary>
    private bool Perform(PlanningAction action, GridPosition here) => action.Verb switch
    {
        ActionVerb.Take => _runtime.Take(Parameter(action, "item")),
        ActionVerb.Put => _runtime.Put(Parameter(action, "item"), ResolveTarget(action) ?? here),
        ActionVerb.SetFlag => _runtime.SetFlag(
            Parameter(action, "condition"),
            Flag(action, "value"),
            ResolveTarget(action)),
        ActionVerb.Damage => _runtime.Damage(Parameter(action, "target"), Amount(action, "amount")),
        ActionVerb.Wait => true,
        _ => true,
    };

    private void Succeed(Plan plan, PlanningAction action)
    {
        State = action.ApplyTo(State);
        ActionCompleted?.Invoke(action);
        ResetStep();

        if (plan.Advance()) return;

        Finish(plan);
    }

    private void Fail(Plan plan, PlanningAction action, ActionFailureKind kind, string reason)
    {
        plan.Fail($"{action.Name} : {reason}");
        ResetStep();
        _runtime.CancelMove();

        ActionFailed?.Invoke(action, new ActionFailure(kind, reason));
        PlanFinished?.Invoke(plan);
    }

    private void Finish(Plan plan)
    {
        ResetStep();
        PlanFinished?.Invoke(plan);
    }

    private void ResetStep()
    {
        _stepStarted = false;
        _elapsedInStep = 0f;
        _destination = null;
    }

    private GridPosition? ResolveTarget(PlanningAction action)
        => action.TargetObject is { } target ? Plan?.Problem.PositionOf(target) : null;

    private static string Parameter(PlanningAction action, string key)
        => action.Parameters.TryGetValue(key, out string? value) ? value : string.Empty;

    private static bool Flag(PlanningAction action, string key)
        => !bool.TryParse(Parameter(action, key), out bool value) || value;

    private static float Amount(PlanningAction action, string key)
        => float.TryParse(Parameter(action, key), NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value
            : 0f;
}

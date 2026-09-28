using Hordinary.AI.Planning;
using Hordinary.AI.Utility;
using Hordinary.Agents;

namespace Hordinary.AI.Replanning;

/// <summary>
/// La boucle de decision d'un survivant, et surtout ce qui la relance (specification, livrable 6).
///
/// Elle suit le flux de la specification, dans cet ordre :
///
/// <code>
/// Evenement detecte -> annulation de l'action actuelle -> mise a jour des connaissances
/// -> reevaluation Utility AI -> creation du nouvel etat symbolique -> recherche GBFS
/// -> execution du nouveau plan
/// </code>
///
/// La mise a jour des connaissances n'est pas ici : <c>PerceptionSystem</c> l'a deja faite quand
/// <see cref="Advance"/> est appele, et le <see cref="UtilityContext"/> recu en est le resultat. Le
/// reste — annuler, reevaluer, reconstruire l'etat symbolique, relancer GBFS, executer — tient dans
/// cette classe.
///
/// Elle vit dans le coeur, sans dependance au moteur : la couche Godot ne fait que lui passer un
/// contexte et afficher son journal, ce qui permet de derouler une mission entiere en test. C'est
/// aussi ce qui garantit que le jeu et les tests suivent la meme boucle, et non deux copies qui
/// divergent.
/// </summary>
public sealed class Replanner
{
    private readonly Survivor _survivor;
    private readonly UtilitySelector _utility;
    private readonly Planner _planner;
    private readonly PlanExecutor _executor;
    private readonly ReplanningSettings _settings;

    /// <summary>
    /// Dernier plan dont A* a refuse une destination. Le couple objectif/actions suffit a reconnaitre
    /// la tentative reconstruite a l'identique lors de la passe suivante.
    /// </summary>
    private FailedRoute? _failedRoute;

    /// <summary>
    /// Etat dans lequel une tentative identique a ete mise en attente. Tant qu'il ne change pas,
    /// relancer GBFS ne peut produire aucune information nouvelle.
    /// </summary>
    private string? _parkedStateHash;

    /// <summary>Date de la derniere passe, pour horodater ce que les evenements racontent.</summary>
    private double _now;

    public Replanner(
        Survivor survivor,
        UtilitySelector utility,
        Planner planner,
        PlanExecutor executor,
        ReplanningSettings? settings = null,
        DecisionJournal? journal = null)
    {
        _survivor = survivor ?? throw new ArgumentNullException(nameof(survivor));
        _utility = utility ?? throw new ArgumentNullException(nameof(utility));
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _settings = settings ?? new ReplanningSettings();

        Monitor = new ReplanMonitor(_settings);
        Journal = journal ?? new DecisionJournal(_settings.JournalCapacity);

        // Un echec d'action est le declencheur le plus direct de la specification : l'executeur le
        // sait avant tout le monde, il le dit au moniteur sans passer par la couche moteur.
        _executor.ActionFailed += (action, failure) =>
        {
            Monitor.Report(action, failure, _now);
            Journal.Add(_now, _survivor.Name, DecisionKind.Action, $"{action.Name} echoue : {failure.Reason}");
            _survivor.PlanConfidence = 0f;

            _failedRoute = failure.Kind is ActionFailureKind.NoRoute or ActionFailureKind.UnknownDestination
                && _executor.Plan is { } failed
                    ? new FailedRoute(failed.Goal, PlanKey(failed))
                    : null;
        };

        _executor.ActionStarted += action
            => Journal.Add(_now, _survivor.Name, DecisionKind.Action, $"commence {action.Describe()}");
    }

    public ReplanMonitor Monitor { get; }

    public DecisionJournal Journal { get; }

    /// <summary>Plan courant, ou <c>null</c> si aucune planification n'a encore abouti.</summary>
    public Plan? Plan { get; private set; }

    /// <summary>Dernier declencheur retenu. Alimente le HUD et le panneau debug.</summary>
    public ReplanTrigger? LastTrigger { get; private set; }

    /// <summary>Derniere replanification journalisee, sous sa forme structuree.</summary>
    public ReplanRecord? LastReplan { get; private set; }

    /// <summary>Phrase expliquant la derniere planification, reussie ou non.</summary>
    public string LastReason { get; private set; } = "aucune planification";

    /// <summary>
    /// Dernier etat symbolique reconcilie : ce que le plan a accompli, corrige par ce que l'agent
    /// vient d'observer. C'est l'etat sur lequel la validite du plan est jugee.
    /// </summary>
    public SymbolicState State { get; private set; } = SymbolicState.Empty;

    public int ReplanCount => Journal.ReplanCount;

    /// <summary>Replanifications d'affilee qui n'ont produit aucun plan.</summary>
    public int ConsecutiveFailures { get; private set; }

    /// <summary>
    /// Vrai quand l'objectif courant est suspendu : le meme plan vient d'echouer faute d'itineraire, et
    /// rien ne sera retente avant une information nouvelle. C'est, avec l'absence de plan, ce qui dit
    /// qu'un ordre de camarade ne peut pas etre rempli (<see cref="Agents.SurvivorAgent"/>).
    /// </summary>
    public bool Suspended => _parkedStateHash is not null;

    public double LastReplanAt { get; private set; } = double.NegativeInfinity;

    /// <summary>Emis a chaque replanification, une fois le nouveau plan connu.</summary>
    public event Action<ReplanRecord>? Replanned;

    /// <summary>
    /// Une passe de la boucle. A appeler apres la perception, avec le contexte qu'elle vient de
    /// produire : c'est la « mise a jour des connaissances » du flux, et rien ici ne va chercher
    /// ailleurs que dans la memoire du survivant.
    /// </summary>
    public void Advance(UtilityContext context, float delta)
    {
        ArgumentNullException.ThrowIfNull(context);

        _now = context.Now;

        if (!_survivor.Alive) return;

        var trigger = Detect(context);

        // Un evenement important court-circuite le cooldown de la Utility AI, jamais son hysteresis :
        // reflechir plus tot ne veut pas dire changer d'avis plus facilement (section 11.4).
        if (trigger is { } found)
        {
            LastTrigger = found;
            _utility.Request(found.Reason);
        }

        if (_utility.ShouldEvaluate(context.Now)) Decide(context, trigger);

        _executor.Advance(delta);
    }

    /// <summary>Signale un evenement que seule la couche moteur peut constater.</summary>
    public void Report(ReplanCause cause, string detail, double at) => Monitor.Report(cause, detail, at);

    /// <summary>Tout oublier : survivant hors de combat, ou nouvelle partie.</summary>
    public void Reset(string reason)
    {
        _executor.Abandon(reason);
        _utility.Reset();
        Monitor.Reset();

        Plan = null;
        LastTrigger = null;
        State = SymbolicState.Empty;
        LastReason = reason;
        ConsecutiveFailures = 0;
        _failedRoute = null;
        _parkedStateHash = null;
        _survivor.CurrentGoal = null;
    }

    // --- Detection ------------------------------------------------------------------------------

    /// <summary>
    /// Reconstruit l'etat symbolique depuis la memoire, le confronte a l'avancement du plan, et
    /// demande au moniteur s'il y a matiere a replanifier.
    ///
    /// Deux freins, pour ne pas planifier a chaque frame : l'intervalle de verification du moniteur,
    /// et un delai apres chaque replanification. Un evenement deja constate par le moteur passe
    /// devant le second — il n'est pas une hypothese a verifier, il est arrive.
    /// </summary>
    private ReplanTrigger? Detect(UtilityContext context)
    {
        bool throttled = context.Now - LastReplanAt < _settings.MinimumInterval;

        if (throttled && !Monitor.HasReport) return null;
        if (!Monitor.ShouldInspect(context.Now)) return null;

        var observed = SymbolicStateBuilder.Observe(context, _planner.Settings).State;
        State = _executor.State.Reconcile(observed, SymbolicStateBuilder.ObservablePredicates);

        return Monitor.Inspect(context, Plan, State);
    }

    // --- Decision -------------------------------------------------------------------------------

    private void Decide(UtilityContext context, ReplanTrigger? trigger)
    {
        string? previousGoal = Plan?.Goal;

        var decision = _utility.Evaluate(context, context.Now);
        _survivor.CurrentGoal = decision.Goal;

        if (decision.Changed) Journal.Add(context.Now, _survivor.Name, DecisionKind.Goal, decision.Reason);

        if (decision.Goal is not { } goal)
        {
            if (_executor.IsBusy) _executor.Abandon("plus aucun objectif au-dessus du seuil");

            Plan = null;
            LastReason = "aucun objectif au-dessus du seuil";
            return;
        }

        bool sameGoal = previousGoal is not null && previousGoal.Equals(goal, StringComparison.OrdinalIgnoreCase);

        // Un plan mis en attente apres un NoRoute ne se relance que si une observation a change
        // l'etat symbolique, ou si un nouvel evenement explicite demande de revoir la situation.
        // Sans ce verrou, une reevaluation periodique suffit a recreer la meme boucle quelques
        // dixiemes de seconde plus tard.
        if (trigger is null && sameGoal && _parkedStateHash == State.Hash) return;

        if (_parkedStateHash is not null && _parkedStateHash != State.Hash)
        {
            _parkedStateHash = null;
            _failedRoute = null;
        }

        // Un plan en cours et toujours valable se laisse finir ; un objectif deja atteint ne se
        // replanifie pas en boucle pour qu'on lui reponde chaque fois « zero action ».
        if (trigger is null && sameGoal && Plan is { IsActive: true } or { StepCount: 0, Status: PlanStatus.Completed })
            return;

        // Sans declencheur, changer d'objectif est en soi une raison de replanifier — la neuvieme de
        // la liste. Une premiere planification, elle, n'a rien remplace : ce n'est pas une
        // replanification, et le journal ne doit pas la compter comme telle.
        var cause = trigger;
        if (cause is null && !sameGoal && Plan is not null)
            cause = new ReplanTrigger(ReplanCause.GoalChanged, decision.Reason, context.Now);

        StartPlan(context, goal, previousGoal, cause);
    }

    /// <summary>
    /// Les quatre dernieres etapes du flux : annuler l'action en cours, reconstruire l'etat
    /// symbolique — c'est <see cref="SymbolicStateBuilder"/>, appele par le planificateur —, relancer
    /// GBFS, confier le resultat a l'executeur.
    /// </summary>
    private void StartPlan(UtilityContext context, string goal, string? previousGoal, ReplanTrigger? trigger)
    {
        // Ce que le plan abandonne avait deja obtenu et qui reste vrai : sans cela, replanifier
        // revient a tout recommencer, et un survivant refouille sans fin la piece qu'il quitte.
        var carried = SymbolicStateBuilder.Carry(_executor.State, _planner.Domain).ToList();

        if (_executor.IsBusy) _executor.Abandon(trigger?.Reason ?? $"nouveau plan pour {goal}");

        var result = _planner.PlanFor(goal, context, context.Now, carried);

        bool repeatsFailedRoute = trigger is { Cause: ReplanCause.PathImpossible }
            && result.Plan is { } candidate
            && _failedRoute is { } failed
            && failed.Goal.Equals(goal, StringComparison.OrdinalIgnoreCase)
            && failed.PlanKey.Equals(PlanKey(candidate), StringComparison.Ordinal);

        if (repeatsFailedRoute)
        {
            // Le plan precedent reste le dernier plan effectivement joue. Le candidat identique
            // n'est pas confie a l'executeur et le journal dit pourquoi aucun nouveau plan ne part.
            _parkedStateHash = State.Hash;
            LastReason = $"{goal} suspendu : le meme plan vient d'echouer sans itineraire ; "
                       + "attente d'une nouvelle information";
            _survivor.PlanConfidence = 0f;
        }
        else
        {
            Plan = result.Plan;
            LastReason = result.Reason;
            _survivor.PlanConfidence = result.Found ? 1f : 0f;

            _parkedStateHash = null;
            _failedRoute = null;

            if (result.Plan is { } plan) _executor.Start(plan);
        }

        if (trigger is not { } cause)
        {
            Journal.Add(context.Now, _survivor.Name, DecisionKind.Plan, result.Reason);
            return;
        }

        LastReplanAt = context.Now;
        ConsecutiveFailures = result.Found && !repeatsFailedRoute ? 0 : ConsecutiveFailures + 1;

        var record = new ReplanRecord(
            context.Now, _survivor.Name, cause, previousGoal, goal,
            repeatsFailedRoute ? null : result.Plan,
            LastReason);

        LastReplan = record;
        Journal.AddReplan(record);
        Replanned?.Invoke(record);
    }

    private static string PlanKey(Plan plan)
        => $"{plan.Goal}|{string.Join('|', plan.Actions.Select(action => action.Name))}";

    private readonly record struct FailedRoute(string Goal, string PlanKey);
}

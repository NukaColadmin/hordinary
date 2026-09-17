using Hordinary.AI.Planning;
using Hordinary.AI.Utility;
using Hordinary.Agents;

namespace Hordinary.AI.Replanning;

/// <summary>
/// Guette les evenements qui rendent un plan caduc (specification, livrable 6 : « Detecter l'echec
/// d'une action », « Invalider un plan »).
///
/// Deux sources, et c'est voulu :
/// ce que le moteur constate et <see cref="Report"/> — une action qui echoue, un itineraire coupe —
/// et ce que le moniteur deduit lui-meme en comparant la memoire du survivant a ce que son plan
/// supposait. La premiere source ne peut rien dire avant que l'agent ne se cogne au probleme ; la
/// seconde le voit venir.
///
/// Chaque detection deduite est protegee par un front : un zombie ne compte qu'a la premiere
/// rencontre, un danger qu'au franchissement du seuil. Sans cela, une menace immobile dans le champ
/// de vision relancerait GBFS jusqu'a la fin de la partie — ce que la specification interdit
/// explicitement.
/// </summary>
public sealed class ReplanMonitor
{
    private readonly ReplanningSettings _settings;
    private readonly HashSet<string> _knownThreats = new(StringComparer.Ordinal);
    private readonly HashSet<string> _endangeredAllies = new(StringComparer.Ordinal);

    private ReplanTrigger? _reported;
    private bool _aboveDangerThreshold;
    private double _lastInspectionAt = double.NegativeInfinity;

    public ReplanMonitor(ReplanningSettings? settings = null) => _settings = settings ?? new ReplanningSettings();

    public ReplanningSettings Settings => _settings;

    /// <summary>
    /// Vrai si un evenement constate par le moteur attend d'etre pris en compte. Il ne se laisse pas
    /// mettre en attente comme une deduction : il a deja eu lieu.
    /// </summary>
    public bool HasReport => _reported is not null;

    /// <summary>Nombre de declencheurs retenus depuis le debut, par cause. Alimente le panneau debug.</summary>
    public IReadOnlyDictionary<ReplanCause, int> Counts => _counts;

    private readonly Dictionary<ReplanCause, int> _counts = [];

    /// <summary>Emis a chaque declencheur retenu, avant meme qu'un nouveau plan n'existe.</summary>
    public event Action<ReplanTrigger>? Detected;

    /// <summary>
    /// Signale un evenement constate par le moteur. Le premier recu tient la place jusqu'a ce qu'il
    /// soit consomme : une action qui echoue n'a pas besoin d'etre annoncee deux fois, et les
    /// evenements suivants seront de toute facon rejoues a l'inspection suivante.
    /// </summary>
    public void Report(ReplanCause cause, string detail, double at)
    {
        if (cause == ReplanCause.None) return;
        _reported ??= new ReplanTrigger(cause, detail, at);
    }

    /// <summary>Signale l'echec d'une action, avec la nature que l'executeur lui a donnee.</summary>
    public void Report(PlanningAction action, ActionFailure failure, double at)
    {
        var trigger = ReplanTrigger.FromFailure(action, failure, at);
        Report(trigger.Cause, trigger.Detail, at);
    }

    /// <summary>
    /// Vrai s'il est temps de regarder. Un evenement deja signale passe devant le cadencement : le
    /// reste attend <see cref="ReplanningSettings.CheckInterval"/>.
    /// </summary>
    public bool ShouldInspect(double now)
        => _reported is not null || now - _lastInspectionAt >= _settings.CheckInterval;

    /// <summary>
    /// Cherche une raison de replanifier. <paramref name="current"/> est l'etat symbolique reconcilie
    /// — ce que le plan a deja accompli, corrige par ce que l'agent vient d'observer.
    /// </summary>
    public ReplanTrigger? Inspect(UtilityContext context, Plan? plan, SymbolicState current)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(current);

        _lastInspectionAt = context.Now;

        return Publish(
            Consume()
            ?? NewThreat(context)
            ?? EndangeredAlly(context)
            ?? RisingDanger(context)
            ?? InvalidPlan(plan, current, context.Now));
    }

    /// <summary>Oublie ce qui a ete vu : un survivant hors de combat, ou une nouvelle partie.</summary>
    public void Reset()
    {
        _reported = null;
        _knownThreats.Clear();
        _endangeredAllies.Clear();
        _aboveDangerThreshold = false;
        _lastInspectionAt = double.NegativeInfinity;
    }

    // --- Detections -----------------------------------------------------------------------------

    private ReplanTrigger? Consume()
    {
        var reported = _reported;
        _reported = null;
        return reported;
    }

    /// <summary>
    /// « Un zombie apparait » : une menace credible dont l'agent n'avait pas connaissance a la
    /// derniere inspection. La comparaison porte sur les identifiants encore crus, donc un zombie
    /// oublie puis revu compte a nouveau — c'est bien une nouvelle.
    /// </summary>
    private ReplanTrigger? NewThreat(UtilityContext context)
    {
        var believed = context.Self.Knowledge
            .BelievedEntities(context.Now)
            .Where(belief => belief.Kind == EntityKind.Zombie)
            .Select(belief => belief.Id)
            .ToHashSet(StringComparer.Ordinal);

        string? spotted = believed
            .Where(id => !_knownThreats.Contains(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .FirstOrDefault();

        _knownThreats.Clear();
        _knownThreats.UnionWith(believed);

        return spotted is null
            ? null
            : new ReplanTrigger(ReplanCause.ZombieSpotted, $"{spotted} repere", context.Now);
    }

    /// <summary>
    /// « Un allie est attaque » : faute de croyance portant la sante d'un allie — elle arrive au
    /// livrable 7 — c'est le danger pesant sur sa position supposee qui en tient lieu.
    ///
    /// Depuis le livrable 15 le survivant n'a plus d'objectif de secours a declencher ; le
    /// declencheur survit quand meme, parce qu'il rouvre la decision. Ce qui en sort peut etre une
    /// distraction pour detourner la horde, une fuite, ou rien — mais le survivant aura regarde.
    /// </summary>
    private ReplanTrigger? EndangeredAlly(UtilityContext context)
    {
        string? attacked = null;
        var endangered = new HashSet<string>(StringComparer.Ordinal);

        foreach (var ally in context.Allies.OrderBy(ally => ally.Id, StringComparer.Ordinal))
        {
            if (context.DangerAt(ally.Value) < _settings.AllyDangerThreshold) continue;

            endangered.Add(ally.Id);
            if (!_endangeredAllies.Contains(ally.Id)) attacked ??= ally.Id;
        }

        _endangeredAllies.Clear();
        _endangeredAllies.UnionWith(endangered);

        return attacked is null
            ? null
            : new ReplanTrigger(ReplanCause.AllyAttacked, $"{attacked} est pris a partie", context.Now);
    }

    /// <summary>
    /// « Le niveau de danger depasse un seuil » : seul le franchissement compte, et il faut
    /// redescendre franchement sous le seuil pour qu'un suivant soit possible.
    /// </summary>
    private ReplanTrigger? RisingDanger(UtilityContext context)
    {
        float pressure = context.ThreatPressure;

        if (_aboveDangerThreshold)
        {
            if (pressure <= _settings.DangerThreshold - _settings.DangerHysteresis) _aboveDangerThreshold = false;
            return null;
        }

        if (pressure < _settings.DangerThreshold) return null;

        _aboveDangerThreshold = true;
        return new ReplanTrigger(
            ReplanCause.DangerThreshold,
            $"danger ressenti {pressure:0.00} au-dela de {_settings.DangerThreshold:0.00}",
            context.Now);
    }

    private ReplanTrigger? InvalidPlan(Plan? plan, SymbolicState current, double now)
    {
        if (!_settings.WatchPlanValidity) return null;
        if (plan is not { IsActive: true }) return null;

        return PlanValidity.Check(plan, current, now);
    }

    private ReplanTrigger? Publish(ReplanTrigger? trigger)
    {
        if (trigger is not { } found) return null;

        _counts[found.Cause] = _counts.GetValueOrDefault(found.Cause) + 1;
        Detected?.Invoke(found);
        return found;
    }
}

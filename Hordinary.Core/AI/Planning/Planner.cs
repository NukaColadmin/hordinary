using Hordinary.AI.Utility;
using Hordinary.Config;

namespace Hordinary.AI.Planning;

/// <summary>
/// Resultat d'une demande de planification : le plan retenu, la recherche qui l'a produit, et la
/// phrase qui l'explique — celle qui part dans le journal des decisions de l'IA.
/// </summary>
public readonly record struct PlanningResult(
    bool Found,
    Plan? Plan,
    GbfsResult Search,
    string Reason,
    bool UsedFallback)
{
    public override string ToString() => Reason;
}

/// <summary>
/// Transforme un objectif de la Utility AI en suite d'actions executables (specification, livrable 5).
///
/// Le planificateur ne connait aucun objectif ni aucune action par son nom : il lit le domaine
/// (<c>config/planner_domain.json</c>), construit un etat symbolique depuis la memoire du survivant,
/// et laisse <see cref="Gbfs"/> chercher. Ajouter <c>FightZombie</c> au livrable 8 sera une entree de
/// configuration, pas une classe.
///
/// Quand aucun plan n'existe, la specification impose un repli : « l'agent choisit un objectif
/// d'exploration, cherche une zone inconnue pertinente, collecte de nouvelles informations, relance la
/// planification ». Les deux premieres etapes sont ici (<see cref="PlannerSettings.ExplorationFallbackGoal"/>) ;
/// les deux dernieres sont la boucle de jeu, qui percoit puis redemande un plan.
/// </summary>
public sealed class Planner
{
    public Planner(PlanningDomain domain, PlannerSettings? settings = null)
    {
        Domain = domain ?? throw new ArgumentNullException(nameof(domain));
        Settings = settings ?? new PlannerSettings();
    }

    public PlanningDomain Domain { get; private set; }

    /// <summary>
    /// Remplace le domaine specialise, sans changer d'instance de planificateur : ce qu'un survivant
    /// prend en main modifie le prix de ses actions (livrable 11), et le moniteur de replanification
    /// qui tient ce planificateur doit continuer de le tenir.
    ///
    /// Le repertoire d'actions change, jamais la recherche : GBFS decouvre le nouveau plan tout seul.
    /// </summary>
    public void Respecialize(PlanningDomain domain)
        => Domain = domain ?? throw new ArgumentNullException(nameof(domain));

    public PlannerSettings Settings { get; }

    /// <summary>Nombre de recherches lancees, repli compris. Alimente le mode debug.</summary>
    public int SearchCount { get; private set; }

    public PlanningResult? Last { get; private set; }

    /// <summary>
    /// Les recherches qui n'ont donne aucun plan, ou qui ont du se rabattre sur l'exploration
    /// (specification, livrable 8 : le mode debug doit afficher « les plans rejetes » et « la raison
    /// d'un echec de planification »).
    ///
    /// GBFS ne produit pas de plans concurrents entre lesquels arbitrer : un plan rejete, ici, est
    /// une recherche que le survivant a lancee et dont il n'a rien pu faire. C'est la seule notion de
    /// rejet que l'algorithme connaisse, et c'est celle qui explique un comportement — un survivant
    /// qui explore alors qu'on lui a demande les medicaments a une raison, et elle est ecrite la.
    /// </summary>
    public IReadOnlyList<PlanningResult> Rejected => _rejected;

    private readonly List<PlanningResult> _rejected = [];

    /// <summary>Au-dela, les plus anciens rejets sont oublies : c'est un affichage, pas un audit.</summary>
    private const int RejectedCapacity = 20;

    /// <summary>Emis a chaque planification, reussie ou non : c'est la source du journal.</summary>
    public event Action<PlanningResult>? Planned;

    /// <summary>
    /// Cherche un plan pour un objectif de la Utility AI. En cas d'echec, retente une fois avec
    /// l'objectif d'exploration configure.
    /// </summary>
    /// <param name="carried">
    /// Acquis du plan precedent a reconduire (voir <see cref="SymbolicStateBuilder.Carry"/>). Sans
    /// eux, replanifier revient a tout recommencer : la piece qu'on venait de fouiller redevient a
    /// fouiller.
    /// </param>
    public PlanningResult PlanFor(
        string? goalName,
        UtilityContext context,
        double now = 0d,
        IEnumerable<string>? carried = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var acquired = carried?.ToList();

        var attempt = TrySolve(goalName, context, now, isFallback: false, acquired);
        if (attempt.Found) return Publish(attempt);

        string fallbackName = Settings.ExplorationFallbackGoal;

        bool fallbackPossible = !string.IsNullOrWhiteSpace(fallbackName)
            && !fallbackName.Equals(goalName, StringComparison.OrdinalIgnoreCase)
            && Domain.FindGoal(fallbackName) is not null;

        if (!fallbackPossible) return Publish(attempt);

        var fallback = TrySolve(fallbackName, context, now, isFallback: true, acquired);

        string reason = fallback.Found
            ? $"{Describe(goalName)} sans solution ({attempt.Search.Reason}) : "
            + $"repli sur {fallbackName} — {fallback.Search.Describe()}"
            : $"{Describe(goalName)} sans solution ({attempt.Search.Reason}) ; "
            + $"le repli {fallbackName} echoue aussi ({fallback.Search.Reason})";

        return Publish(fallback with { Reason = reason });
    }

    /// <summary>
    /// Resout un probleme deja construit, sans repli. C'est le point d'entree des tests et, au
    /// livrable 6, de la replanification sur un etat corrige.
    /// </summary>
    public PlanningResult Solve(PlanningProblem problem, double now = 0d, bool isFallback = false)
    {
        ArgumentNullException.ThrowIfNull(problem);

        SearchCount++;
        var search = new Gbfs(problem, Settings).Search();

        if (!search.Found)
        {
            return new PlanningResult(
                false, null, search, $"{problem.GoalName} : {search.Describe()}", isFallback);
        }

        var plan = new Plan(problem, search.Actions, now, isFallback) { TotalCost = search.Cost };
        return new PlanningResult(true, plan, search, $"{plan.Describe()} — {search.Describe()}", isFallback);
    }

    private PlanningResult TrySolve(
        string? goalName,
        UtilityContext context,
        double now,
        bool isFallback,
        IReadOnlyList<string>? carried)
    {
        if (Domain.FindGoal(goalName) is not { } goal)
        {
            var unknown = GbfsResult.Fail(PlanningFailure.UnknownGoal, 0, 0, 0d);
            return new PlanningResult(
                false,
                null,
                unknown,
                $"{Describe(goalName)} n'est traduit en conditions symboliques dans aucun domaine charge",
                isFallback);
        }

        var problem = SymbolicStateBuilder.Build(context, Domain, goal, Settings, carried);
        return Solve(problem, now, isFallback);
    }

    private PlanningResult Publish(PlanningResult result)
    {
        Last = result;

        // Un repli est un rejet qui a trouve une consolation : l'objectif demande n'a pas de plan,
        // et c'est cela qui merite d'etre visible, pas seulement l'exploration qui en resulte.
        if (!result.Found || result.UsedFallback)
        {
            _rejected.Add(result);
            if (_rejected.Count > RejectedCapacity) _rejected.RemoveAt(0);
        }

        Planned?.Invoke(result);
        return result;
    }

    private static string Describe(string? goalName) => goalName is null ? "Aucun objectif" : goalName;
}

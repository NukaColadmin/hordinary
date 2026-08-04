using Hordinary.Config;

namespace Hordinary.AI.Planning;

/// <summary>
/// Detail d'une estimation, pour le panneau debug : c'est ce qui explique pourquoi GBFS a prefere
/// forcer une porte plutot que briser une vitre.
/// </summary>
public readonly record struct HeuristicBreakdown(
    int Unsatisfied,
    float Distance,
    float Danger,
    float Noise,
    float Total)
{
    public override string ToString()
        => $"h = {Total:0.00} ({Unsatisfied} conditions, distance {Distance:0.00}, "
         + $"danger {Danger:0.00}, bruit {Noise:0.00})";
}

/// <summary>
/// Heuristique de la recherche (specification, livrable 5).
///
/// Version simple : <c>heuristic = number_of_unsatisfied_goal_conditions</c>.
/// Version enrichie : on y ajoute la distance restante, le danger du lieu vise et le bruit deja
/// produit — de quoi preferer, a nombre d'etapes egal, le plan discret et sur.
///
/// Les quatre poids viennent de <see cref="PlannerSettings"/> : les mettre a zero sauf le premier
/// redonne exactement la version simple, sans toucher au code.
/// </summary>
public sealed class Heuristic(PlanningProblem problem, PlannerSettings? settings = null)
{
    private readonly PlanningProblem _problem = problem ?? throw new ArgumentNullException(nameof(problem));
    private readonly PlannerSettings _settings = settings ?? new PlannerSettings();

    public PlannerSettings Settings => _settings;

    /// <summary>Estimation utilisee comme priorite du noeud. Plus elle est basse, plus le noeud est prometteur.</summary>
    public float Estimate(SymbolicState state, float accumulatedNoise) => Explain(state, accumulatedNoise).Total;

    public HeuristicBreakdown Explain(SymbolicState state, float accumulatedNoise)
    {
        int unsatisfied = state.CountUnsatisfied(_problem.Goal);

        // Un etat qui satisfait le but ne doit rien couter d'autre : sinon la recherche pourrait
        // preferer un noeud non final au noeud final, et tourner pour rien.
        if (unsatisfied == 0) return new HeuristicBreakdown(0, 0f, 0f, 0f, 0f);

        float distance = DistanceTerm(state);
        float danger = _problem.DangerOf(_problem.AgentLocationIn(state));
        float noise = _settings.NoiseScale <= 0f ? 0f : Math.Clamp(accumulatedNoise / _settings.NoiseScale, 0f, 1f);

        float total = _settings.UnsatisfiedWeight * unsatisfied
                    + _settings.DistanceWeight * distance
                    + _settings.DangerWeight * danger
                    + _settings.NoiseWeight * noise;

        return new HeuristicBreakdown(unsatisfied, distance, danger, noise, total);
    }

    /// <summary>
    /// Distance restante jusqu'au lieu du but le plus proche, ramenee entre 0 et 1. Un but qui ne cite
    /// aucun lieu (« avoir les medicaments ») n'a pas de distance : le terme s'annule et l'heuristique
    /// se reduit au comptage des conditions.
    /// </summary>
    private float DistanceTerm(SymbolicState state)
    {
        if (_problem.GoalPositions.Count == 0 || _settings.DistanceScale <= 0f) return 0f;

        var from = _problem.AgentPositionIn(state);
        int nearest = _problem.GoalPositions.Min(cell => from.ChebyshevDistanceTo(cell));

        return Math.Clamp(nearest / _settings.DistanceScale, 0f, 1f);
    }
}

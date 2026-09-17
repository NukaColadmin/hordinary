using System.Text.Json;
using Hordinary.Config;

namespace Hordinary.AI.Utility;

/// <summary>
/// Catalogue des objectifs, charge depuis <c>config/utility_goals.json</c>.
///
/// C'est la seule definition des objectifs du jeu : le code n'en connait aucun par son nom. Ajouter
/// « BarricadeDoor », retirer « Hide » le temps d'un test ou repondérer « FleeDanger » est une
/// modification de ce fichier, jamais une recompilation.
/// </summary>
public sealed class UtilityGoalSet
{
    public List<UtilityGoal> Goals { get; init; } = [];

    public static UtilityGoalSet Empty { get; } = new();

    /// <summary>Objectifs reellement en jeu, dans l'ordre du fichier.</summary>
    public IEnumerable<UtilityGoal> Active => Goals.Where(goal => goal.Enabled);

    public UtilityGoal? Find(string? name)
        => name is null ? null : Goals.FirstOrDefault(goal => goal.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Lit le catalogue et le valide immediatement : une consideration mal orthographiee doit se
    /// voir au chargement, avec la liste du vocabulaire disponible, et non se traduire en objectif
    /// silencieusement inerte.
    /// </summary>
    public static UtilityGoalSet FromJson(string json)
    {
        var set = JsonSerializer.Deserialize<UtilityGoalSet>(json, GameConfig.JsonOptions) ?? Empty;
        set.Validate();
        return set;
    }

    public void Validate()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var goal in Goals)
        {
            if (string.IsNullOrWhiteSpace(goal.Name))
                throw new InvalidOperationException("Un objectif sans nom a ete declare.");

            if (!seen.Add(goal.Name))
                throw new InvalidOperationException($"L'objectif « {goal.Name} » est declare deux fois.");

            if (goal.Considerations.Count == 0)
                throw new InvalidOperationException($"L'objectif « {goal.Name} » n'a aucune consideration.");

            if (goal.Target is { } target && !UtilityContext.TargetNames.Contains(target))
            {
                throw new InvalidOperationException(
                    $"L'objectif « {goal.Name} » vise « {target} », inconnue. "
                    + $"Cibles connues : {string.Join(", ", UtilityContext.TargetNames.Order())}.");
            }

            foreach (var input in goal.Considerations.Select(c => c.Input)
                         .Concat(goal.Preconditions.Select(p => p.Input)))
            {
                if (UtilityInputs.IsKnown(input)) continue;

                throw new InvalidOperationException(
                    $"L'objectif « {goal.Name} » utilise la consideration inconnue « {input} ». "
                    + UtilityInputs.Vocabulary());
            }
        }
    }
}

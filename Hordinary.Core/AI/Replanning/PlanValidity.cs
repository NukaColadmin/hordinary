using Hordinary.AI.Planning;

namespace Hordinary.AI.Replanning;

/// <summary>
/// Repond a une seule question : le plan tient-il encore, sachant ce que l'agent sait maintenant ?
/// (specification, livrable 6 : « Invalider un plan ».)
///
/// La verification rejoue les actions restantes a partir de l'etat courant. Ce n'est pas la meme
/// chose que ce que fait <see cref="PlanExecutor"/>, qui verifie les preconditions de l'action
/// <b>au moment</b> de la commencer : ici on regarde plus loin que le pas suivant. Un survivant qui
/// apercoit de la rue que la porte de la pharmacie est verrouillee n'a aucune raison de finir son
/// trajet avant de s'en apercevoir — c'est ce qui fait la difference entre reagir et subir.
///
/// La classe ne connait ni le monde ni la memoire : on lui donne un etat, elle le confronte au plan.
/// C'est ce qui la rend testable sans carte et ce qui garantit qu'aucune verification ne triche en
/// consultant la verite du monde.
/// </summary>
public static class PlanValidity
{
    /// <summary>
    /// Predicat qui, lorsqu'il porte sur un objet cite par la precondition en defaut, designe la
    /// cause nommee par la specification : « une porte est verrouillee ». Il ne sert qu'a etiqueter
    /// le journal — le plan est invalide de la meme facon avec ou sans cette lecture.
    /// </summary>
    private const string LockedPredicate = "door_locked";

    /// <summary>
    /// Familles de predicats qui affinent le libelle d'une precondition en defaut. Meme remarque :
    /// seul le libelle en depend.
    /// </summary>
    private static readonly Dictionary<string, ReplanCause> CauseByPredicate = new(StringComparer.OrdinalIgnoreCase)
    {
        ["medicine_found"] = ReplanCause.ItemMissing,
        ["item_at"] = ReplanCause.ItemMissing,
        ["has"] = ReplanCause.ItemMissing,
        ["known"] = ReplanCause.ObstacleDiscovered,
        ["accessible"] = ReplanCause.ObstacleDiscovered,
    };

    /// <summary>
    /// Verifie les actions non encore executees. Renvoie le premier declencheur trouve, ou
    /// <c>null</c> si le plan mene toujours au but.
    /// </summary>
    public static ReplanTrigger? Check(Plan plan, SymbolicState current, double now)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(current);

        var state = current;

        for (int i = plan.CurrentActionIndex; i < plan.Actions.Count; i++)
        {
            var action = plan.Actions[i];

            foreach (var condition in action.Preconditions)
            {
                if (state.Satisfies(condition)) continue;

                return new ReplanTrigger(
                    CauseOf(condition, state),
                    $"{action.Name} : « {condition} » n'est plus vrai",
                    now);
            }

            state = action.ApplyTo(state);
        }

        // Les actions restantes s'enchainent, mais leur resultat ne satisfait plus le but : le monde
        // a defait ce que le plan tenait pour acquis avant meme d'avoir commence.
        if (plan.Problem.IsSatisfiedBy(state)) return null;

        return new ReplanTrigger(
            ReplanCause.PreconditionFalse,
            $"le plan {plan.Goal} ne mene plus au but",
            now);
    }

    /// <summary>
    /// De quelle sorte est la precondition en defaut. La porte verrouillee se reconnait a ce que
    /// l'etat affirme des objets en cause, pas au nom de la precondition : c'est <c>accessible</c>
    /// ou <c>door_closed</c> qui devient faux quand une porte se revele fermee a clef, et c'est bien
    /// de verrouillage qu'il faut parler dans le journal.
    /// </summary>
    private static ReplanCause CauseOf(Condition condition, SymbolicState state)
    {
        if (Fact.PredicateOf(condition.Pattern).Equals(LockedPredicate, StringComparison.OrdinalIgnoreCase)
            && !condition.Expected)
        {
            return ReplanCause.DoorLocked;
        }

        foreach (var argument in Fact.ArgumentsOf(condition.Pattern))
        {
            if (IsLocked(argument, state) || IsGuardedByLocked(argument, state)) return ReplanCause.DoorLocked;
        }

        return CauseByPredicate.GetValueOrDefault(Fact.PredicateOf(condition.Pattern), ReplanCause.PreconditionFalse);
    }

    private static bool IsLocked(string name, SymbolicState state) => state.Holds($"{LockedPredicate}({name})");

    /// <summary>
    /// Un lieu n'est jamais verrouille ; ce qui le garde peut l'etre. L'etat le dit par un fait
    /// binaire reliant l'ouverture au lieu — <c>door_of(door_6_m2, pharmacy)</c> dans le domaine
    /// livre. La lecture ne suppose pas le nom du predicat, seulement sa forme : c'est ce qui evite
    /// qu'un domaine rebaptise fasse mentir le journal.
    /// </summary>
    private static bool IsGuardedByLocked(string place, SymbolicState state)
    {
        foreach (var fact in state.Facts)
        {
            var arguments = Fact.ArgumentsOf(fact);

            if (arguments.Length != 2) continue;
            if (!arguments[1].Equals(place, StringComparison.OrdinalIgnoreCase)) continue;
            if (IsLocked(arguments[0], state)) return true;
        }

        return false;
    }
}

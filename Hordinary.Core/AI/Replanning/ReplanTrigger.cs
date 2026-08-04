using Hordinary.AI.Planning;

namespace Hordinary.AI.Replanning;

/// <summary>
/// Un evenement qui remet un plan en cause : sa nature, ce qu'on peut en dire de precis, et quand.
///
/// <see cref="Detail"/> nomme les objets en jeu — la porte, l'action, le zombie — la ou
/// <see cref="ReplanCause"/> ne donne que la categorie. Les deux sont necessaires : le journal des
/// decisions doit pouvoir dire « la porte door_6_m2 est verrouillee » et le panneau debug compter les
/// replanifications par cause.
/// </summary>
public readonly record struct ReplanTrigger(ReplanCause Cause, string Detail, double At)
{
    /// <summary>Phrase du journal : le detail, suivi de la categorie entre parentheses.</summary>
    public string Reason => string.IsNullOrWhiteSpace(Detail)
        ? ReplanCauses.Label(Cause)
        : $"{Detail} ({ReplanCauses.Label(Cause)})";

    public override string ToString() => Reason;

    /// <summary>
    /// Traduit un echec d'action en declencheur. La specification distingue quatre evenements que
    /// l'executeur sait deja separer (<see cref="ActionFailureKind"/>) : les confondre reviendrait a
    /// journaliser « une action a echoue » quand la vraie nouvelle est qu'un mur barre la rue.
    /// </summary>
    public static ReplanTrigger FromFailure(PlanningAction action, ActionFailure failure, double at)
    {
        var cause = failure.Kind switch
        {
            ActionFailureKind.Precondition => ReplanCause.PreconditionFalse,
            ActionFailureKind.RouteBlocked => ReplanCause.ObstacleDiscovered,
            ActionFailureKind.NoRoute or ActionFailureKind.UnknownDestination => ReplanCause.PathImpossible,
            _ => ReplanCause.ActionFailed,
        };

        return new ReplanTrigger(cause, $"{action.Name} : {failure.Reason}", at);
    }
}

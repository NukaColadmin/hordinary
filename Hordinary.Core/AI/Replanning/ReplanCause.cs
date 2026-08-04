namespace Hordinary.AI.Replanning;

/// <summary>
/// Pourquoi un plan cesse d'etre valable (specification, livrable 6 : « Evenements declencheurs »).
///
/// Les dix valeurs sont exactement les dix evenements de la liste, dans le meme ordre. Ce n'est pas
/// une commodite d'affichage : c'est ce qui rend verifiable qu'aucun declencheur n'a ete oublie, et
/// ce qui donne au journal des decisions une raison nommee plutot qu'un message reconstitue.
/// </summary>
public enum ReplanCause
{
    /// <summary>Aucune : la planification n'est pas une replanification.</summary>
    None = 0,

    /// <summary>Une precondition d'une action a venir est devenue fausse.</summary>
    PreconditionFalse,

    /// <summary>Une action a echoue a l'execution.</summary>
    ActionFailed,

    /// <summary>Une porte que le plan comptait franchir est verrouillee.</summary>
    DoorLocked,

    /// <summary>Un obstacle a ete decouvert sur l'itineraire.</summary>
    ObstacleDiscovered,

    /// <summary>Un zombie jusqu'ici inconnu vient d'etre repere.</summary>
    ZombieSpotted,

    /// <summary>Un allie est menace.</summary>
    AllyAttacked,

    /// <summary>La Utility AI a change d'objectif.</summary>
    GoalChanged,

    /// <summary>Un objet que le plan attendait quelque part n'y est pas.</summary>
    ItemMissing,

    /// <summary>A* ne trouve aucun chemin vers la destination de l'action en cours.</summary>
    PathImpossible,

    /// <summary>Le niveau de danger ressenti a depasse le seuil.</summary>
    DangerThreshold,
}

/// <summary>Libelles des causes, tels qu'ils apparaissent dans le journal des decisions de l'IA.</summary>
public static class ReplanCauses
{
    public static string Label(ReplanCause cause) => cause switch
    {
        ReplanCause.PreconditionFalse => "une precondition est devenue fausse",
        ReplanCause.ActionFailed => "une action a echoue",
        ReplanCause.DoorLocked => "une porte est verrouillee",
        ReplanCause.ObstacleDiscovered => "un obstacle a ete decouvert",
        ReplanCause.ZombieSpotted => "un zombie est apparu",
        ReplanCause.AllyAttacked => "un allie est en danger",
        ReplanCause.GoalChanged => "la Utility AI a change d'objectif",
        ReplanCause.ItemMissing => "un objet attendu est absent",
        ReplanCause.PathImpossible => "le chemin A* est impossible",
        ReplanCause.DangerThreshold => "le niveau de danger depasse le seuil",
        _ => "aucune cause",
    };
}

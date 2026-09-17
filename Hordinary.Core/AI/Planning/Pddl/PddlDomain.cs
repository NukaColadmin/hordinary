namespace Hordinary.AI.Planning.Pddl;

/// <summary>Une fonction numerique : <c>(total-cost)</c>, <c>(distance ?from ?to - location)</c>.</summary>
public sealed record PddlFunction(string Name, IReadOnlyList<PddlParameter> Parameters)
{
    public string Canonical
        => Parameters.Count == 0 ? $"({Name})" : $"({Name} {string.Join(' ', Parameters)})";

    public override string ToString() => Canonical;
}

/// <summary>
/// Le domaine de planification sous forme PDDL : types, constantes, predicats, fonctions, schemas
/// d'action. C'est la representation intermediaire de la spec G — construite depuis
/// <c>config/planner_domain.json</c>, rendue soit en texte pour un planificateur tiers
/// (<see cref="PddlWriter"/>), soit en <see cref="PlanningDomain"/> pour GBFS
/// (<see cref="PlanningDomainRenderer"/>).
///
/// <para>
/// Les deux rendus sont deux vues du <b>meme</b> objet, et c'est la seule propriete qui compte ici :
/// il n'y a pas deux compilateurs a maintenir d'accord, donc pas de derive possible entre ce qu'un
/// planificateur tiers verifie et ce que le jeu execute.
/// </para>
/// </summary>
public sealed class PddlDomain
{
    public required string Name { get; init; }

    public IReadOnlyList<string> Requirements { get; init; } = [];

    public IReadOnlyList<string> Types { get; init; } = [];

    /// <summary>Constantes du domaine : <c>agent</c>, <c>objective</c>, <c>remedy</c>, <c>toolbox</c>.</summary>
    public IReadOnlyList<PddlParameter> Constants { get; init; } = [];

    public IReadOnlyList<PddlPredicate> Predicates { get; init; } = [];

    public IReadOnlyList<PddlFunction> Functions { get; init; } = [];

    public IReadOnlyList<PddlAction> Actions { get; init; } = [];

    /// <summary>
    /// Le bloc <c>(:constraints …)</c>, vide sauf pour la forme declarative rendue au portillon 4 et a
    /// VAL. Un domaine soumis a un planificateur n'en porte jamais : le compilateur du livrable 15
    /// rend un domaine dans le meme fragment que celui de la spec G, <c>:adl</c> + <c>:action-costs</c>.
    /// </summary>
    public IReadOnlyList<PddlConstraint> Constraints { get; init; } = [];

    /// <summary>
    /// Les buts symboliques de <c>config/planner_domain.json</c>. En PDDL ils appartiennent au
    /// <b>probleme</b>, pas au domaine ; ils voyagent ici parce que le fichier du jeu les declare au
    /// meme endroit et que le rendu vers GBFS en a besoin. Ils ne sont jamais ecrits dans un
    /// <c>domain.pddl</c>.
    /// </summary>
    public IReadOnlyList<PlanningGoal> Goals { get; init; } = [];

    /// <summary>
    /// Categories de lieux nommables — <c>building</c>, <c>street</c>, <c>alley</c>… Chacune est un
    /// predicat unaire sur <c>location</c>, pose par le moteur et par aucune action : c'est la classe
    /// de predicats que la spec G documente comme « posee par le runtime ». C'est aussi le vocabulaire
    /// des classes de schemas de la grammaire du livrable 15 (<c>enter:building</c>).
    /// </summary>
    public IReadOnlyList<string> LocationKinds { get; init; } = [];

    /// <summary>
    /// Reperes que le moteur recalcule a chaque decision — <c>group</c>, <c>cover</c>,
    /// <c>frontier</c>, <c>away_from_danger</c>, <c>mission</c>. Ce sont des objets du probleme, pas
    /// des constantes du domaine : leur valeur depend de l'instant. Ils sont listes pour que la
    /// grammaire sache les reconnaitre — « regroupez-vous avant d'entrer » nomme <c>group</c>, qui
    /// n'apparait dans aucune action.
    /// </summary>
    public IReadOnlyList<string> Landmarks { get; init; } = [];

    public PddlAction? FindAction(string? name)
        => name is null ? null : Actions.FirstOrDefault(action => action.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public PddlPredicate? FindPredicate(string? name)
        => name is null ? null : Predicates.FirstOrDefault(predicate => predicate.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public bool IsLocationKind(string? name)
        => name is not null && LocationKinds.Any(kind => kind.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Le meme domaine, d'autres actions — ce que rend le compilateur de contraintes.</summary>
    public PddlDomain With(
        IReadOnlyList<PddlAction>? actions = null,
        IReadOnlyList<PddlConstraint>? constraints = null)
        => new()
        {
            Name = Name,
            Requirements = Requirements,
            Types = Types,
            Constants = Constants,
            Predicates = Predicates,
            Functions = Functions,
            Actions = actions ?? Actions,
            Constraints = constraints ?? Constraints,
            Goals = Goals,
            LocationKinds = LocationKinds,
            Landmarks = Landmarks,
        };

    public override string ToString() => $"{Name} — {Actions.Count} actions, {Predicates.Count} predicats";
}

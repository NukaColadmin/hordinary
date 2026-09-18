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

    /// <summary>
    /// Les symboles qu'un fait peut nommer, avec leur type : les constantes declarees et les reperes.
    ///
    /// <para>
    /// Les reperes sont des lieux par construction — <see cref="PddlDomainBuilder"/> ne verse dans
    /// <see cref="Landmarks"/> que des symboles dont le typage deduit dit <c>location</c>, plus ceux
    /// que <c>places.landmarks</c> declare, qui sont des lieux par definition.
    /// </para>
    ///
    /// <para>
    /// C'est le vocabulaire du portillon 1 <b>et</b> celui du digest envoye au service : les deux le
    /// lisent ici, ils ne le recopient pas. Une liste tenue a deux endroits aurait fini par accepter
    /// d'un cote ce qu'elle ne montre pas de l'autre, et le symptome aurait ete un refus
    /// incomprehensible pour le joueur.
    /// </para>
    /// </summary>
    public IEnumerable<PddlParameter> Vocabulary
        => Constants
            .Concat(Landmarks.Select(landmark => new PddlParameter(landmark, PlanningObjectTypes.Location)))
            .DistinctBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(entry => entry.Name, StringComparer.Ordinal);

    /// <summary>Le type declare d'un symbole nommable, ou <c>null</c> s'il n'appartient pas au domaine.</summary>
    public string? TypeOfObject(string? name)
        => name is null
            ? null
            : Vocabulary.FirstOrDefault(entry => entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { Name: not null } found
                ? found.Type
                : null;

    /// <summary>
    /// Le type <paramref name="actual"/> est-il recevable la ou le domaine attend
    /// <paramref name="expected"/> ?
    ///
    /// <para>
    /// La hierarchie de types du domaine est plate — <c>(:types ally door heavy item location threat
    /// window - object)</c> — donc la reponse tient en deux cas : une position typee <c>object</c>
    /// accepte tout, une position typee autrement n'accepte que son type exact. Rien de plus n'est
    /// necessaire ici, et rien de moins ne suffirait : <c>agent</c> est de type <c>object</c>, donc
    /// <c>equipped(agent)</c> — dont la position est typee <c>item</c> — est mal type, alors que
    /// <c>agent</c> est bien un symbole du domaine.
    /// </para>
    /// </summary>
    public static bool TypeFits(string actual, string expected)
        => expected.Equals(PddlDomainBuilder.RootType, StringComparison.OrdinalIgnoreCase)
            || actual.Equals(expected, StringComparison.OrdinalIgnoreCase);

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

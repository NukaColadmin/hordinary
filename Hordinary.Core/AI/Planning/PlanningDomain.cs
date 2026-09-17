using System.Text.Json;
using Hordinary.Agents;
using Hordinary.Config;

namespace Hordinary.AI.Planning;

/// <summary>
/// Traduction d'un objectif de la Utility AI en conditions symboliques a satisfaire.
///
/// Le planificateur ne connait aucun objectif par son nom : <c>PickUpObjective</c> n'est pas une
/// methode, c'est une entree de <c>config/planner_domain.json</c> qui dit « cet objectif est atteint
/// quand <c>has(agent, objective)</c> est vrai ». Le nom fait le lien avec
/// <c>config/utility_goals.json</c> (livrable 4).
/// </summary>
public sealed class PlanningGoal
{
    private List<Condition>? _conditions;

    public string Name { get; init; } = string.Empty;

    /// <summary>Conditions a satisfaire, ecrites comme des preconditions (negation avec <c>!</c>).</summary>
    public List<string> Conditions { get; init; } = [];

    public bool Enabled { get; init; } = true;

    public IReadOnlyList<Condition> ParsedConditions => _conditions ??= [.. Conditions.Select(Condition.Parse)];

    public override string ToString() => $"{Name} : {string.Join(" et ", ParsedConditions)}";
}

/// <summary>
/// Les lieux que le domaine nomme sans qu'aucune action ne les pose (section <c>places</c> de
/// <c>config/planner_domain.json</c>).
///
/// Deux listes, deux natures. <see cref="Kinds"/> sont des <b>categories</b> — batiment, rue, ruelle
/// — qui deviennent des predicats unaires sur les lieux ; <see cref="Landmarks"/> sont des
/// <b>reperes</b> recalcules a chaque decision — le barycentre du groupe, une cachette, une frontiere
/// d'exploration — qui sont des objets du probleme et non des constantes du domaine.
///
/// Elles sont en configuration et non dans le code parce qu'elles sont exactement ce qu'une
/// instruction du joueur a le droit de nommer : « regroupez-vous avant d'entrer dans un batiment »
/// cite <c>group</c> et <c>building</c>, dont ni l'un ni l'autre n'apparait dans une action.
/// </summary>
public sealed class PlanningPlaces
{
    public List<string> Kinds { get; init; } = [];

    public List<string> Landmarks { get; init; } = [];
}

/// <summary>
/// Le domaine de planification, charge depuis <c>config/planner_domain.json</c> : les actions
/// disponibles et la traduction des objectifs en conditions symboliques.
///
/// C'est la seule definition des actions du jeu. Comme <see cref="AI.Utility.UtilityGoalSet"/> pour
/// les objectifs, le fichier est valide des le chargement : un verbe mal orthographie ou une variable
/// non declaree doit se voir immediatement, avec le vocabulaire disponible, et non se traduire en
/// action silencieusement inapplicable.
/// </summary>
public sealed class PlanningDomain
{
    public List<ActionSchema> Actions { get; init; } = [];

    public List<PlanningGoal> Goals { get; init; } = [];

    /// <summary>
    /// Le vocabulaire de lieux que le moteur pose lui-meme et qu'aucune action ne declare, section
    /// <c>places</c> du fichier. Il ne change rien a la recherche : il sert a l'export PDDL, qui doit
    /// declarer ces predicats, et a la grammaire des instructions tactiques (livrable 15), qui refuse
    /// tout symbole absent du domaine.
    /// </summary>
    public PlanningPlaces Places { get; init; } = new();

    public static PlanningDomain Empty { get; } = new();

    /// <summary>Actions reellement en jeu, dans l'ordre du fichier.</summary>
    public IEnumerable<ActionSchema> Active => Actions.Where(action => action.Enabled);

    /// <summary>
    /// Predicats dont un objectif fait sa condition de reussite : <c>explored</c>, <c>hidden</c>,
    /// <c>extracted</c>…
    ///
    /// Ce ne sont pas des faits comme les autres. Ils marquent qu'un but a ete atteint, et un but
    /// atteint une fois ne l'est pas pour toujours : reconduire <c>explored</c> d'un plan au suivant
    /// rendrait « explorer » satisfait des la premiere sortie, et le survivant ne bougerait plus.
    /// C'est ce qui distingue un marqueur de but d'un acquis durable comme <c>searched(pharmacy)</c>.
    /// </summary>
    public IReadOnlySet<string> GoalPredicates => _goalPredicates ??= CollectGoalPredicates();

    private HashSet<string>? _goalPredicates;

    private HashSet<string> CollectGoalPredicates()
    {
        var predicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var goal in Goals)
        {
            foreach (var condition in goal.ParsedConditions) predicates.Add(Fact.PredicateOf(condition.Pattern));
        }

        return predicates;
    }

    public PlanningGoal? FindGoal(string? name)
        => name is null
            ? null
            : Goals.FirstOrDefault(goal => goal.Enabled && goal.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public ActionSchema? FindAction(string? name)
        => name is null ? null : Actions.FirstOrDefault(action => action.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static PlanningDomain FromJson(string json)
    {
        var domain = JsonSerializer.Deserialize<PlanningDomain>(json, GameConfig.JsonOptions) ?? Empty;
        domain.Validate();
        return domain;
    }

    /// <summary>Decline toutes les actions actives sur les objets connus de l'agent.</summary>
    public IReadOnlyList<PlanningAction> Ground(IReadOnlyList<PlanningObject> objects)
        => [.. Active.SelectMany(schema => schema.Ground(objects))];

    /// <summary>
    /// Le domaine tel qu'un survivant donne le pratique (livrable 7 : « actions speciales » d'Alice,
    /// Bob et Claire).
    ///
    /// Les trois profils partagent le meme repertoire d'actions — personne ne connait un verbe secret
    /// — mais pas les memes prix. Forcer une porte coute moitie moins a Bob : GBFS lui trouvera donc
    /// spontanement le plan qui passe par la porte, la ou Alice preferera la fenetre ou le detour.
    /// La difference de comportement sort de la recherche, elle n'y est pas ecrite.
    ///
    /// Le domaine d'origine n'est jamais modifie : chaque survivant recoit sa copie, et un profil
    /// sans competence recoit le domaine commun tel quel.
    /// </summary>
    public PlanningDomain Specialize(IReadOnlyDictionary<string, ActionModifier>? skills)
    {
        if (skills is null || skills.Count == 0) return this;

        return new PlanningDomain
        {
            Actions =
            [
                .. Actions.Select(action => skills.TryGetValue(action.Name, out var skill)
                    ? action.Specialize(skill)
                    : action),
            ],
            Goals = Goals,
            Places = Places,
        };
    }

    public void Validate()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var action in Actions)
        {
            if (string.IsNullOrWhiteSpace(action.Name))
                throw new InvalidOperationException("Une action sans nom a ete declaree.");

            if (!seen.Add(action.Name))
                throw new InvalidOperationException($"L'action « {action.Name} » est declaree deux fois.");

            ValidateAction(action);
        }

        ValidateGoals();
    }

    private static void ValidateAction(ActionSchema action)
    {
        if (!ActionVerbs.TryParse(action.Verb, out var verb))
        {
            throw new InvalidOperationException(
                $"L'action « {action.Name} » utilise le verbe inconnu « {action.Verb} ». {ActionVerbs.Vocabulary()}");
        }

        foreach (var required in ActionVerbs.RequiredParameters(verb))
        {
            if (!action.Params.ContainsKey(required))
            {
                throw new InvalidOperationException(
                    $"L'action « {action.Name} » ({action.Verb}) doit fournir le parametre « {required} ». "
                    + $"Parametres attendus : {string.Join(", ", ActionVerbs.RequiredParameters(verb))}.");
            }
        }

        if (action.Effects.Count == 0)
            throw new InvalidOperationException($"L'action « {action.Name} » n'a aucun effet : elle ne peut faire avancer aucun plan.");

        if (action.Duration < 0f || action.Cost < 0f || action.NoiseLevel < 0f)
            throw new InvalidOperationException($"L'action « {action.Name} » a un cout, un bruit ou une duree negatif.");

        // Les preconditions et effets sont analyses ici pour que les erreurs de syntaxe remontent au
        // chargement du fichier, et non au milieu d'une recherche.
        var used = action.ParsedPreconditions.SelectMany(condition => condition.Variables)
            .Concat(action.ParsedEffects.SelectMany(effect => effect.Variables))
            .Concat(action.Params.Values.SelectMany(VariablesIn));

        foreach (var variable in used)
        {
            if (action.Parameters.ContainsKey(variable)) continue;

            throw new InvalidOperationException(
                $"L'action « {action.Name} » utilise la variable « ?{variable} » sans la declarer. "
                + $"Ajouter \"parameters\": {{ \"{variable}\": \"<type>\" }} "
                + $"(types connus : {string.Join(", ", PlanningObjectTypes.Names.Order())}).");
        }

        foreach (var (name, type) in action.Parameters)
        {
            if (PlanningObjectTypes.Names.Contains(type)) continue;

            throw new InvalidOperationException(
                $"L'action « {action.Name} » declare le parametre « {name} » de type inconnu « {type} ». "
                + $"Types connus : {string.Join(", ", PlanningObjectTypes.Names.Order())}.");
        }
    }

    private void ValidateGoals()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var goal in Goals)
        {
            if (string.IsNullOrWhiteSpace(goal.Name))
                throw new InvalidOperationException("Un objectif de planification sans nom a ete declare.");

            if (!seen.Add(goal.Name))
                throw new InvalidOperationException($"L'objectif de planification « {goal.Name} » est declare deux fois.");

            if (goal.Conditions.Count == 0)
                throw new InvalidOperationException($"L'objectif « {goal.Name} » ne dit pas a quoi on reconnait qu'il est atteint.");

            foreach (var condition in goal.ParsedConditions)
            {
                if (!condition.Variables.Any()) continue;

                throw new InvalidOperationException(
                    $"L'objectif « {goal.Name} » contient la variable « {condition} » : "
                    + "un but doit etre entierement instancie.");
            }
        }
    }

    private static IEnumerable<string> VariablesIn(string value)
        => Fact.IsVariable(value) ? [value[1..]] : Fact.VariablesOf(value);
}

/// <summary>
/// Types d'objets que <see cref="SymbolicStateBuilder"/> sait deriver de la memoire d'un agent.
/// La liste sert a valider le domaine : un parametre de type <c>vehicule</c> ne serait jamais lie.
/// </summary>
public static class PlanningObjectTypes
{
    public const string Location = "location";
    public const string Door = "door";
    public const string Window = "window";
    public const string Item = "item";
    public const string Ally = "ally";
    public const string Threat = "threat";

    /// <summary>Meuble poussable (livrable 11) : rayonnage, benne.</summary>
    public const string Heavy = "heavy";

    public static IReadOnlySet<string> Names { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Location, Door, Window, Item, Ally, Threat, Heavy,
    };
}

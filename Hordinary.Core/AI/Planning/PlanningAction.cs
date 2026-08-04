using System.Text.Json.Serialization;
using Hordinary.Agents;
using Hordinary.World;

namespace Hordinary.AI.Planning;

/// <summary>
/// Objet manipulable par le planificateur : un lieu, une porte, une fenetre, un objet ramassable,
/// un allie. Les objets ne sont pas declares en configuration — ils sont derives de la memoire de
/// l'agent par <see cref="SymbolicStateBuilder"/>. Une porte jamais vue n'existe donc pas pour lui.
/// </summary>
/// <param name="Position">
/// Ou se trouve l'objet, quand la question a un sens. C'est ce qui relie le plan symbolique au
/// terrain : le verbe <c>toPos</c> y envoie A*.
/// </param>
public sealed record PlanningObject(string Name, string Type, GridPosition? Position = null)
{
    public override string ToString() => Position is { } cell ? $"{Name} [{Type}] {cell}" : $"{Name} [{Type}]";
}

/// <summary>
/// Une action du domaine, telle qu'elle est ecrite dans <c>config/planner_domain.json</c> :
/// un nom, un verbe primitif, ses parametres, ses preconditions, ses effets, son cout, son bruit et
/// sa duree (specification, livrable 5).
///
/// Le schema est generique : <c>MoveToLocation(?location)</c> vaut pour tous les lieux connus.
/// <see cref="Ground"/> le decline en actions concretes selon les objets que l'agent connait.
/// </summary>
public sealed class ActionSchema
{
    private List<Condition>? _preconditions;
    private List<Effect>? _effects;

    public string Name { get; init; } = string.Empty;

    /// <summary>Nom du verbe primitif, valide contre <see cref="ActionVerbs"/> au chargement.</summary>
    public string Verb { get; init; } = "wait";

    /// <summary>Parametres du verbe (<c>location</c>, <c>item</c>, <c>condition</c>...), variables comprises.</summary>
    [JsonConverter(typeof(ScalarDictionaryConverter))]
    public Dictionary<string, string> Params { get; init; } = [];

    /// <summary>Variables de l'action et leur type d'objet : <c>{ "door": "door" }</c>.</summary>
    public Dictionary<string, string> Parameters { get; init; } = [];

    public List<string> Preconditions { get; init; } = [];

    public List<string> Effects { get; init; } = [];

    public float Cost { get; init; } = 1f;

    /// <summary>Surcout par cellule parcourue, pour les actions de deplacement. Nul par defaut.</summary>
    public float DistanceCost { get; init; }

    /// <summary>Bareme de la section 18.1 : le plan choisit aussi sa discretion.</summary>
    public float NoiseLevel { get; init; }

    /// <summary>
    /// Comment le bruit de l'action s'appelle dans le journal et pour les zombies qui l'entendent
    /// (<c>ForceDoor</c>, <c>BreakWindow</c>, <c>Distraction</c>…). Le niveau reste
    /// <see cref="NoiseLevel"/> ; ce champ ne fait que nommer l'evenement, et une action qui l'omet
    /// produit un bruit generique.
    /// </summary>
    public string? NoiseAction { get; init; }

    /// <summary>Duree d'execution en secondes, utilisee par <see cref="PlanExecutor"/>.</summary>
    public float Duration { get; init; } = 0.5f;

    /// <summary>Passer a <c>false</c> retire l'action du jeu sans la supprimer du fichier.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Le meme schema, vu par un survivant qui a une competence dessus (livrable 7). Seuls le cout,
    /// le bruit, la duree et la disponibilite changent : les preconditions et les effets d'une porte
    /// forcee sont les memes pour Bob que pour Claire — c'est le prix qu'il paie qui differe, et
    /// c'est ce prix que GBFS minimise.
    /// </summary>
    public ActionSchema Specialize(SurvivorSkill skill)
    {
        ArgumentNullException.ThrowIfNull(skill);

        return new ActionSchema
        {
            Name = Name,
            Verb = Verb,
            Params = Params,
            Parameters = Parameters,
            Preconditions = Preconditions,
            Effects = Effects,
            Cost = Cost * skill.Cost,
            DistanceCost = DistanceCost * skill.Cost,
            NoiseLevel = NoiseLevel * skill.Noise,
            NoiseAction = NoiseAction,
            Duration = Duration * skill.Duration,
            Enabled = Enabled && skill.Enabled,
        };
    }

    public ActionVerb ParsedVerb
        => ActionVerbs.TryParse(Verb, out var verb)
            ? verb
            : throw new InvalidOperationException($"Verbe inconnu « {Verb} ». {ActionVerbs.Vocabulary()}");

    public IReadOnlyList<Condition> ParsedPreconditions
        => _preconditions ??= [.. Preconditions.Select(Condition.Parse)];

    public IReadOnlyList<Effect> ParsedEffects
        => _effects ??= [.. Effects.Select(Effect.Parse)];

    /// <summary>
    /// Decline le schema en actions concretes. Une action sans parametre en produit exactement une ;
    /// <c>ForceDoor(?door, ?location)</c> en produit autant que de couples porte/lieu connus.
    ///
    /// Les liaisons sont enumerees dans l'ordre des noms de parametres puis des objets fournis :
    /// deux planifications sur la meme memoire produisent le meme plan, ce qui rend les tests et le
    /// journal de decisions reproductibles.
    /// </summary>
    public IEnumerable<PlanningAction> Ground(IReadOnlyList<PlanningObject> objects)
    {
        var names = Parameters.Keys.Order(StringComparer.Ordinal).ToList();
        if (names.Count == 0)
        {
            yield return new PlanningAction(this, new Dictionary<string, string>(StringComparer.Ordinal));
            yield break;
        }

        var candidates = names
            .Select(name => objects
                .Where(o => o.Type.Equals(Parameters[name], StringComparison.OrdinalIgnoreCase))
                .Select(o => o.Name)
                .ToList())
            .ToList();

        if (candidates.Any(list => list.Count == 0)) yield break;

        var indices = new int[names.Count];
        while (true)
        {
            var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < names.Count; i++) bindings[names[i]] = candidates[i][indices[i]];

            yield return new PlanningAction(this, bindings);

            int position = names.Count - 1;
            while (position >= 0 && ++indices[position] >= candidates[position].Count)
            {
                indices[position] = 0;
                position--;
            }

            if (position < 0) yield break;
        }
    }

    public override string ToString() => $"{Name} [{Verb}] cout {Cost:0.##}, bruit {NoiseLevel:0.##}";
}

/// <summary>
/// Action concrete, prete a etre appliquee a un etat : le schema avec ses variables liees.
/// C'est le <c>PlanningAction</c> du modele de donnees de la specification (document A).
/// </summary>
public sealed class PlanningAction
{
    private readonly Dictionary<string, string> _bindings;

    public ActionSchema Schema { get; }

    /// <summary>Nom lisible, liaisons comprises : <c>ForceDoor(door_7_4, pharmacy)</c>.</summary>
    public string Name { get; }

    public IReadOnlyDictionary<string, string> Bindings => _bindings;

    public IReadOnlyList<Condition> Preconditions { get; }

    public IReadOnlyList<Effect> Effects { get; }

    /// <summary>Parametres du verbe, variables resolues : ce que lira <see cref="PlanExecutor"/>.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; }

    public ActionVerb Verb => Schema.ParsedVerb;

    public float Cost => Schema.Cost;

    public float NoiseLevel => Schema.NoiseLevel;

    /// <summary>Nom de l'evenement sonore emis par l'action, s'il en porte un.</summary>
    public string? NoiseAction => Schema.NoiseAction;

    public float Duration => Schema.Duration;

    /// <summary>Objet dont la position pilote l'execution, s'il y en a un (voir <see cref="ActionVerbs.PositionParameter"/>).</summary>
    public string? TargetObject { get; }

    public PlanningAction(ActionSchema schema, Dictionary<string, string> bindings)
    {
        Schema = schema;
        _bindings = bindings;

        Preconditions = [.. schema.ParsedPreconditions.Select(condition => condition.Substitute(bindings))];
        Effects = [.. schema.ParsedEffects.Select(effect => effect.Substitute(bindings))];

        Parameters = schema.Params.ToDictionary(
            pair => pair.Key,
            pair => Substitute(pair.Value, bindings),
            StringComparer.OrdinalIgnoreCase);

        Name = bindings.Count == 0
            ? schema.Name
            : $"{schema.Name}({string.Join(", ", bindings.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value))})";

        TargetObject = ActionVerbs.PositionParameter(schema.ParsedVerb) is { } key
            && Parameters.TryGetValue(key, out string? target)
                ? target
                : null;
    }

    public bool IsApplicable(SymbolicState state) => state.Satisfies(Preconditions);

    public SymbolicState ApplyTo(SymbolicState state) => state.Apply(Effects);

    /// <summary>Ligne du journal des decisions de l'IA et du panneau debug.</summary>
    public string Describe()
        => $"{Name} — cout {Cost:0.##}, bruit {NoiseLevel:0.##}, {Duration:0.##} s";

    public override string ToString() => Name;

    /// <summary>
    /// Substitue les variables dans une valeur de parametre de verbe. Contrairement a un fait, la
    /// valeur peut etre une variable nue (<c>"?door"</c>) ou un fait entier (<c>"door_open(?door)"</c>).
    /// </summary>
    private static string Substitute(string value, IReadOnlyDictionary<string, string> bindings)
    {
        if (Fact.IsVariable(value)) return bindings.TryGetValue(value[1..], out string? bound) ? bound : value;
        return Fact.Substitute(value, bindings);
    }
}

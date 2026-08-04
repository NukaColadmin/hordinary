namespace Hordinary.AI.Planning;

/// <summary>
/// Etat symbolique du monde tel qu'un agent se le represente (specification, livrable 5).
///
/// C'est un simple ensemble de faits vrais — <c>at(agent, pharmacy)</c>, <c>door_locked(door_7_4)</c>,
/// <c>medicine_found</c>. Tout ce qui n'y figure pas est repute faux : l'etat est ferme sur ce que
/// l'agent sait. C'est volontaire et c'est la meme contrainte qu'au livrable 2 — il est construit par
/// <see cref="SymbolicStateBuilder"/> a partir de la seule <c>AgentKnowledge</c>, jamais du
/// <c>WorldState</c>, donc un survivant ne peut pas planifier avec une porte qu'il n'a jamais vue.
///
/// L'instance est immuable : <see cref="Apply"/> renvoie un nouvel etat. La recherche GBFS empile des
/// milliers d'etats voisins, elle ne doit jamais pouvoir abimer celui d'ou elle vient.
/// </summary>
public sealed class SymbolicState
{
    private readonly HashSet<string> _facts;
    private string? _hash;

    public static SymbolicState Empty { get; } = new([]);

    public SymbolicState(IEnumerable<string> facts)
        => _facts = new HashSet<string>(facts.Select(Fact.Normalize), StringComparer.OrdinalIgnoreCase);

    private SymbolicState(HashSet<string> facts) => _facts = facts;

    public IReadOnlyCollection<string> Facts => _facts;

    public bool Holds(string fact) => _facts.Contains(Fact.Normalize(fact));

    public bool Satisfies(Condition condition) => _facts.Contains(condition.Pattern) == condition.Expected;

    public bool Satisfies(IEnumerable<Condition> conditions) => conditions.All(Satisfies);

    /// <summary>Conditions d'un objectif qui ne sont pas encore remplies — la mesure de l'heuristique simple.</summary>
    public int CountUnsatisfied(IReadOnlyList<Condition> conditions)
    {
        int unsatisfied = 0;
        foreach (var condition in conditions)
        {
            if (!Satisfies(condition)) unsatisfied++;
        }

        return unsatisfied;
    }

    /// <summary>
    /// Applique une liste d'effets et renvoie l'etat resultant. Les suppressions passent d'abord,
    /// pour que <c>["!at(agent, *)", "at(agent, pharmacy)"]</c> se lise « oublier ou j'etais, puis
    /// noter ou je suis » et non l'inverse.
    /// </summary>
    public SymbolicState Apply(IReadOnlyList<Effect> effects)
    {
        if (effects.Count == 0) return this;

        var facts = new HashSet<string>(_facts, StringComparer.OrdinalIgnoreCase);

        foreach (var effect in effects)
        {
            if (effect.Value) continue;

            if (Fact.HasWildcard(effect.Pattern)) facts.RemoveWhere(fact => Fact.Matches(effect.Pattern, fact));
            else facts.Remove(effect.Pattern);
        }

        foreach (var effect in effects)
        {
            if (effect.Value) facts.Add(effect.Pattern);
        }

        return new SymbolicState(facts);
    }

    public SymbolicState With(params string[] facts) => Apply([.. facts.Select(fact => Effect.Parse(fact))]);

    /// <summary>
    /// Confronte l'etat courant a ce que l'agent vient d'observer (livrable 6 : « mise a jour des
    /// connaissances »). L'observation tranche sur tous les predicats qu'elle sait produire — une
    /// porte, un lieu, un objet, une menace ; l'etat courant conserve tout le reste, c'est-a-dire
    /// l'avancement du plan, que le monde ne raconte pas : personne ne peut voir qu'une piece a deja
    /// ete fouillee.
    ///
    /// Sans cette separation, verifier un plan reviendrait a choisir entre oublier ce qui a ete fait
    /// et ignorer ce qui a change.
    /// </summary>
    public SymbolicState Reconcile(SymbolicState observed, IReadOnlySet<string> observablePredicates)
    {
        ArgumentNullException.ThrowIfNull(observed);
        ArgumentNullException.ThrowIfNull(observablePredicates);

        var facts = new HashSet<string>(observed._facts, StringComparer.OrdinalIgnoreCase);

        foreach (var fact in _facts)
        {
            if (!observablePredicates.Contains(Fact.PredicateOf(fact))) facts.Add(fact);
        }

        return new SymbolicState(facts);
    }

    /// <summary>
    /// Empreinte stable de l'etat, independante de l'ordre d'insertion. Elle sert de cle a l'ensemble
    /// ferme de GBFS et, sur un plan, de temoin de l'etat qui l'a produit
    /// (<see cref="Plan.CreatedFromStateHash"/>) : c'est ce qui permettra au livrable 6 de savoir
    /// qu'un plan a ete concu dans un monde qui n'existe plus.
    /// </summary>
    public string Hash => _hash ??= ComputeHash();

    private string ComputeHash()
    {
        // FNV-1a sur les faits tries : deterministe d'une execution a l'autre, contrairement au
        // GetHashCode des chaines .NET, qui est randomise par processus.
        ulong hash = 14695981039346656037UL;

        foreach (var fact in _facts.Select(f => f.ToLowerInvariant()).Order(StringComparer.Ordinal))
        {
            foreach (char c in fact)
            {
                hash = (hash ^ c) * 1099511628211UL;
            }

            hash = (hash ^ '|') * 1099511628211UL;
        }

        return hash.ToString("x16");
    }

    public override string ToString()
        => _facts.Count == 0 ? "(etat vide)" : string.Join(", ", _facts.Order(StringComparer.OrdinalIgnoreCase));
}

namespace Hordinary.AI.Planning.Pddl;

/// <summary>
/// Traduit le domaine du jeu (<c>config/planner_domain.json</c>, charge en
/// <see cref="PlanningDomain"/>) en <see cref="PddlDomain"/> (spec G).
///
/// <para>
/// Rien n'est ecrit en dur : les types viennent de <see cref="PlanningObjectTypes"/>, les predicats
/// sont extraits mecaniquement des preconditions et des effets de toutes les actions activees, et
/// leur arite comme leurs types se deduisent des <c>parameters</c> des actions qui les emploient. Le
/// nombre d'actions n'est ecrit ni ici ni dans un test : c'est « autant que le fichier en declare,
/// moins celles a <c>enabled: false</c> ».
/// </para>
///
/// <para>
/// Les champs <c>verb</c>, <c>params</c>, <c>duration</c> et <c>noise_action</c> ne sont pas traduits :
/// ils decrivent l'<b>execution</b>, que PDDL ne modelise pas. Ils ne sont pas perdus pour autant —
/// <see cref="PddlAction.Origin"/> les garde pour le rendu inverse vers GBFS.
/// </para>
///
/// <para>
/// <c>distance_cost</c> n'est pas traduit non plus, et c'est une <b>limite</b> plutot qu'un choix.
/// Le jeu facture un cout par cellule parcourue, calcule depuis l'endroit ou l'agent se trouve ;
/// l'ecrire en PDDL demanderait <c>(increase (total-cost) (distance ?from ?to))</c>, donc un
/// parametre d'origine que <c>MoveToLocation(?location)</c> n'a pas. Le cout exporte est donc la
/// part fixe seule. Le declarer quand meme — une fonction <c>distance</c> qu'aucune action ne lit,
/// et sa table de valeurs dans chaque probleme — ne rapprochait pas les deux couts : ca ajoutait
/// deux cents lignes mortes par instantane et laissait croire qu'ils coincidaient.
/// </para>
/// </summary>
public static class PddlDomainBuilder
{
    /// <summary>Le fragment de la spec G. Le domaine compile du livrable 15 ne doit jamais en sortir.</summary>
    public static IReadOnlyList<string> Requirements { get; } =
    [
        ":strips", ":typing", ":negative-preconditions", ":conditional-effects", ":action-costs",
    ];

    /// <summary>Le type racine de PDDL, celui des constantes qu'aucune action ne type mieux.</summary>
    public const string RootType = "object";

    public static PddlDomain From(PlanningDomain domain, string name = "hordinary")
    {
        ArgumentNullException.ThrowIfNull(domain);

        var schemas = domain.Active.ToList();
        var types = Typing.Infer(schemas);

        var locationKinds = domain.Places.Kinds.ToList();
        var landmarks = Landmarks(domain, types);

        return new PddlDomain
        {
            Name = name,
            Requirements = Requirements,
            Types = [.. PlanningObjectTypes.Names.Order(StringComparer.Ordinal)],
            Constants = Constants(types),
            Predicates = Predicates(types, locationKinds),
            Functions = [new PddlFunction(PddlIncrease.TotalCost, [])],
            Actions = [.. schemas.Select(schema => Translate(schema, types))],
            Goals = [.. domain.Goals.Where(goal => goal.Enabled)],
            LocationKinds = locationKinds,
            Landmarks = landmarks,
        };
    }

    /// <summary>
    /// Un schema JSON devient un schema PDDL. Le seul point qui demande de reflechir est le joker
    /// <c>*</c> : PDDL n'a pas de suppression par joker, et <c>"!at(agent, *)"</c> se rend en effet
    /// universellement quantifie — la reponse retenue par la spec G, fidele et supportee par Fast
    /// Downward via ADL.
    /// </summary>
    private static PddlAction Translate(ActionSchema schema, Typing types)
    {
        var parameters = schema.Parameters
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new PddlParameter($"?{pair.Key}", pair.Value))
            .ToList();

        var precondition = new PddlConjunction(schema.ParsedPreconditions.Select(condition =>
            condition.Expected
                ? PddlAtom.Parse(condition.Pattern)
                : (PddlFormula)new PddlNegation(PddlAtom.Parse(condition.Pattern))));

        var effect = new PddlConjunction(schema.ParsedEffects.Select(item => TranslateEffect(item, parameters, types)));

        return new PddlAction(schema.Name, parameters, precondition, effect, schema.Cost, schema);
    }

    private static PddlFormula TranslateEffect(Effect effect, IReadOnlyList<PddlParameter> parameters, Typing types)
    {
        var atom = PddlAtom.Parse(effect.Pattern);

        if (effect.Value) return atom;
        if (!atom.Arguments.Contains(Fact.Wildcard)) return new PddlNegation(atom);

        int position = atom.Arguments.ToList().IndexOf(Fact.Wildcard);
        string type = types.TypeOf(atom.Predicate, position);
        var variable = FreshVariable(type, parameters);

        var arguments = atom.Arguments.ToArray();
        arguments[position] = variable.Name;

        return new PddlUniversal(variable, new PddlNegation(new PddlAtom(atom.Predicate, arguments)));
    }

    /// <summary>Une variable de quantification qui ne peut pas capturer un parametre de l'action.</summary>
    private static PddlParameter FreshVariable(string type, IReadOnlyList<PddlParameter> parameters)
    {
        string root = $"?{char.ToLowerInvariant(type[0])}";
        string candidate = root;
        int suffix = 1;

        while (parameters.Any(parameter => parameter.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = root + suffix++.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return new PddlParameter(candidate, type);
    }

    /// <summary>
    /// Les constantes du domaine : les symboles nommes en dur par une precondition ou un effet, sauf
    /// ceux de type <c>location</c>.
    ///
    /// La ligne de partage n'est pas arbitraire. Un lieu depend de la carte et de la mission —
    /// <c>objective_zone</c> n'est pas la meme pharmacie d'une partie a l'autre — donc il appartient
    /// au <b>probleme</b>. <c>agent</c>, <c>objective</c> ou <c>remedy</c> sont des symboles du
    /// langage, vrais pour toute partie : ils appartiennent au domaine.
    /// </summary>
    private static List<PddlParameter> Constants(Typing types)
        => [.. types.Constants
            .Where(pair => !pair.Value.Equals(PlanningObjectTypes.Location, StringComparison.OrdinalIgnoreCase))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new PddlParameter(pair.Key, pair.Value))];

    private static List<string> Landmarks(PlanningDomain domain, Typing types)
    {
        var landmarks = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, type) in types.Constants)
        {
            if (type.Equals(PlanningObjectTypes.Location, StringComparison.OrdinalIgnoreCase)) landmarks.Add(name);
        }

        // Les buts nomment des lieux que les actions ne nomment pas : at(agent, mission) est un but
        // sans action dediee.
        foreach (var goal in domain.Goals.Where(goal => goal.Enabled))
        {
            foreach (var condition in goal.ParsedConditions)
            {
                var atom = PddlAtom.Parse(condition.Pattern);
                for (int position = 0; position < atom.Arity; position++)
                {
                    if (atom.Arguments[position].StartsWith('?')) continue;
                    if (types.TypeOf(atom.Predicate, position).Equals(PlanningObjectTypes.Location, StringComparison.OrdinalIgnoreCase))
                        landmarks.Add(atom.Arguments[position]);
                }
            }
        }

        foreach (string declared in domain.Places.Landmarks) landmarks.Add(declared);

        return [.. landmarks];
    }

    private static List<PddlPredicate> Predicates(Typing types, IReadOnlyList<string> locationKinds)
    {
        var predicates = types.Predicates.ToList();

        // Les categories de lieux ne sont posees par aucune action — c'est le moteur qui les emet,
        // comme la perception pose "known". Elles doivent pourtant etre declarees : sans cela le
        // schema scinde par « avant d'entrer dans un batiment » citerait un predicat inconnu, et un
        // planificateur tiers refuserait le domaine.
        foreach (string kind in locationKinds)
        {
            if (predicates.Any(predicate => predicate.Name.Equals(kind, StringComparison.OrdinalIgnoreCase))) continue;
            predicates.Add(new PddlPredicate(kind, [PlanningObjectTypes.Location]));
        }

        return [.. predicates.OrderBy(predicate => predicate.Name, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Le typage deduit du domaine : pour chaque predicat, le type de chacun de ses arguments, et
    /// pour chaque symbole nomme en dur, le type de la position ou il apparait.
    ///
    /// La deduction se fait en deux passes parce qu'elle est circulaire par nature : ce sont les
    /// <b>variables</b> des actions qui portent un type (<c>at(agent, ?location)</c> dit que la
    /// seconde position de <c>at</c> est un lieu), et ce sont ces positions qui donnent ensuite leur
    /// type aux constantes (<c>at(agent, extraction)</c> dit que <c>extraction</c> est un lieu).
    /// </summary>
    private sealed class Typing
    {
        private readonly Dictionary<string, string[]> _positions = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> Constants { get; } = new(StringComparer.OrdinalIgnoreCase);

        public IEnumerable<PddlPredicate> Predicates
            => _positions.Select(pair => new PddlPredicate(pair.Key, pair.Value));

        public string TypeOf(string predicate, int position)
            => _positions.TryGetValue(predicate, out var types) && position < types.Length ? types[position] : RootType;

        public static Typing Infer(IReadOnlyList<ActionSchema> schemas)
        {
            var typing = new Typing();
            var atoms = schemas.SelectMany(schema => Atoms(schema).Select(atom => (Schema: schema, Atom: atom))).ToList();

            foreach (var (schema, atom) in atoms)
            {
                if (!typing._positions.TryGetValue(atom.Predicate, out var types))
                {
                    types = new string[atom.Arity];
                    Array.Fill(types, RootType);
                    typing._positions[atom.Predicate] = types;
                }

                if (types.Length != atom.Arity)
                {
                    throw new InvalidOperationException(
                        $"Le predicat « {atom.Predicate} » est employe avec {atom.Arity} arguments et {types.Length} ailleurs : "
                        + "un predicat a une arite unique.");
                }

                for (int position = 0; position < atom.Arity; position++)
                {
                    string argument = atom.Arguments[position];
                    if (!argument.StartsWith('?')) continue;
                    if (!schema.Parameters.TryGetValue(argument[1..], out string? type)) continue;

                    // Un predicat employe sur deux types differents retombe sur « object » : c'est la
                    // seule reponse qu'un planificateur tiers accepte sans se plaindre.
                    types[position] = types[position] == RootType || types[position] == type ? type : RootType;
                }
            }

            foreach (var (_, atom) in atoms)
            {
                for (int position = 0; position < atom.Arity; position++)
                {
                    string argument = atom.Arguments[position];
                    if (argument.StartsWith('?') || argument == Fact.Wildcard) continue;

                    typing.Constants[argument] = typing.TypeOf(atom.Predicate, position);
                }
            }

            return typing;
        }

        private static IEnumerable<PddlAtom> Atoms(ActionSchema schema)
            => schema.ParsedPreconditions.Select(condition => PddlAtom.Parse(condition.Pattern))
                .Concat(schema.ParsedEffects.Select(effect => PddlAtom.Parse(effect.Pattern)));
    }
}

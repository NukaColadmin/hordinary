using System.Globalization;
using Hordinary.AI.Planning;
using Hordinary.AI.Planning.Pddl;

namespace Hordinary.AI.Instructions;

/// <summary>Ce qu'un portillon rend : un verdict, et la phrase qui l'explique au joueur.</summary>
/// <param name="Constraint">La contrainte reconnue, quand elle l'a ete.</param>
public readonly record struct GrammarResult(bool Accepted, TacticalConstraint? Constraint, string Reason)
{
    public static GrammarResult Reject(string reason) => new(false, null, reason);

    public static GrammarResult Accept(TacticalConstraint constraint) => new(true, constraint, constraint.Text);

    public override string ToString() => Reason;
}

/// <summary>
/// Portillon 1 : la grammaire. Deterministe, sans reseau, dans <c>Hordinary.Core</c>.
///
/// <para>
/// La contrainte se lit dans l'une des cinq formes ; chaque predicat cite figure dans les predicats
/// du domaine exporte, avec la bonne arite et des arguments <b>du bon type</b> ; chaque schema
/// d'action ou classe de schemas citee existe dans ce meme domaine ; le multiplicateur de
/// <c>penalize</c> est dans les bornes de la configuration.
/// </para>
///
/// <para>
/// Le controle de type n'etait pas la, et son absence a ete trouvee par un modele faible plutot que
/// par une relecture : <c>require_before(equipped(agent), AttackWithWeapon)</c> passait, parce que
/// <c>agent</c> est bien un symbole du domaine et que <c>equipped</c> a bien un argument. Il est
/// pourtant de type <c>object</c> la ou le predicat attend un <c>item</c>. La consequence n'etait pas
/// cosmetique : le portillon 2 compile le fait en precondition, donc la faute partait dans le
/// <c>domain.constrained.pddl</c> qu'on donne a Fast Downward — un domaine mal type, refuse par le
/// validateur, pour une faute de grammaire du joueur. Un portillon ne doit pas accepter ce que le
/// validateur exterieur refuse.
/// </para>
///
/// <para>
/// Le vocabulaire de reference est <b>le domaine PDDL, pas une liste tenue a la main</b>. Il n'y a
/// donc aucun endroit ou oublier de mettre a jour la liste des symboles autorises quand le domaine
/// bouge : le cas d'ecole — un modele qui propose <c>require_before(barricaded(?door),
/// enter:building)</c> alors que <c>barricaded</c> n'existe pas — est refuse ici, et il le reste sans
/// que personne ait a y penser.
/// </para>
/// </summary>
public static class ConstraintGrammar
{
    public static GrammarResult Parse(string? text, PddlDomain domain, InstructionSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(domain);
        settings ??= new InstructionSettings();

        string trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0) return GrammarResult.Reject("Instruction vide.");

        int open = trimmed.IndexOf('(');
        if (open < 0 || !trimmed.EndsWith(')'))
            return GrammarResult.Reject($"« {trimmed} » n'est pas une contrainte : il en faut une des cinq formes, {Forms()}.");

        string form = trimmed[..open].Trim().ToLowerInvariant();
        var arguments = SplitArguments(trimmed[(open + 1)..^1]);

        return form switch
        {
            "require_before" => Temporal(ConstraintForm.RequireBefore, arguments, domain, trimmed),
            "require_after" => Temporal(ConstraintForm.RequireAfter, arguments, domain, trimmed),
            "require_always" => Always(arguments, domain, trimmed),
            "forbid" => ForbidForm(arguments, domain, trimmed),
            "penalize" => PenalizeForm(arguments, domain, settings, trimmed),
            _ => GrammarResult.Reject($"Forme inconnue « {form} ». Les cinq formes sont {Forms()}."),
        };
    }

    /// <summary>Les cinq formes, telles qu'on les propose au joueur quand il ecrit autre chose.</summary>
    public static string Forms()
        => "require_before(P, A), require_after(P, A), forbid(A), require_always(P), penalize(A, k)";

    private static GrammarResult Temporal(ConstraintForm form, IReadOnlyList<string> arguments, PddlDomain domain, string text)
    {
        if (arguments.Count != 2)
            return GrammarResult.Reject($"« {text} » attend deux arguments : un fait et une action.");

        if (Predicate(arguments[0], domain) is { } predicateError) return GrammarResult.Reject(predicateError);
        if (Target(arguments[1], domain) is not { } target) return GrammarResult.Reject(TargetError(arguments[1], domain));

        var atom = PddlAtom.Parse(arguments[0]);

        return GrammarResult.Accept(form == ConstraintForm.RequireBefore
            ? TacticalConstraint.RequireBefore(atom, target)
            : TacticalConstraint.RequireAfter(atom, target));
    }

    private static GrammarResult Always(IReadOnlyList<string> arguments, PddlDomain domain, string text)
    {
        if (arguments.Count != 1)
            return GrammarResult.Reject($"« {text} » attend un seul argument : le fait a maintenir vrai.");

        if (Predicate(arguments[0], domain) is { } error) return GrammarResult.Reject(error);

        return GrammarResult.Accept(TacticalConstraint.RequireAlways(PddlAtom.Parse(arguments[0])));
    }

    private static GrammarResult ForbidForm(IReadOnlyList<string> arguments, PddlDomain domain, string text)
    {
        if (arguments.Count != 1)
            return GrammarResult.Reject($"« {text} » attend un seul argument : l'action interdite.");

        if (Target(arguments[0], domain) is not { } target) return GrammarResult.Reject(TargetError(arguments[0], domain));

        return GrammarResult.Accept(TacticalConstraint.Forbid(target));
    }

    private static GrammarResult PenalizeForm(IReadOnlyList<string> arguments, PddlDomain domain, InstructionSettings settings, string text)
    {
        if (arguments.Count != 2)
            return GrammarResult.Reject($"« {text} » attend deux arguments : une action et un multiplicateur.");

        if (Target(arguments[0], domain) is not { } target) return GrammarResult.Reject(TargetError(arguments[0], domain));

        if (!float.TryParse(arguments[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float factor))
            return GrammarResult.Reject($"« {arguments[1]} » n'est pas un multiplicateur de cout.");

        if (factor < settings.MinPenalty || factor > settings.MaxPenalty)
        {
            return GrammarResult.Reject(
                $"Le multiplicateur {factor.ToString("0.###", CultureInfo.InvariantCulture)} sort des bornes de la configuration "
                + $"({settings.MinPenalty.ToString("0.###", CultureInfo.InvariantCulture)} a {settings.MaxPenalty.ToString("0.###", CultureInfo.InvariantCulture)}).");
        }

        return GrammarResult.Accept(TacticalConstraint.Penalize(target, factor));
    }

    /// <summary>La raison du refus d'un fait, ou <c>null</c> s'il est recevable.</summary>
    private static string? Predicate(string text, PddlDomain domain)
    {
        string fact;
        try
        {
            fact = Fact.Normalize(text);
        }
        catch (FormatException error)
        {
            return error.Message;
        }

        if (fact.Length == 0) return "Fait vide.";

        var atom = PddlAtom.Parse(fact);
        var declared = domain.FindPredicate(atom.Predicate);

        if (declared is null)
        {
            return $"Le predicat « {atom.Predicate} » n'existe pas dans le domaine. "
                + $"Predicats connus : {string.Join(", ", domain.Predicates.Select(predicate => predicate.Name))}.";
        }

        if (declared.Arity != atom.Arity)
            return $"Le predicat « {atom.Predicate} » prend {declared.Arity} argument(s), pas {atom.Arity}.";

        for (int position = 0; position < atom.Arity; position++)
        {
            string argument = atom.Arguments[position];

            if (argument.StartsWith('?'))
            {
                return $"« {atom.AsFact} » contient la variable « {argument} » : une contrainte nomme des objets, "
                    + "jamais des variables — personne ne saurait par quoi les lier.";
            }

            if (domain.TypeOfObject(argument) is not { } actual)
            {
                return $"L'objet « {argument} » n'existe pas dans le domaine. "
                    + $"Objets connus : {string.Join(", ", Vocabulary(domain))}.";
            }

            string expected = declared.ParameterTypes[position];

            if (PddlDomain.TypeFits(actual, expected)) continue;

            string candidates = string.Join(", ", OfType(domain, expected));

            return $"« {atom.AsFact} » donne « {argument} », qui est de type {actual}, a une position que "
                + $"« {declared.Name} » type {expected}. Objets de type {expected} : "
                + (candidates.Length > 0 ? candidates : "aucun") + ".";
        }

        return null;
    }

    /// <summary>
    /// Les symboles du domaine qui ont le type demande. Ils vont dans la phrase de refus parce qu'un
    /// « mauvais type » sans la liste des bons n'apprend rien au joueur — ni au modele, a qui la
    /// reprise du noeud <c>self_check</c> renvoie cette meme phrase.
    /// </summary>
    private static IEnumerable<string> OfType(PddlDomain domain, string type)
        => domain.Vocabulary
            .Where(entry => PddlDomain.TypeFits(entry.Type, type))
            .Select(entry => entry.Name)
            .Order(StringComparer.Ordinal);

    private static IEnumerable<string> Vocabulary(PddlDomain domain)
        => domain.Vocabulary.Select(entry => entry.Name).Order(StringComparer.Ordinal);

    private static ActionTarget? Target(string text, PddlDomain domain)
    {
        string trimmed = text.Trim();

        int separator = trimmed.IndexOf(':');
        if (separator < 0)
        {
            return domain.FindAction(trimmed) is { } action ? ActionTarget.OfSchema(action.Name) : null;
        }

        string family = trimmed[..separator].Trim();
        string category = trimmed[(separator + 1)..].Trim();

        if (!ActionFamilies.Names.Contains(family)) return null;
        if (!domain.IsLocationKind(category)) return null;

        return ActionTarget.OfClass(family.ToLowerInvariant(), category.ToLowerInvariant());
    }

    private static string TargetError(string text, PddlDomain domain)
        => text.Contains(':')
            ? $"« {text.Trim()} » n'est pas une classe de schemas. Forme attendue : "
                + $"{string.Join(" ou ", ActionFamilies.Names.Order(StringComparer.Ordinal))} suivi de « : » et d'une categorie de lieu "
                + $"({string.Join(", ", domain.LocationKinds)})."
            : $"L'action « {text.Trim()} » n'existe pas dans le domaine. "
                + $"Actions connues : {string.Join(", ", domain.Actions.Select(action => action.Name))}.";

    /// <summary>Decoupe les arguments au premier niveau : <c>at(agent, group)</c> reste entier.</summary>
    private static List<string> SplitArguments(string text)
    {
        var arguments = new List<string>();
        int depth = 0;
        int start = 0;

        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];

            if (character == '(') depth++;
            else if (character == ')') depth--;
            else if (character == ',' && depth == 0)
            {
                arguments.Add(text[start..index].Trim());
                start = index + 1;
            }
        }

        string last = text[start..].Trim();
        if (last.Length > 0) arguments.Add(last);

        return arguments;
    }
}

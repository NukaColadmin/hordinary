using System.Globalization;
using Hordinary.AI.Planning;
using Hordinary.AI.Planning.Pddl;

namespace Hordinary.AI.Instructions;

/// <summary>
/// Ce que le compilateur doit savoir du monde pour juger une contrainte — et rien de plus. Le reste
/// de sa besogne est une fonction pure du domaine et des contraintes.
/// </summary>
/// <param name="Search">
/// La recherche active, lue dans <c>planner.search</c>. Elle decide si <c>penalize</c> agit.
/// </param>
/// <param name="AvailableKinds">
/// Les categories de lieux reellement presentes sur la carte jouee. Vide ou <c>null</c> : le
/// compilateur ne sait pas, et ne previent pas. Une categorie absente n'est pas un refus — la
/// contrainte est acceptee et signalee sans effet ici.
/// </param>
/// <param name="Goal">Le but courant, pour savoir si un <c>forbid</c> le rend inatteignable.</param>
public readonly record struct CompilationContext(
    PlannerSearch Search = PlannerSearch.Gbfs,
    IReadOnlySet<string>? AvailableKinds = null,
    PlanningGoal? Goal = null);

/// <summary>
/// Le resultat du portillon 2 : un domaine, un verdict, et ce qu'il faut dire au joueur.
///
/// <para>
/// Un refus n'est pas un echec du systeme, c'est son contenu : le journal garde la raison et le
/// portillon qui l'a produite. Un systeme qui n'affiche que ses succes ne prouve rien.
/// </para>
/// </summary>
public sealed record CompiledDomain(
    PddlDomain Domain,
    bool Accepted,
    IReadOnlyList<TacticalConstraint> Applied,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public override string ToString()
        => Accepted
            ? $"{Applied.Count} contrainte(s) compilee(s), {Warnings.Count} avertissement(s)"
            : $"refus : {string.Join(" ", Errors)}";
}

/// <summary>
/// Portillon 2 : la compilation. <c>PddlDomain</c> en entree, <c>PddlDomain</c> en sortie, rien
/// d'autre — une fonction pure, donc testable exhaustivement.
///
/// <para>
/// La methode est celle de l'article de Gerevini &amp; Long : traduire la modalite temporelle en
/// preconditions supplementaires, quitte a scinder une action en deux. Ce n'est pas une elegance,
/// c'est la seule facon d'obtenir quelque chose qu'un planificateur sache resoudre — ni GBFS ni Fast
/// Downward ne prennent un bloc <c>(:constraints …)</c> en entree.
/// </para>
///
/// <para>
/// Le domaine rendu reste donc dans <b>exactement</b> le meme fragment que celui de la spec G :
/// memes <c>:requirements</c>, aucun bloc de contraintes. La forme declarative PDDL 3 existe a cote
/// (<see cref="TacticalConstraint.Declarative"/>) et n'est soumise a aucun planificateur.
/// </para>
///
/// <para>
/// <b>La compilation est plus stricte que la contrainte, et c'est voulu.</b>
/// <c>sometime-before(φ, ψ)</c> demande que <c>ψ</c> ait ete vraie a un moment quelconque avant
/// <c>φ</c> ; la forme compilee l'exige dans l'etat immediatement precedent. Elle est donc correcte
/// — tout plan qu'elle accepte satisfait la contrainte d'origine — et incomplete. L'approximation va
/// dans le sens de l'intention : « regroupez-vous avant d'entrer » veut le regroupement a l'entree,
/// pas un regroupement quelconque dans le passe.
/// </para>
/// </summary>
public static class DomainCompiler
{
    public static CompiledDomain Compile(
        PddlDomain source,
        IReadOnlyList<TacticalConstraint> constraints,
        CompilationContext context = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(constraints);

        var errors = new List<string>();
        var warnings = new List<string>();
        var applied = new List<TacticalConstraint>();

        foreach (string contradiction in Contradictions(constraints)) errors.Add(contradiction);
        if (errors.Count > 0) return new CompiledDomain(source, false, [], errors, warnings);

        var actions = source.Actions.ToList();

        foreach (var constraint in constraints)
        {
            var step = Apply(constraint, actions, context, warnings);

            if (step is null)
            {
                errors.Add($"« {constraint.Text} » : {warnings[^1]}");
                warnings.RemoveAt(warnings.Count - 1);
                return new CompiledDomain(source, false, [], errors, warnings);
            }

            actions = step;
            applied.Add(constraint);
        }

        if (actions.Count == 0)
        {
            errors.Add("Les contraintes actives ne laissent aucune action jouable : le domaine serait mort.");
            return new CompiledDomain(source, false, [], errors, warnings);
        }

        if (Unreachable(source, actions, context.Goal) is { } lost)
        {
            errors.Add(lost);
            return new CompiledDomain(source, false, [], errors, warnings);
        }

        return new CompiledDomain(source.With(actions), true, applied, errors, warnings);
    }

    /// <summary>
    /// Applique une contrainte, ou rend <c>null</c> apres avoir depose sa raison en derniere ligne
    /// des avertissements — le refus est un avertissement qu'on promeut, pas une exception.
    /// </summary>
    private static List<PddlAction>? Apply(
        TacticalConstraint constraint,
        List<PddlAction> actions,
        CompilationContext context,
        List<string> warnings)
    {
        var members = Members(constraint, actions).ToList();

        // require_always ne vise aucune action en particulier : elle les traverse toutes.
        if (constraint.Target is not null && members.Count == 0)
        {
            warnings.Add($"« {constraint.Text} » ne designe aucun schema de ce domaine : sans effet.");
            return actions;
        }

        if (constraint.Target is { IsClass: true, Category: { } category } && context.AvailableKinds is { Count: > 0 } kinds
            && !kinds.Contains(category))
        {
            warnings.Add($"« {constraint.Text} » : aucun lieu de categorie « {category} » sur cette carte, la contrainte est sans effet ici.");
        }

        return constraint.Form switch
        {
            ConstraintForm.RequireBefore => Restrict(constraint, actions, members, constraint.Predicate!, warnings),
            ConstraintForm.RequireAfter => After(constraint, actions, members, warnings),
            ConstraintForm.Forbid => Forbid(constraint, actions, members),
            ConstraintForm.RequireAlways => Always(constraint, actions, warnings),
            ConstraintForm.Penalize => Penalize(constraint, actions, members, context, warnings),
            _ => actions,
        };
    }

    private static IEnumerable<PddlAction> Members(TacticalConstraint constraint, List<PddlAction> actions)
        => constraint.Target is null
            ? []
            : ActionFamilies.Members(new PddlDomain { Name = "courant", Actions = actions }, constraint.Target);

    // --- require_before -----------------------------------------------------------------------

    /// <summary>
    /// Ajoute <paramref name="required"/> a la precondition des schemas vises, en scindant d'abord
    /// ceux qu'une classe ne vise qu'a moitie.
    ///
    /// <para>
    /// La scission existe parce qu'une precondition PDDL est conjonctive : on ne peut pas y ecrire
    /// « si batiment alors regroupe ». On ecrit « batiment <b>et</b> regroupe » d'un cote, « pas
    /// batiment » de l'autre. L'union des preconditions des deux schemas produits couvre exactement
    /// les instances du schema d'origine, et leur intersection est vide — c'est ce qui rend la
    /// scission sure, et c'est ce qu'un test verifie.
    /// </para>
    /// </summary>
    private static List<PddlAction> Restrict(
        TacticalConstraint constraint,
        List<PddlAction> actions,
        IReadOnlyList<PddlAction> members,
        PddlFormula required,
        List<string> warnings)
    {
        var result = new List<PddlAction>(actions);

        foreach (var member in members)
        {
            int index = result.IndexOf(member);

            if (constraint.Target!.IsClass)
            {
                // Une contrainte precedente a pu deja scinder ce schema sur la meme categorie : sa
                // moitie « dedans » recoit l'exigence telle quelle, sa moitie « dehors » n'est pas
                // visee. La rescinder donnerait EnterBuilding_EnterBuilding et une moitie morte,
                // dont la precondition exigerait a la fois building(?l) et sa negation.
                switch (SideOf(constraint.Target, member))
                {
                    case Side.Inside:
                        result[index] = member.With(precondition: member.Precondition.Conjoin(required), generatedBy: Origin(member, constraint));
                        continue;
                    case Side.Outside:
                        continue;
                }

                if (Split(constraint, member, result) is not { } halves)
                {
                    warnings.Add($"« {constraint.Text} » : le schema « {member.Name} » n'emmene l'agent nulle part de nommable.");
                    continue;
                }

                result[index] = halves.Outside;
                result.Insert(index + 1, halves.Inside.With(precondition: halves.Inside.Precondition.Conjoin(required)));
            }
            else
            {
                result[index] = member.With(
                    precondition: member.Precondition.Conjoin(required),
                    generatedBy: Origin(member, constraint));
            }
        }

        return result;
    }

    /// <summary>
    /// Les deux moities d'un schema de deplacement vis-a-vis d'une categorie de lieu : celle qui y va
    /// (<c>Inside</c>, un schema neuf) et celle qui n'y va pas (<c>Outside</c>, le schema d'origine
    /// reduit).
    /// </summary>
    private static (PddlAction Inside, PddlAction Outside)? Split(TacticalConstraint constraint, PddlAction member, IReadOnlyList<PddlAction> existing)
    {
        if (ActionFamilies.DestinationOf(member) is not { } destination) return null;

        var category = new PddlAtom(constraint.Target!.Category!, destination.Name);

        var outside = member.With(precondition: member.Precondition.Conjoin(new PddlNegation(category)));
        var inside = member.With(
            name: SplitName(constraint.Target, member, existing),
            precondition: member.Precondition.Conjoin(category),
            generatedBy: constraint.Text);

        return (inside, outside);
    }

    private enum Side { Straddles, Inside, Outside }

    /// <summary>
    /// Ou tombe deja un schema vis-a-vis de la categorie d'une classe : dedans si sa precondition
    /// exige la categorie de sa destination, dehors si elle l'exclut, a cheval sinon — et seul un
    /// schema a cheval se scinde. C'est ce qui rend deux consignes sur la meme classe composables
    /// (« armez-vous » et « faites diversion », toutes deux avant <c>enter:building</c>).
    /// </summary>
    private static Side SideOf(ActionTarget target, PddlAction member)
    {
        if (ActionFamilies.DestinationOf(member) is not { } destination) return Side.Straddles;

        var category = new PddlAtom(target.Category!, destination.Name);
        var conjuncts = member.Precondition.Conjuncts;

        if (conjuncts.Contains(category)) return Side.Inside;
        if (conjuncts.Contains(new PddlNegation(category))) return Side.Outside;
        return Side.Straddles;
    }

    /// <summary>
    /// Les contraintes dont un schema est issu, dans l'ordre ou elles l'ont touche. Le commentaire
    /// du domaine exporte les cite toutes : n'en garder que la derniere ferait croire, a qui lit le
    /// PDDL, qu'une precondition vient de nulle part.
    /// </summary>
    private static string Origin(PddlAction member, TacticalConstraint constraint)
        => member.GeneratedBy is { } earlier && !earlier.Contains(constraint.Text, StringComparison.Ordinal)
            ? $"{earlier}, puis {constraint.Text}"
            : constraint.Text;

    /// <summary>
    /// Le nom du schema issu d'une scission : <c>enter:building</c> sur <c>MoveToLocation</c> donne
    /// <c>EnterBuilding</c>. Le nom du schema d'origine est repris en suffixe si la forme courte est
    /// deja prise — deux contraintes ne doivent jamais produire deux schemas homonymes.
    /// </summary>
    private static string SplitName(ActionTarget target, PddlAction member, IReadOnlyList<PddlAction> existing)
    {
        string root = Capitalize(target.Family!) + Capitalize(target.Category!);
        string candidate = root;

        if (Taken(candidate, existing)) candidate = $"{root}_{member.Name}";

        int suffix = 2;
        while (Taken(candidate, existing))
        {
            candidate = $"{root}_{member.Name}_{suffix++.ToString(CultureInfo.InvariantCulture)}";
        }

        return candidate;
    }

    private static bool Taken(string name, IReadOnlyList<PddlAction> existing)
        => existing.Any(action => action.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static string Capitalize(string text)
        => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    // --- require_after ------------------------------------------------------------------------

    /// <summary>
    /// « A doit poser P ». Trois cas, et un seul demande du travail.
    ///
    /// <para>
    /// Si le schema etablit deja <c>P</c>, il n'y a rien a compiler et on le dit. S'il le retire,
    /// la contrainte est impossible a satisfaire par cette action : refus. Sinon <c>P</c> devient une
    /// precondition — ce qui peut surprendre pour une contrainte dite « apres », et ne l'est pas : une
    /// action qui ne touche pas a <c>P</c> le laisse vrai apres si et seulement s'il l'etait avant.
    /// C'est la meme lecture stricte que pour <c>require_before</c>, et elle est correcte par la meme
    /// raison.
    /// </para>
    /// </summary>
    private static List<PddlAction>? After(
        TacticalConstraint constraint,
        List<PddlAction> actions,
        IReadOnlyList<PddlAction> members,
        List<string> warnings)
    {
        var required = constraint.Predicate!;
        var pending = new List<PddlAction>();

        foreach (var member in members)
        {
            if (Establishes(member, required))
            {
                warnings.Add($"« {constraint.Text} » : « {member.Name} » etablit deja ce fait, la contrainte ne change rien.");
                continue;
            }

            if (Deletes(member, required))
            {
                warnings.Add($"« {member.Name} » retire « {required.AsFact} » : aucune reecriture ne peut faire tenir ce fait apres elle.");
                return null;
            }

            pending.Add(member);
        }

        return pending.Count == 0 ? actions : Restrict(constraint, actions, pending, required, warnings);
    }

    // --- forbid -------------------------------------------------------------------------------

    private static List<PddlAction> Forbid(TacticalConstraint constraint, List<PddlAction> actions, IReadOnlyList<PddlAction> members)
    {
        var result = new List<PddlAction>(actions);

        foreach (var member in members)
        {
            int index = result.IndexOf(member);

            // Interdire une classe ne retire pas le schema : il n'en reste que la moitie qui ne va
            // pas la ou le joueur ne veut pas qu'on aille.
            var side = constraint.Target!.IsClass ? SideOf(constraint.Target, member) : Side.Inside;

            if (side == Side.Outside) continue;
            if (side == Side.Straddles && Split(constraint, member, result) is { } halves) result[index] = halves.Outside;
            else result.RemoveAt(index);
        }

        return result;
    }

    /// <summary>
    /// Un but devient inatteignable quand plus aucune action ne produit ce qu'il exige, alors que le
    /// domaine d'origine en avait. La nuance compte : un predicat que personne n'a jamais produit est
    /// pose par le moteur, pas par le domaine — c'est la classe de faux positifs que la spec G
    /// documente, et il n'y a pas lieu de refuser une contrainte a cause d'elle.
    /// </summary>
    private static string? Unreachable(PddlDomain source, IReadOnlyList<PddlAction> compiled, PlanningGoal? goal)
    {
        if (goal is null) return null;

        foreach (var condition in goal.ParsedConditions)
        {
            if (!condition.Expected) continue;

            var wanted = PddlAtom.Parse(condition.Pattern);
            if (!source.Actions.Any(action => Establishes(action, wanted))) continue;
            if (compiled.Any(action => Establishes(action, wanted))) continue;

            return $"Plus aucune action ne produit « {wanted.AsFact} », que l'objectif « {goal.Name} » exige.";
        }

        return null;
    }

    // --- require_always -----------------------------------------------------------------------

    /// <summary>
    /// Regression de <c>P</c> a travers chaque schema. Elle est simple ici parce que tous les effets
    /// du domaine sont des litteraux : regresser un litteral positif <c>P</c> a travers un schema vaut
    /// <i>vrai</i> si le schema ajoute <c>P</c>, <i>faux</i> s'il le retire, et <c>P</c> lui-meme
    /// sinon.
    ///
    /// <para>
    /// Le seul cas particulier est l'effet universel de la spec G, <c>(forall (?l - location) (not (at
    /// agent ?l)))</c>, qui retire le fait pour toute instance : une contrainte <c>require_always</c>
    /// sur un tel predicat rendrait tout deplacement impossible. Le compilateur doit le detecter et le
    /// dire — pas produire un domaine mort ou le survivant reste plante sans que personne sache
    /// pourquoi.
    /// </para>
    /// </summary>
    private static List<PddlAction>? Always(
        TacticalConstraint constraint,
        List<PddlAction> actions,
        List<string> warnings)
    {
        var required = constraint.Predicate!;
        var result = new List<PddlAction>();

        foreach (var action in actions)
        {
            if (DeletesUniversally(action, required))
            {
                warnings.Add(
                    $"« {action.Name} » retire « {required.AsFact} » pour toutes ses instances (effet universel) : "
                    + "exiger ce fait a chaque etat interdirait toute action de ce type.");
                return null;
            }

            if (Establishes(action, required))
            {
                result.Add(action);
                continue;
            }

            if (Deletes(action, required))
            {
                warnings.Add($"« {constraint.Text} » retire « {action.Name} » du domaine : cette action defait le fait exige.");
                continue;
            }

            result.Add(action.With(precondition: action.Precondition.Conjoin(required), generatedBy: constraint.Text));
        }

        if (result.Count != 0) return result;

        warnings.Add($"« {constraint.Text} » ne laisse aucune action jouable.");
        return null;
    }

    // --- penalize -----------------------------------------------------------------------------

    private static List<PddlAction> Penalize(
        TacticalConstraint constraint,
        List<PddlAction> actions,
        IReadOnlyList<PddlAction> members,
        CompilationContext context,
        List<string> warnings)
    {
        if (!context.Search.UsesCost())
        {
            warnings.Add(
                $"« {constraint.Text} » : la recherche active ({context.Search}) ordonne sur h seul et ignore g — "
                + "le cout est exact dans le PDDL et sans effet sur le plan choisi en jeu.");
        }

        var result = new List<PddlAction>(actions);

        foreach (var member in members)
        {
            int index = result.IndexOf(member);

            var side = constraint.Target!.IsClass ? SideOf(constraint.Target, member) : Side.Inside;

            if (side == Side.Outside) continue;

            if (side == Side.Straddles && Split(constraint, member, result) is { } halves)
            {
                result[index] = halves.Outside;
                result.Insert(index + 1, halves.Inside.With(cost: halves.Inside.Cost * constraint.Factor));
                continue;
            }

            result[index] = member.With(cost: member.Cost * constraint.Factor, generatedBy: Origin(member, constraint));
        }

        return result;
    }

    // --- contradictions et litteraux ------------------------------------------------------------

    /// <summary>
    /// Les contradictions syntaxiques : interdire une action et en exiger quelque chose. Elles se
    /// voient sans raisonner sur le monde, donc avant de compiler quoi que ce soit, et le refus nomme
    /// les deux contraintes — c'est la difference entre « impossible » et une phrase utile.
    /// </summary>
    private static IEnumerable<string> Contradictions(IReadOnlyList<TacticalConstraint> constraints)
    {
        foreach (var forbidden in constraints.Where(constraint => constraint.Form == ConstraintForm.Forbid))
        {
            foreach (var other in constraints)
            {
                if (ReferenceEquals(other, forbidden)) continue;
                if (other.Form == ConstraintForm.Forbid) continue;
                if (other.Target is null) continue;
                if (!other.Target.Text.Equals(forbidden.Target!.Text, StringComparison.OrdinalIgnoreCase)) continue;

                yield return $"« {forbidden.Text} » et « {other.Text} » se contredisent : "
                    + "la premiere retire du domaine l'action dont la seconde attend quelque chose.";
            }
        }
    }

    private static bool Establishes(PddlAction action, PddlAtom wanted)
        => action.Effect.Literals.OfType<PddlAtom>().Any(atom => Covers(atom, wanted));

    private static bool Deletes(PddlAction action, PddlAtom wanted)
        => action.Effect.Literals.Any(literal => literal switch
        {
            PddlNegation { Inner: PddlAtom removed } => Covers(removed, wanted),
            PddlUniversal { Body: PddlNegation { Inner: PddlAtom removed } } => Covers(removed, wanted),
            _ => false,
        });

    private static bool DeletesUniversally(PddlAction action, PddlAtom wanted)
        => action.Effect.Literals.Any(literal =>
            literal is PddlUniversal { Body: PddlNegation { Inner: PddlAtom removed } } && Covers(removed, wanted));

    /// <summary>
    /// Vrai si le litteral du schema — variables comprises — peut valoir le fait cherche. Une variable
    /// couvre n'importe quelle constante : c'est le sens de « ce schema peut poser ce fait ».
    /// </summary>
    private static bool Covers(PddlAtom general, PddlAtom wanted)
    {
        if (!general.Predicate.Equals(wanted.Predicate, StringComparison.OrdinalIgnoreCase)) return false;
        if (general.Arity != wanted.Arity) return false;

        for (int position = 0; position < general.Arity; position++)
        {
            string argument = general.Arguments[position];
            if (argument.StartsWith('?') || argument == Fact.Wildcard) continue;
            if (!argument.Equals(wanted.Arguments[position], StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }
}

using System.Globalization;
using Hordinary.AI.Planning;
using Hordinary.AI.Planning.Pddl;

namespace Hordinary.AI.Instructions;

/// <summary>
/// Les cinq formes de la grammaire fermee du livrable 15, et rien d'autre. C'est le premier
/// garde-fou, et le plus efficace : la surface qu'un modele de langage peut produire est minuscule,
/// et tout ce qui n'entre pas dans ces cinq cases est refuse avant d'atteindre le moindre
/// planificateur.
/// </summary>
public enum ConstraintForm
{
    /// <summary>« A n'est jouable que si P est vrai » — modalite PDDL 3 <c>sometime-before</c>.</summary>
    RequireBefore = 0,

    /// <summary>« A doit poser P » — modalite PDDL 3 <c>sometime-after</c>.</summary>
    RequireAfter,

    /// <summary>« ne fais jamais A » — pas une modalite PDDL 3 : un retrait de schema.</summary>
    Forbid,

    /// <summary>« P doit etre vrai a chaque etat » — modalite PDDL 3 <c>always</c>.</summary>
    RequireAlways,

    /// <summary>« evite A » — pas une modalite PDDL 3 : une preference, portee par <c>:action-costs</c>.</summary>
    Penalize,
}

/// <summary>
/// Ce qu'une contrainte vise : un schema d'action declare (<c>ForceDoor</c>), ou une classe de
/// schemas designee par la categorie du lieu vers lequel ils emmenent (<c>enter:building</c>).
/// </summary>
public sealed record ActionTarget(string? Schema, string? Family, string? Category)
{
    public bool IsClass => Family is not null;

    public string Text => IsClass ? $"{Family}:{Category}" : Schema ?? "?";

    public static ActionTarget OfSchema(string schema) => new(schema, null, null);

    public static ActionTarget OfClass(string family, string category) => new(null, family, category);

    public override string ToString() => Text;
}

/// <summary>
/// Les classes de schemas nommables par une instruction.
///
/// <para>
/// <c>move</c> et <c>enter</c> designent le <b>meme</b> ensemble de schemas, et c'est voulu : PDDL ne
/// connait pas la notion d'« entrer ». Un domaine sait seulement qu'une action emmene l'agent
/// quelque part. Ce qui distingue « entrer dans un batiment » de « traverser une rue » n'est pas le
/// verbe mais la <b>categorie du lieu vise</b> — d'ou la seconde moitie de la classe. Les deux mots
/// existent parce qu'un joueur ecrit l'un ou l'autre, pas parce qu'ils selectionnent autre chose.
/// </para>
/// </summary>
public static class ActionFamilies
{
    public const string Move = "move";
    public const string Enter = "enter";

    public static IReadOnlySet<string> Names { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Move, Enter };

    /// <summary>
    /// Le parametre de lieu vers lequel le schema emmene l'agent, s'il y en a un.
    ///
    /// La reconnaissance ne nomme aucun predicat : un schema de deplacement est celui qui <b>remplace
    /// une position</b> — il retire un predicat pour toutes ses instances (l'effet universel issu du
    /// joker <c>*</c>) et le repose sur un de ses parametres de type <c>location</c>. Ecrire
    /// « le predicat <c>at</c> » ici recoderait en dur un vocabulaire qui vit dans
    /// <c>config/planner_domain.json</c>.
    /// </summary>
    public static PddlParameter? DestinationOf(PddlAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        foreach (var literal in action.Effect.Literals)
        {
            if (literal is not PddlUniversal { Body: PddlNegation { Inner: PddlAtom removed } } universal) continue;
            if (!universal.Variable.Type.Equals(PlanningObjectTypes.Location, StringComparison.OrdinalIgnoreCase)) continue;

            int position = removed.Arguments.ToList().FindIndex(argument => argument.Equals(universal.Variable.Name, StringComparison.OrdinalIgnoreCase));
            if (position < 0) continue;

            foreach (var other in action.Effect.Literals)
            {
                if (other is not PddlAtom added) continue;
                if (!added.Predicate.Equals(removed.Predicate, StringComparison.OrdinalIgnoreCase)) continue;
                if (position >= added.Arity) continue;

                string destination = added.Arguments[position];
                if (!destination.StartsWith('?')) continue;

                var parameter = action.Parameters.FirstOrDefault(p => p.Name.Equals(destination, StringComparison.OrdinalIgnoreCase));
                if (parameter.Name is not null && parameter.Type.Equals(PlanningObjectTypes.Location, StringComparison.OrdinalIgnoreCase))
                    return parameter;
            }
        }

        return null;
    }

    /// <summary>Le litteral que le schema pose en arrivant, sa destination remplacee par la variable donnee.</summary>
    public static PddlAtom? ArrivalAtom(PddlAction action, string variable)
    {
        if (DestinationOf(action) is not { } destination) return null;

        foreach (var literal in action.Effect.Literals)
        {
            if (literal is not PddlAtom atom) continue;
            if (!atom.Arguments.Any(argument => argument.Equals(destination.Name, StringComparison.OrdinalIgnoreCase))) continue;

            return new PddlAtom(atom.Predicate, [.. atom.Arguments.Select(argument =>
                argument.Equals(destination.Name, StringComparison.OrdinalIgnoreCase) ? variable : argument)]);
        }

        return null;
    }

    /// <summary>Les schemas que la classe designe, dans l'ordre du domaine.</summary>
    public static IEnumerable<PddlAction> Members(PddlDomain domain, ActionTarget target)
    {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(target);

        return target.IsClass
            ? domain.Actions.Where(action => DestinationOf(action) is not null)
            : domain.Actions.Where(action => action.Name.Equals(target.Schema, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Une contrainte tactique : ce que devient la phrase du joueur une fois passee le portillon 1.
///
/// <para>
/// Elle a <b>deux rendus PDDL</b>, et c'est toute l'idee du livrable. La forme compilee — des
/// preconditions, dans le fragment ADL — est donnee au planificateur (<c>DomainCompiler</c>). La
/// forme declarative — <see cref="Declarative"/>, un <c>(:constraints (sometime-before …))</c> PDDL 3
/// — n'est donnee a personne qui planifie : elle existe pour verifier la premiere (portillon 4).
/// Une compilation fausse produit un plan valide au regard du domaine reecrit et faux au regard de la
/// phrase du joueur ; rien d'autre dans la chaine ne le voit.
/// </para>
/// </summary>
public sealed class TacticalConstraint
{
    private TacticalConstraint(ConstraintForm form, PddlAtom? predicate, ActionTarget? target, float factor, string? utterance)
    {
        Form = form;
        Predicate = predicate;
        Target = target;
        Factor = factor;
        Utterance = utterance;
    }

    public ConstraintForm Form { get; }

    /// <summary>Le fait exige, pour les trois formes qui en portent un.</summary>
    public PddlAtom? Predicate { get; }

    /// <summary>L'action ou la classe visee, pour les quatre formes qui en portent une.</summary>
    public ActionTarget? Target { get; }

    /// <summary>Le multiplicateur de cout de <see cref="ConstraintForm.Penalize"/>.</summary>
    public float Factor { get; }

    /// <summary>La phrase du joueur, quand elle est connue. Le journal la garde a cote du verdict.</summary>
    public string? Utterance { get; }

    public static TacticalConstraint RequireBefore(PddlAtom predicate, ActionTarget target, string? utterance = null)
        => new(ConstraintForm.RequireBefore, predicate, target, 1f, utterance);

    public static TacticalConstraint RequireAfter(PddlAtom predicate, ActionTarget target, string? utterance = null)
        => new(ConstraintForm.RequireAfter, predicate, target, 1f, utterance);

    public static TacticalConstraint Forbid(ActionTarget target, string? utterance = null)
        => new(ConstraintForm.Forbid, null, target, 1f, utterance);

    public static TacticalConstraint RequireAlways(PddlAtom predicate, string? utterance = null)
        => new(ConstraintForm.RequireAlways, predicate, null, 1f, utterance);

    public static TacticalConstraint Penalize(ActionTarget target, float factor, string? utterance = null)
        => new(ConstraintForm.Penalize, null, target, factor, utterance);

    public TacticalConstraint WithUtterance(string? utterance)
        => new(Form, Predicate, Target, Factor, utterance);

    /// <summary>
    /// La contrainte dans la grammaire fermee, telle qu'elle se relit et se rejoue :
    /// <c>require_before(at(agent, group), enter:building)</c>. C'est cette chaine qui est figee dans
    /// la sauvegarde de mission — rejouer ne reinterroge aucun modele.
    /// </summary>
    public string Text => Form switch
    {
        ConstraintForm.RequireBefore => $"require_before({Predicate!.AsFact}, {Target!.Text})",
        ConstraintForm.RequireAfter => $"require_after({Predicate!.AsFact}, {Target!.Text})",
        ConstraintForm.Forbid => $"forbid({Target!.Text})",
        ConstraintForm.RequireAlways => $"require_always({Predicate!.AsFact})",
        ConstraintForm.Penalize => $"penalize({Target!.Text}, {Factor.ToString("0.###", CultureInfo.InvariantCulture)})",
        _ => "?",
    };

    /// <summary>
    /// Le rendu declaratif PDDL 3 : ce que le joueur voulait dire, dans le langage ou cela se dit.
    ///
    /// <para>
    /// Deux des cinq formes n'y ont pas d'equivalent et le disent (<see cref="PddlUnexpressible"/>)
    /// plutot que de faire semblant. Une troisieme est une approximation assumee : quand la
    /// contrainte vise un schema nomme plutot qu'une classe de deplacement, le declencheur ne peut
    /// pas etre « cette action vient d'etre jouee » — PDDL 3 parle d'etats, pas d'actions. On prend
    /// alors ses effets positifs comme temoin de son passage. Le verificateur de trajectoire, lui,
    /// travaille sur le plan et connait l'identite des actions : il est plus precis que ce rendu, qui
    /// ne sert qu'a la corroboration exterieure par VAL.
    /// </para>
    /// </summary>
    public PddlConstraint Declarative(PddlDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);

        switch (Form)
        {
            case ConstraintForm.RequireAlways:
                return new PddlAlways(Predicate!);

            case ConstraintForm.Forbid:
                return new PddlUnexpressible($"{Text} — retrait de schema, sans equivalent PDDL 3.");

            case ConstraintForm.Penalize:
                return new PddlUnexpressible($"{Text} — preference de cout, sans equivalent PDDL 3.");

            case ConstraintForm.RequireBefore:
            case ConstraintForm.RequireAfter:
                return Temporal(domain);

            default:
                return new PddlUnexpressible($"{Text} — forme inconnue.");
        }
    }

    private PddlConstraint Temporal(PddlDomain domain)
    {
        bool before = Form == ConstraintForm.RequireBefore;

        if (Target!.IsClass)
        {
            var variable = new PddlParameter("?x", PlanningObjectTypes.Location);
            var member = ActionFamilies.Members(domain, Target).FirstOrDefault();
            var arrival = member is null ? null : ActionFamilies.ArrivalAtom(member, variable.Name);

            if (arrival is null) return new PddlUnexpressible($"{Text} — aucun schema de deplacement a decrire.");

            var trigger = new PddlConjunction([new PddlAtom(Target.Category!, variable.Name), arrival]);

            return new PddlUniversalConstraint(
                variable,
                before ? new PddlSometimeBefore(trigger, Predicate!) : new PddlSometimeAfter(trigger, Predicate!));
        }

        var schema = domain.FindAction(Target.Schema);
        if (schema is null) return new PddlUnexpressible($"{Text} — schema inconnu.");

        var witness = new PddlConjunction(schema.Effect.Literals.OfType<PddlAtom>());

        return before
            ? new PddlSometimeBefore(witness, Predicate!)
            : new PddlSometimeAfter(witness, Predicate!);
    }

    public override string ToString() => Text;
}

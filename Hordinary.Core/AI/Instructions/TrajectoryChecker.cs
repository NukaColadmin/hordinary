using Hordinary.AI.Planning;
using Hordinary.AI.Planning.Pddl;

namespace Hordinary.AI.Instructions;

/// <summary>
/// Un plan rejoue etat par etat : <c>States[i]</c> est l'etat avant <c>Actions[i]</c>, et
/// <c>States[^1]</c> l'etat final. C'est la trajectoire au sens de PDDL 3, la suite d'etats sur
/// laquelle une contrainte porte.
/// </summary>
public sealed class TrajectoryTrace
{
    private TrajectoryTrace(IReadOnlyList<SymbolicState> states, IReadOnlyList<PlanningAction> actions)
    {
        States = states;
        Actions = actions;
    }

    public IReadOnlyList<SymbolicState> States { get; }

    public IReadOnlyList<PlanningAction> Actions { get; }

    /// <summary>Deroule les effets des actions depuis l'etat initial, sans rien demander au moteur.</summary>
    public static TrajectoryTrace Replay(SymbolicState initial, IEnumerable<PlanningAction> actions)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(actions);

        var states = new List<SymbolicState> { initial };
        var played = new List<PlanningAction>();
        var current = initial;

        foreach (var action in actions)
        {
            current = action.ApplyTo(current);
            states.Add(current);
            played.Add(action);
        }

        return new TrajectoryTrace(states, played);
    }

    public static TrajectoryTrace Of(Plan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Replay(plan.Problem.Initial, plan.Actions);
    }
}

/// <summary>Le verdict du portillon 4, et les violations relevees — une phrase par contrainte trahie.</summary>
public readonly record struct TrajectoryVerdict(bool Satisfied, IReadOnlyList<string> Violations)
{
    public override string ToString()
        => Satisfied ? "trajectoire conforme" : string.Join(" ", Violations);
}

/// <summary>
/// Portillon 4 : la conformite. Le portillon 3 verifie le domaine compile ; celui-ci verifie <b>la
/// compilation elle-meme</b>, ce qui est un probleme different et va dans l'autre sens.
///
/// <para>
/// Il rejoue le plan produit et controle qu'il satisfait la forme <b>declarative</b> de chaque
/// contrainte — celle qu'aucun planificateur n'a vue. Une compilation fausse produit un plan valide
/// au regard du domaine reecrit et faux au regard de la phrase du joueur : rien d'autre dans la
/// chaine ne le voit.
/// </para>
///
/// <para>
/// Il interprete les modalites PDDL 3 utilisees — <c>sometime-before</c>, <c>sometime-after</c>,
/// <c>always</c>, plus le cas degenere du retrait de schema — <b>dans leur lecture large</b>, celle du
/// langage, et non dans la lecture stricte de la compilation. C'est delibere : un plan qui rejoint le
/// groupe, traverse une rue puis entre dans le batiment satisfait <c>sometime-before</c> et non la
/// forme compilee. Le verificateur doit l'accepter, sans quoi il ne verifierait plus la contrainte
/// mais sa traduction — et l'incompletude assumee du livrable deviendrait invisible.
/// </para>
///
/// <para>
/// Il n'a besoin d'aucun analyseur syntaxique et d'aucun binaire : il lit la structure, jamais du
/// texte PDDL.
/// </para>
/// </summary>
public static class TrajectoryChecker
{
    public static TrajectoryVerdict Check(
        PddlDomain domain,
        IReadOnlyList<TacticalConstraint> constraints,
        TrajectoryTrace trace)
    {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(constraints);
        ArgumentNullException.ThrowIfNull(trace);

        var violations = new List<string>();

        foreach (var constraint in constraints)
        {
            switch (constraint.Form)
            {
                case ConstraintForm.RequireAlways:
                    Always(constraint, trace, violations);
                    break;

                case ConstraintForm.Forbid:
                    Forbidden(constraint, domain, trace, violations);
                    break;

                case ConstraintForm.RequireBefore:
                    Before(constraint, domain, trace, violations);
                    break;

                case ConstraintForm.RequireAfter:
                    After(constraint, domain, trace, violations);
                    break;

                case ConstraintForm.Penalize:
                    // Une preference de cout ne dit rien de la trajectoire : il n'y a rien a verifier,
                    // et pretendre le contraire serait le seul moyen de rendre ce portillon menteur.
                    break;
            }
        }

        return new TrajectoryVerdict(violations.Count == 0, violations);
    }

    private static void Always(TacticalConstraint constraint, TrajectoryTrace trace, List<string> violations)
    {
        string fact = constraint.Predicate!.AsFact;

        for (int index = 0; index < trace.States.Count; index++)
        {
            if (trace.States[index].Holds(fact)) continue;

            violations.Add($"« {constraint.Text} » : « {fact} » est faux a l'etat {index} du plan.");
            return;
        }
    }

    private static void Forbidden(TacticalConstraint constraint, PddlDomain domain, TrajectoryTrace trace, List<string> violations)
    {
        for (int index = 0; index < trace.Actions.Count; index++)
        {
            if (!Matches(constraint, domain, trace, index)) continue;

            violations.Add($"« {constraint.Text} » : le plan joue « {trace.Actions[index].Name} » au pas {index + 1}.");
            return;
        }
    }

    /// <summary>
    /// <c>sometime-before</c> : si l'action visee est jouee au pas <c>i</c>, le fait exige a ete vrai
    /// dans un etat <b>quelconque</b> jusqu'a l'etat <c>i</c> compris — donc au plus tard juste avant
    /// de la jouer.
    /// </summary>
    private static void Before(TacticalConstraint constraint, PddlDomain domain, TrajectoryTrace trace, List<string> violations)
    {
        string fact = constraint.Predicate!.AsFact;

        for (int index = 0; index < trace.Actions.Count; index++)
        {
            if (!Matches(constraint, domain, trace, index)) continue;

            bool held = false;
            for (int earlier = 0; earlier <= index && !held; earlier++) held = trace.States[earlier].Holds(fact);

            if (held) continue;

            violations.Add(
                $"« {constraint.Text} » : « {trace.Actions[index].Name} » est jouee au pas {index + 1} "
                + $"sans que « {fact} » ait jamais ete vrai avant.");
            return;
        }
    }

    /// <summary><c>sometime-after</c> : le fait exige devient vrai a un moment quelconque apres l'action.</summary>
    private static void After(TacticalConstraint constraint, PddlDomain domain, TrajectoryTrace trace, List<string> violations)
    {
        string fact = constraint.Predicate!.AsFact;

        for (int index = 0; index < trace.Actions.Count; index++)
        {
            if (!Matches(constraint, domain, trace, index)) continue;

            bool held = false;
            for (int later = index + 1; later < trace.States.Count && !held; later++) held = trace.States[later].Holds(fact);

            if (held) continue;

            violations.Add(
                $"« {constraint.Text} » : « {trace.Actions[index].Name} » est jouee au pas {index + 1} "
                + $"sans que « {fact} » ne devienne jamais vrai ensuite.");
            return;
        }
    }

    /// <summary>
    /// Le pas du plan tombe-t-il sous la contrainte ?
    ///
    /// <para>
    /// Pour un schema nomme, la question se pose aussi de la <b>moitie</b> qu'une scission en a tiree :
    /// <c>EnterBuilding</c> est une instance de <c>MoveToLocation</c>, et une contrainte qui vise la
    /// seconde doit voir la premiere. C'est <see cref="PddlAction.Origin"/> qui le dit — la couche
    /// execution, celle que PDDL n'ecrit pas, sert ici a ne pas perdre la filiation.
    /// </para>
    ///
    /// <para>
    /// Pour une classe, la question est celle du lieu vise : l'action emmene-t-elle l'agent vers un
    /// lieu de cette categorie, d'apres l'etat ou elle est jouee ? Une carte qui n'a jamais pose
    /// <c>building(entrepot_ouest)</c> ne fait tomber aucun pas sous la contrainte, ce qui est la
    /// traduction exacte de « acceptee, sans effet ici ».
    /// </para>
    /// </summary>
    private static bool Matches(TacticalConstraint constraint, PddlDomain domain, TrajectoryTrace trace, int index)
    {
        var action = trace.Actions[index];
        var schema = domain.FindAction(action.Schema.Name);
        var target = constraint.Target!;

        if (!target.IsClass)
        {
            return action.Schema.Name.Equals(target.Schema, StringComparison.OrdinalIgnoreCase)
                || (schema?.Origin?.Name.Equals(target.Schema, StringComparison.OrdinalIgnoreCase) ?? false);
        }

        if (schema is null || ActionFamilies.DestinationOf(schema) is not { } destination) return false;
        if (!action.Bindings.TryGetValue(destination.Variable, out string? place)) return false;

        return trace.States[index].Holds(new PddlAtom(target.Category!, place).AsFact);
    }
}

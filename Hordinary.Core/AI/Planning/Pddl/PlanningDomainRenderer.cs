namespace Hordinary.AI.Planning.Pddl;

/// <summary>
/// Le second rendu d'un <see cref="PddlDomain"/> : celui que GBFS consomme.
///
/// <para>
/// Ce n'est pas un import PDDL et la spec G ne s'en trouve pas elargie : rien ici ne lit du texte.
/// Le chemin est <c>planner_domain.json → PddlDomain → { texte pour Fast Downward, PlanningDomain
/// pour GBFS }</c>, et cette fleche-ci est un rendu en memoire, pas une analyse syntaxique.
/// </para>
///
/// <para>
/// C'est ce rendu qui fait que le livrable 15 change reellement le comportement du jeu : le domaine
/// compile par une contrainte du joueur revient ici, et le survivant planifie dessus. Sans lui, la
/// contrainte serait vraie dans un fichier et fausse dans la partie.
/// </para>
/// </summary>
public static class PlanningDomainRenderer
{
    public static PlanningDomain ToPlanningDomain(this PddlDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);

        return new PlanningDomain
        {
            Actions = [.. domain.Actions.Select(ToSchema)],
            Goals = [.. domain.Goals],
            Places = new PlanningPlaces
            {
                Kinds = [.. domain.LocationKinds],
                Landmarks = [.. domain.Landmarks],
            },
        };
    }

    private static ActionSchema ToSchema(PddlAction action)
    {
        var origin = action.Origin
            ?? throw new InvalidOperationException(
                $"Le schema « {action.Name} » n'a pas d'origine executable : PDDL ne dit pas par quel verbe le jouer. "
                + "Un schema produit par une contrainte doit heriter de l'origine de celui qu'il scinde.");

        return new ActionSchema
        {
            Name = action.Name,
            Verb = origin.Verb,
            Params = origin.Params,
            Parameters = action.Parameters.ToDictionary(parameter => parameter.Variable, parameter => parameter.Type, StringComparer.Ordinal),
            Preconditions = [.. action.Precondition.Literals.Select(Write)],
            Effects = [.. action.Effect.Literals.Select(Write)],
            Cost = action.Cost,
            DistanceCost = origin.DistanceCost,
            NoiseLevel = origin.NoiseLevel,
            NoiseAction = origin.NoiseAction,
            Duration = origin.Duration,
            Enabled = true,
        };
    }

    /// <summary>
    /// Un litteral PDDL redevient un fait de la grammaire du jeu. L'effet universel refait le chemin
    /// inverse de la spec G : <c>(forall (?l - location) (not (at agent ?l)))</c> redevient le joker
    /// <c>!at(agent, *)</c>, qui est la facon dont <see cref="SymbolicState.Apply"/> sait oublier
    /// toute position memorisee.
    /// </summary>
    private static string Write(PddlFormula literal)
        => literal switch
        {
            PddlAtom atom => atom.AsFact,
            PddlNegation { Inner: PddlAtom atom } => $"!{atom.AsFact}",
            PddlUniversal { Body: PddlNegation { Inner: PddlAtom atom } } universal
                => $"!{Wildcard(atom, universal.Variable).AsFact}",
            _ => throw new InvalidOperationException(
                $"« {literal.Canonical} » n'a pas d'equivalent dans la grammaire des faits du jeu. "
                + "Le domaine du jeu est un domaine de litteraux : conjonctions de litteraux en precondition, "
                + "litteraux et suppressions universelles en effet."),
        };

    private static PddlAtom Wildcard(PddlAtom atom, PddlParameter variable)
        => new(atom.Predicate, [.. atom.Arguments.Select(argument =>
            argument.Equals(variable.Name, StringComparison.OrdinalIgnoreCase) ? Fact.Wildcard : argument)]);
}

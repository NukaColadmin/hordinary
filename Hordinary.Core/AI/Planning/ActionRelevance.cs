namespace Hordinary.AI.Planning;

/// <summary>
/// Ecarte, avant la recherche, les actions qui ne peuvent servir a rien pour le but demande.
///
/// Le domaine livre compte une vingtaine de schemas, declines en plusieurs dizaines d'actions
/// concretes. Sans tri, GBFS passerait son budget de noeuds a envisager d'observer une rue, de faire
/// du bruit ou de poser son sac pendant qu'on lui demande d'aller chercher des medicaments : ces
/// actions changent l'etat, donc creent des noeuds, mais aucune ne rapproche du but.
///
/// Le tri est un simple parcours arriere : on part des faits que le but reclame, on garde les actions
/// qui savent les produire, on ajoute leurs preconditions aux faits reclames, et on recommence
/// jusqu'au point fixe. Une action ecartee ici n'aurait, par construction, pu apparaitre dans aucun
/// plan valide — le tri ne change donc aucun resultat, seulement le temps pour y arriver.
/// </summary>
public static class ActionRelevance
{
    public static IReadOnlyList<PlanningAction> Filter(
        IReadOnlyList<PlanningAction> actions,
        IReadOnlyList<Condition> goal)
    {
        var needed = new HashSet<(string Fact, bool Polarity)>();
        foreach (var condition in goal) needed.Add((condition.Pattern, condition.Expected));

        var relevant = new HashSet<PlanningAction>();
        bool changed = true;

        while (changed)
        {
            changed = false;

            foreach (var action in actions)
            {
                if (relevant.Contains(action) || !Achieves(action, needed)) continue;

                relevant.Add(action);
                changed = true;

                foreach (var precondition in action.Preconditions)
                {
                    needed.Add((precondition.Pattern, precondition.Expected));
                }
            }
        }

        // L'ordre du fichier de domaine est conserve : c'est lui qui departage deux plans de meme
        // estimation, et donc ce qui rend une planification reproductible.
        return [.. actions.Where(relevant.Contains)];
    }

    /// <summary>Vrai si l'action peut produire au moins un des litteraux reclames.</summary>
    private static bool Achieves(PlanningAction action, HashSet<(string Fact, bool Polarity)> needed)
    {
        foreach (var effect in action.Effects)
        {
            if (effect.Value)
            {
                if (needed.Contains((effect.Pattern, true))) return true;
                continue;
            }

            // Un effet de suppression rend faux tout fait qu'il recouvre — joker compris.
            foreach (var (fact, polarity) in needed)
            {
                if (!polarity && Fact.Matches(effect.Pattern, fact)) return true;
            }
        }

        return false;
    }
}

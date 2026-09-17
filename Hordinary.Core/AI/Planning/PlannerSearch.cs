namespace Hordinary.AI.Planning;

/// <summary>
/// La fonction de priorite de la recherche de plan, reglee par <c>planner.search</c> dans
/// <c>config/game_config.json</c>.
///
/// Les deux strategies partagent la meme boucle, la meme frontiere et les memes garde-fous : seule
/// change la facon d'ordonner la file. C'etait la raison de ne pas ecrire deux chercheurs — deux
/// boucles, c'est deux fois la meme erreur a corriger, et une seule des deux qui le serait.
/// </summary>
public enum PlannerSearch
{
    /// <summary>
    /// Greedy Best-First Search : priorite = <c>h</c>, le cout ne fait que departager les ex aequo.
    /// C'est la strategie de la specification (livrable 5) et celle du jeu livre.
    /// </summary>
    Gbfs = 0,

    /// <summary>
    /// A* : priorite = <c>g + h</c>, le cout deja paye compte autant que l'estimation restante. Plus
    /// de noeuds explores pour des plans moins chers.
    ///
    /// Sans garantie d'optimalite pour autant : <see cref="Heuristic"/> ajoute au nombre de
    /// conditions non satisfaites des termes de distance, de danger et de bruit ponderes par la
    /// configuration, et rien ne borne leur somme par le cout reel. C'est donc un A* au sens de la
    /// fonction de priorite, pas au sens du theoreme.
    /// </summary>
    AStar = 1,
}

/// <summary>Ce que la strategie retenue fait — ou ne fait pas — du cout d'un plan.</summary>
public static class PlannerSearches
{
    /// <summary>
    /// Vrai si la recherche ordonne la file sur <c>g</c>, donc si le cout d'une action change le plan
    /// choisi. Faux pour GBFS, ou le cout ne fait que departager des noeuds de meme <c>h</c>.
    ///
    /// <para>
    /// La question n'est pas academique : c'est elle qui decide si la contrainte <c>penalize(A, k)</c>
    /// du livrable 15 agit ou se contente d'etre exacte dans le PDDL. Sous GBFS, le compilateur
    /// accepte la contrainte et previent qu'elle est sans effet sur ce moteur de recherche ; sous A*,
    /// il n'a rien a signaler. Cela se lit dans <c>planner.search</c> et nulle part ailleurs.
    /// </para>
    /// </summary>
    public static bool UsesCost(this PlannerSearch search) => search == PlannerSearch.AStar;
}

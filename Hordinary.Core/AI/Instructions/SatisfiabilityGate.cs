namespace Hordinary.AI.Instructions;

/// <summary>
/// Ce qu'un planificateur repond sur un jeu de contraintes : le domaine qu'elles produisent
/// laisse-t-il encore un plan ?
///
/// <para>
/// C'est un delegue, et pour la meme raison que <see cref="InstructionTransport"/> : le
/// planificateur tiers est un binaire hors ligne, <c>Hordinary.Core</c> n'en lance aucun, et la
/// recherche du coupable minimal — la seule partie qui demande a reflechir — se teste alors sans lui.
/// </para>
/// </summary>
/// <param name="constraints">Le sous-ensemble a eprouver. Vide : le domaine d'origine.</param>
/// <returns><c>true</c> si un plan existe encore.</returns>
public delegate bool SatisfiabilityOracle(IReadOnlyList<TacticalConstraint> constraints);

/// <summary>Ce que le portillon 3 rend : un verdict, et le coupable quand il y en a un.</summary>
/// <param name="Culprits">
/// Le plus petit sous-ensemble de contraintes qui rend deja la mission impossible. Vide si tout va
/// bien — ou si c'est la mission elle-meme qui n'a pas de solution, ce que <see cref="Blameless"/>
/// distingue.
/// </param>
/// <param name="Blameless">
/// Vrai quand le domaine <b>sans aucune contrainte</b> n'a deja pas de plan. Accuser une instruction
/// dans ce cas serait une erreur judiciaire : le joueur retirerait une consigne innocente et
/// verrait la mission rester impossible.
/// </param>
public sealed record SatisfiabilityVerdict(
    bool Satisfiable,
    IReadOnlyList<TacticalConstraint> Culprits,
    bool Blameless,
    int Probes)
{
    public string Reason => Satisfiable
        ? "Un plan reste possible."
        : Blameless
            ? "La mission n'a pas de solution, meme sans instruction : aucune consigne n'est en cause."
            : $"Plus aucun plan n'existe. En cause : {string.Join(", ", Culprits.Select(c => c.Text))}.";

    public override string ToString() => Reason;
}

/// <summary>
/// Portillon 3 : la satisfiabilite, et la recherche du coupable minimal.
///
/// <para>
/// Le portillon lui-meme est un planificateur tiers — Fast Downward — donc <b>hors ligne par
/// construction</b> : aucun planificateur tiers ne tourne pendant une partie. Ce qui vit ici est ce
/// qui reste quand on le retire : la strategie d'interrogation, qui est la partie interessante et la
/// seule qui puisse etre fausse en silence.
/// </para>
///
/// <para>
/// <b>Pourquoi un coupable minimal plutot qu'un « non ».</b> « Votre consigne rend la mission
/// impossible » avec cinq consignes actives ne dit rien : le joueur les retire au hasard. Le refus
/// n'a de valeur que s'il nomme le plus petit ensemble qui suffit a bloquer — et c'est le troisieme
/// critere de validation du livrable, celui que la specification tient pour le plus important des
/// trois.
/// </para>
/// </summary>
public static class SatisfiabilityGate
{
    /// <summary>
    /// Eprouve un jeu de contraintes et, s'il bloque, en isole un sous-ensemble minimal.
    ///
    /// <para>
    /// L'algorithme est une <b>reduction gloutonne</b> : on retire une contrainte a la fois et on
    /// garde le retrait quand le reste bloque toujours. Ce qui en sort est minimal au sens de
    /// l'inclusion — aucune contrainte ne peut en etre otee sans que le blocage disparaisse — ce qui
    /// est exactement ce qu'il faut dire au joueur. Ce n'est pas le plus petit ensemble dans
    /// l'absolu : deux paires de consignes peuvent bloquer independamment, et on en nomme une.
    /// </para>
    ///
    /// <para>
    /// Le cout est de <c>n</c> appels au planificateur apres le premier, la ou une recherche
    /// exhaustive en demanderait <c>2^n</c>. Avec <c>max_active_constraints</c> a cinq, les deux
    /// tiennent — mais la difference entre « six appels » et « trente-deux » se voit quand chaque
    /// appel est un processus externe, et la minimalite obtenue est la meme dans les deux cas pour
    /// ce qu'on en fait.
    /// </para>
    /// </summary>
    public static SatisfiabilityVerdict Check(
        IReadOnlyList<TacticalConstraint> constraints,
        SatisfiabilityOracle oracle)
    {
        ArgumentNullException.ThrowIfNull(constraints);
        ArgumentNullException.ThrowIfNull(oracle);

        int probes = 1;

        if (oracle(constraints)) return new SatisfiabilityVerdict(true, [], false, probes);

        // Le domaine nu bloque deja : ce n'est pas une consigne qu'il faut accuser. La question est
        // posee en second et non en premier parce qu'elle ne se pose que sur un echec — la payer a
        // chaque instruction acceptee doublerait le cout du portillon pour rien.
        probes++;

        if (constraints.Count == 0 || !oracle([]))
            return new SatisfiabilityVerdict(false, [], Blameless: true, probes);

        var culprits = constraints.ToList();

        // Parcours dans l'ordre de pose, en tentant de retirer d'abord les plus anciennes : ce sont
        // donc les <b>dernieres</b> qui restent accusees quand plusieurs reponses minimales existent.
        // Ce n'est pas une finesse d'algorithme mais une decision d'interface — le joueur comprend
        // qu'on accuse ce qu'il vient d'ajouter, beaucoup moins qu'on accuse la premiere de sa liste.
        foreach (var suspect in constraints)
        {
            var candidate = culprits.Where(constraint => !ReferenceEquals(constraint, suspect)).ToList();

            if (candidate.Count == culprits.Count) continue;

            probes++;

            if (oracle(candidate)) continue;

            culprits = candidate;
        }

        return new SatisfiabilityVerdict(false, culprits, Blameless: false, probes);
    }
}

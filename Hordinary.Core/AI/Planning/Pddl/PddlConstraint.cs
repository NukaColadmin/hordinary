namespace Hordinary.AI.Planning.Pddl;

/// <summary>
/// Une contrainte de trajectoire PDDL 3 (Gerevini &amp; Long, 2005) : une formule qui porte sur la
/// <b>suite des etats</b> traversee par le plan, et non sur son etat final.
///
/// <para>
/// Elle n'est donnee a aucun planificateur. Ni GBFS ni Fast Downward ne savent resoudre le fragment
/// PDDL 3 ; c'est precisement pourquoi le livrable 15 <b>compile</b> ses contraintes en preconditions
/// (voir <c>DomainCompiler</c>). Cette representation-ci existe pour le portillon 4 : elle dit ce que
/// le joueur voulait, et le verificateur de trajectoire s'en sert pour juger si la forme compilee dit
/// bien la meme chose. Une compilation fausse produit un plan valide au regard du domaine reecrit et
/// faux au regard de la phrase — rien d'autre dans la chaine ne le voit.
/// </para>
/// </summary>
public abstract class PddlConstraint
{
    /// <summary>La contrainte en Lisp, telle qu'elle apparaitrait dans un bloc <c>(:constraints …)</c>.</summary>
    public abstract string Canonical { get; }

    /// <summary>
    /// Vrai si la forme appartient vraiment au fragment PDDL 3. Deux des cinq formes du livrable 15
    /// n'en sont pas — <c>forbid</c> est un retrait de schema, <c>penalize</c> une preference de cout
    /// — et il vaut mieux le dire que faire semblant : elles se rendent en commentaire.
    /// </summary>
    public virtual bool IsPddl3 => true;

    public override string ToString() => Canonical;
}

/// <summary><c>(always φ)</c> : la formule est vraie dans chaque etat du plan.</summary>
public sealed class PddlAlways(PddlFormula formula) : PddlConstraint
{
    public PddlFormula Formula { get; } = formula;

    public override string Canonical => $"(always {Formula.Canonical})";
}

/// <summary>
/// <c>(sometime-before φ ψ)</c> : si <c>φ</c> devient vraie, <c>ψ</c> l'a ete a un moment
/// <b>quelconque</b> avant. C'est plus permissif que la forme compilee, qui exige <c>ψ</c> dans
/// l'etat immediatement precedent — l'incompletude assumee du livrable 15.
/// </summary>
public sealed class PddlSometimeBefore(PddlFormula trigger, PddlFormula required) : PddlConstraint
{
    public PddlFormula Trigger { get; } = trigger;

    public PddlFormula Required { get; } = required;

    public override string Canonical => $"(sometime-before {Trigger.Canonical} {Required.Canonical})";
}

/// <summary><c>(sometime-after φ ψ)</c> : si <c>φ</c> devient vraie, <c>ψ</c> le sera a un moment quelconque ensuite.</summary>
public sealed class PddlSometimeAfter(PddlFormula trigger, PddlFormula required) : PddlConstraint
{
    public PddlFormula Trigger { get; } = trigger;

    public PddlFormula Required { get; } = required;

    public override string Canonical => $"(sometime-after {Trigger.Canonical} {Required.Canonical})";
}

/// <summary>Quantification universelle d'une contrainte : <c>(forall (?b - location) (sometime-before …))</c>.</summary>
public sealed class PddlUniversalConstraint(PddlParameter variable, PddlConstraint inner) : PddlConstraint
{
    public PddlParameter Variable { get; } = variable;

    public PddlConstraint Inner { get; } = inner;

    public override string Canonical => $"(forall ({Variable.Name} - {Variable.Type}) {Inner.Canonical})";

    public override bool IsPddl3 => Inner.IsPddl3;
}

/// <summary>
/// Le cas degenere : une contrainte du joueur sans equivalent PDDL 3. Elle garde sa phrase pour le
/// journal et pour le bloc <c>(:constraints …)</c>, ou elle sort en commentaire.
/// </summary>
public sealed class PddlUnexpressible(string explanation) : PddlConstraint
{
    public string Explanation { get; } = explanation;

    public override string Canonical => $"; {Explanation}";

    public override bool IsPddl3 => false;
}

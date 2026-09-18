using Hordinary.AI.Planning.Pddl;

namespace Hordinary.AI.Instructions;

/// <summary>Le portillon qui a tranche. Un refus sans son portillon n'apprend rien a personne.</summary>
public enum InstructionGate
{
    /// <summary>Portillon 1 : la grammaire fermee.</summary>
    Grammar = 0,

    /// <summary>Portillon 2 : la compilation dans le domaine.</summary>
    Compilation,

    /// <summary>Portillon 3 : la satisfiabilite, par un planificateur tiers, hors ligne.</summary>
    Satisfiability,

    /// <summary>Portillon 4 : la conformite de la compilation a la forme declarative.</summary>
    Conformance,
}

/// <summary>Une ligne du journal des instructions : ce que le joueur a demande, et ce qu'il en a ete.</summary>
/// <param name="Utterance">La phrase du joueur, ou l'intitule de l'instruction pre-ecrite.</param>
/// <param name="Constraint">La contrainte reconnue, dans la grammaire fermee, quand il y en a une.</param>
/// <param name="Gate">Le portillon qui a refuse, <c>null</c> si l'instruction est passee.</param>
public sealed record InstructionEntry(
    string Utterance,
    string? Constraint,
    bool Accepted,
    InstructionGate? Gate,
    string Reason,
    IReadOnlyList<string> Warnings)
{
    public override string ToString()
        => Accepted
            ? $"[ok] {Utterance} → {Constraint}"
            : $"[refus:{Gate}] {Utterance} — {Reason}";
}

/// <summary>
/// Le journal des instructions. Il garde les refus autant que les acceptations, avec leur raison et
/// le portillon qui les a produits : un systeme qui n'affiche que ses succes ne prouve rien.
/// </summary>
public sealed class InstructionLog
{
    private readonly List<InstructionEntry> _entries = [];

    public IReadOnlyList<InstructionEntry> Entries => _entries;

    public InstructionEntry Record(InstructionEntry entry)
    {
        _entries.Add(entry);
        return entry;
    }

    public override string ToString() => string.Join(Environment.NewLine, _entries);
}

/// <summary>
/// Les contraintes actives d'une mission, et le domaine qu'elles produisent.
///
/// <para>
/// C'est le point d'entree que l'interface appelle : une phrase entre, un verdict sort, et le domaine
/// de planification du groupe est remplace. Retirer toutes les instructions rend le jeu bit a bit
/// identique a ce qu'il etait — <see cref="Source"/> n'est jamais modifie, et
/// <see cref="Compiled"/> repart de lui a chaque changement.
/// </para>
///
/// <para>
/// Les portillons 1, 2 et 4 tournent ici, sans reseau ni binaire. Le portillon 3 — Fast Downward —
/// est hors ligne par construction : aucun planificateur tiers ne tourne pendant une partie.
/// </para>
/// </summary>
public sealed class TacticalInstructions
{
    private readonly List<TacticalConstraint> _active = [];

    public TacticalInstructions(PddlDomain source, InstructionSettings? settings = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Settings = settings ?? new InstructionSettings();
        Compiled = source;
    }

    /// <summary>Le domaine d'origine, celui de la spec G. Jamais modifie.</summary>
    public PddlDomain Source { get; }

    public InstructionSettings Settings { get; }

    /// <summary>Le domaine tel que les contraintes actives l'ont reecrit.</summary>
    public PddlDomain Compiled { get; private set; }

    public IReadOnlyList<TacticalConstraint> Active => _active;

    public InstructionLog Log { get; } = new();

    /// <summary>
    /// Fait passer une instruction les portillons 1 et 2. Acceptee, elle entre dans les contraintes
    /// actives et le domaine compile change ; refusee, rien ne bouge et la raison entre au journal.
    /// </summary>
    public InstructionEntry Add(string text, CompilationContext context = default, string? utterance = null)
    {
        var grammar = ConstraintGrammar.Parse(text, Source, Settings);

        if (!grammar.Accepted)
            return Log.Record(new InstructionEntry(utterance ?? text, null, false, InstructionGate.Grammar, grammar.Reason, []));

        if (_active.Count >= Settings.MaxActiveConstraints)
        {
            return Log.Record(new InstructionEntry(
                utterance ?? text,
                grammar.Constraint!.Text,
                false,
                InstructionGate.Compilation,
                $"Deja {_active.Count} contraintes actives, la configuration en autorise {Settings.MaxActiveConstraints}.",
                []));
        }

        var constraint = grammar.Constraint!.WithUtterance(utterance ?? text);
        var candidate = new List<TacticalConstraint>(_active) { constraint };
        var compiled = DomainCompiler.Compile(Source, candidate, context);

        if (!compiled.Accepted)
        {
            return Log.Record(new InstructionEntry(
                utterance ?? text,
                constraint.Text,
                false,
                InstructionGate.Compilation,
                string.Join(" ", compiled.Errors),
                compiled.Warnings));
        }

        _active.Add(constraint);
        Compiled = compiled.Domain;

        return Log.Record(new InstructionEntry(utterance ?? text, constraint.Text, true, null, constraint.Text, compiled.Warnings));
    }

    /// <summary>Retire une contrainte active et recompile ce qui reste, depuis le domaine d'origine.</summary>
    public bool Remove(string constraintText, CompilationContext context = default)
    {
        int removed = _active.RemoveAll(constraint => constraint.Text.Equals(constraintText, StringComparison.OrdinalIgnoreCase));
        if (removed == 0) return false;

        var compiled = DomainCompiler.Compile(Source, _active, context);
        Compiled = compiled.Accepted ? compiled.Domain : Source;

        return true;
    }

    public void Clear()
    {
        _active.Clear();
        Compiled = Source;
    }

    /// <summary>
    /// Portillon 4 sur un plan reellement produit : la trajectoire respecte-t-elle ce que le joueur a
    /// demande, et non ce que la compilation en a fait ?
    /// </summary>
    public TrajectoryVerdict Verify(TrajectoryTrace trace) => TrajectoryChecker.Check(Compiled, _active, trace);

    /// <summary>
    /// Portillon 3 sur les contraintes actives : reste-t-il un plan, et sinon laquelle accuser ?
    ///
    /// <para>
    /// L'oracle est fourni par l'appelant parce que c'est un planificateur tiers, hors ligne. Cette
    /// methode n'est donc <b>jamais appelee pendant une partie</b> : elle existe pour l'outillage et
    /// les tests. La laisser ici plutot que dans <c>tools/</c> a une raison — le portillon doit voir
    /// exactement les contraintes actives et le domaine qu'elles ont produit, et les recopier
    /// ailleurs aurait cree une seconde verite sur ce qui est en vigueur.
    /// </para>
    /// </summary>
    public SatisfiabilityVerdict CheckSatisfiability(SatisfiabilityOracle oracle)
        => SatisfiabilityGate.Check(_active, oracle);

    /// <summary>
    /// Le domaine qu'un sous-ensemble de contraintes produirait, sans toucher a l'etat courant.
    ///
    /// <para>
    /// C'est ce dont la recherche du coupable minimal a besoin : elle eprouve des sous-ensembles, et
    /// chacun demande son domaine. Recompiler depuis <see cref="Source"/> a chaque fois est ce qui
    /// garantit qu'un essai n'en contamine pas un autre.
    /// </para>
    /// </summary>
    public CompiledDomain Hypothetical(IReadOnlyList<TacticalConstraint> constraints, CompilationContext context = default)
        => DomainCompiler.Compile(Source, constraints, context);
}

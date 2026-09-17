using System.Globalization;
using System.Text;

namespace Hordinary.AI.Planning.Pddl;

/// <summary>
/// Formule PDDL : un litteral, une conjonction, une negation, un effet universel ou un increment de
/// cout. C'est la representation intermediaire demandee par la spec G — l'export ne concatene pas des
/// chaines, il construit un arbre et le rend ensuite (<see cref="PddlWriter"/>).
///
/// <para>
/// L'egalite se fait sur la <see cref="Canonical">forme canonique</see>, et c'est un choix. Un
/// <c>record</c> portant une liste d'arguments comparerait ses listes par reference : deux
/// <c>(at agent group)</c> construits separement seraient differents, et tout le compilateur de
/// contraintes — qui deduplique des preconditions — deviendrait faux sans que rien ne le signale. La
/// forme canonique <b>est</b> l'identite de la formule.
/// </para>
/// </summary>
public abstract class PddlFormula : IEquatable<PddlFormula>
{
    /// <summary>La formule en Lisp, sur une ligne, sans indentation : la cle d'egalite.</summary>
    public abstract string Canonical { get; }

    /// <summary>Les litteraux de la formule, conjonctions traversees.</summary>
    public abstract IEnumerable<PddlFormula> Literals { get; }

    public bool Equals(PddlFormula? other)
        => other is not null && string.Equals(Canonical, other.Canonical, StringComparison.Ordinal);

    public override bool Equals(object? other) => Equals(other as PddlFormula);

    public override int GetHashCode() => Canonical.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Canonical;

    /// <summary>Les conjoints de la formule : ses parties si c'est un <c>(and …)</c>, elle-meme sinon.</summary>
    public IReadOnlyList<PddlFormula> Conjuncts
        => this is PddlConjunction conjunction ? conjunction.Parts : [this];

    /// <summary>
    /// La meme formule, augmentee des parties donnees, sans doublon et dans l'ordre. C'est l'operation
    /// centrale du compilateur de contraintes : ajouter une precondition a un schema.
    /// </summary>
    public PddlFormula Conjoin(params PddlFormula[] parts)
    {
        var merged = new List<PddlFormula>(Conjuncts);

        foreach (var part in parts)
        {
            if (!merged.Contains(part)) merged.Add(part);
        }

        return new PddlConjunction(merged);
    }

    /// <summary>La meme formule, privee des parties qui verifient le predicat donne.</summary>
    public PddlFormula Without(Func<PddlFormula, bool> unwanted)
        => new PddlConjunction([.. Conjuncts.Where(part => !unwanted(part))]);
}

/// <summary>
/// Un litteral positif : <c>(at agent pharmacy)</c>, <c>(objective_found)</c>. Les arguments sont des
/// constantes, des objets ou des variables <c>?x</c> — la meme grammaire que <see cref="Fact"/>, dont
/// il se construit directement.
/// </summary>
public sealed class PddlAtom : PddlFormula
{
    public PddlAtom(string predicate, params string[] arguments)
    {
        Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        Arguments = arguments ?? [];
    }

    public string Predicate { get; }

    public IReadOnlyList<string> Arguments { get; }

    public int Arity => Arguments.Count;

    /// <summary>Le litteral dans la grammaire du jeu : <c>at(agent, pharmacy)</c>.</summary>
    public string AsFact => Arguments.Count == 0 ? Predicate : $"{Predicate}({string.Join(", ", Arguments)})";

    public override string Canonical
        => Arguments.Count == 0 ? $"({Predicate})" : $"({Predicate} {string.Join(' ', Arguments)})";

    public override IEnumerable<PddlFormula> Literals => [this];

    /// <summary>Construit le litteral depuis un fait du domaine JSON (<c>"at(agent, ?location)"</c>).</summary>
    public static PddlAtom Parse(string fact)
    {
        string normalized = Fact.Normalize(fact);
        return new PddlAtom(Fact.PredicateOf(normalized), Fact.ArgumentsOf(normalized));
    }

    /// <summary>Le meme litteral, ses variables liees — utilise par le verificateur de trajectoire.</summary>
    public PddlAtom Substitute(IReadOnlyDictionary<string, string> bindings)
        => new(Predicate, [.. Arguments.Select(argument =>
            argument.StartsWith('?') && bindings.TryGetValue(argument[1..], out string? bound) ? bound : argument)]);

    public bool MentionsVariable(string variable)
        => Arguments.Any(argument => string.Equals(argument, $"?{variable}", StringComparison.OrdinalIgnoreCase));

    public IEnumerable<string> Variables
        => Arguments.Where(argument => argument.Length > 1 && argument[0] == '?').Select(argument => argument[1..]);
}

/// <summary>Une negation : <c>(not (door_locked ?door))</c>.</summary>
public sealed class PddlNegation(PddlFormula inner) : PddlFormula
{
    public PddlFormula Inner { get; } = inner ?? throw new ArgumentNullException(nameof(inner));

    public override string Canonical => $"(not {Inner.Canonical})";

    public override IEnumerable<PddlFormula> Literals => [this];
}

/// <summary>
/// Une conjonction : <c>(and (known ?loc) (not (at agent ?loc)))</c>. Toujours rendue avec son
/// <c>and</c>, meme a un seul membre : une precondition de schema est une conjonction par nature, et
/// une forme unique se compare mieux que deux.
/// </summary>
public sealed class PddlConjunction : PddlFormula
{
    public PddlConjunction(IEnumerable<PddlFormula> parts)
    {
        // Les conjonctions imbriquees sont aplaties : (and A (and B C)) et (and A B C) disent la meme
        // chose, et une seule des deux formes doit exister pour que l'egalite canonique soit fidele.
        var flattened = new List<PddlFormula>();

        foreach (var part in parts)
        {
            if (part is PddlConjunction nested) flattened.AddRange(nested.Parts);
            else flattened.Add(part);
        }

        Parts = flattened;
    }

    public IReadOnlyList<PddlFormula> Parts { get; }

    public override string Canonical
    {
        get
        {
            var text = new StringBuilder("(and");
            foreach (var part in Parts) text.Append(' ').Append(part.Canonical);
            return text.Append(')').ToString();
        }
    }

    public override IEnumerable<PddlFormula> Literals => Parts.SelectMany(part => part.Literals);
}

/// <summary>
/// Effet universellement quantifie : <c>(forall (?l - location) (not (at agent ?l)))</c>.
///
/// C'est la traduction du joker <c>*</c> du domaine JSON (<c>"!at(agent, *)"</c>), seul endroit ou la
/// transposition PDDL demande de reflechir plutot que de transcrire — voir la spec G.
/// </summary>
public sealed class PddlUniversal(PddlParameter variable, PddlFormula body) : PddlFormula
{
    public PddlParameter Variable { get; } = variable;

    public PddlFormula Body { get; } = body ?? throw new ArgumentNullException(nameof(body));

    public override string Canonical => $"(forall ({Variable.Name} - {Variable.Type}) {Body.Canonical})";

    public override IEnumerable<PddlFormula> Literals => [this];
}

/// <summary>
/// Increment de cout : <c>(increase (total-cost) 3)</c>. Porte le cout de l'action, que la contrainte
/// <c>penalize</c> multiplie.
/// </summary>
public sealed class PddlIncrease(string function, float amount) : PddlFormula
{
    public const string TotalCost = "total-cost";

    public string Function { get; } = function;

    public float Amount { get; } = amount;

    public override string Canonical
        => $"(increase ({Function}) {Amount.ToString("0.###", CultureInfo.InvariantCulture)})";

    public override IEnumerable<PddlFormula> Literals => [this];
}

/// <summary>
/// Affectation d'une fonction dans l'etat initial d'un probleme : <c>(= (total-cost) 0)</c>,
/// <c>(= (distance pharmacy extraction) 12)</c>.
///
/// Elle n'a de sens que dans un <c>(:init …)</c> — un domaine ne fixe aucune valeur — d'ou son absence
/// du vocabulaire des schemas d'action, ou le cout s'exprime en <see cref="PddlIncrease"/>.
/// </summary>
public sealed class PddlAssign(string function, IReadOnlyList<string> arguments, float value) : PddlFormula
{
    public PddlAssign(string function, float value) : this(function, [], value) { }

    public string Function { get; } = function;

    public IReadOnlyList<string> Arguments { get; } = arguments ?? [];

    public float Value { get; } = value;

    private string Term
        => Arguments.Count == 0 ? $"({Function})" : $"({Function} {string.Join(' ', Arguments)})";

    public override string Canonical
        => $"(= {Term} {Value.ToString("0.###", CultureInfo.InvariantCulture)})";

    public override IEnumerable<PddlFormula> Literals => [this];
}

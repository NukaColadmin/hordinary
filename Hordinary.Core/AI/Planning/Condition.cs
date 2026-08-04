namespace Hordinary.AI.Planning;

/// <summary>
/// Precondition d'une action, ou condition d'un objectif symbolique (specification, livrable 5 :
/// « Chaque action doit contenir preconditions: list[Condition] »).
///
/// Ecrite en configuration sous la forme <c>"at(agent, pharmacy)"</c> ou, niee,
/// <c>"!door_locked(?door)"</c>. Une condition niee est satisfaite par l'<b>absence</b> du fait :
/// l'etat symbolique est ferme sur le monde connu de l'agent, ce qu'il ignore est repute faux.
/// </summary>
public readonly record struct Condition(string Pattern, bool Expected)
{
    public static Condition Parse(string text)
    {
        string trimmed = (text ?? string.Empty).Trim();
        bool expected = true;

        if (trimmed.StartsWith('!'))
        {
            expected = false;
            trimmed = trimmed[1..].Trim();
        }

        if (trimmed.Length == 0)
            throw new FormatException("Precondition vide.");

        string fact = Fact.Normalize(trimmed);

        if (Fact.HasWildcard(fact))
            throw new FormatException($"Le joker « * » n'a de sens que dans un effet de suppression, pas dans « {text} ».");

        return new Condition(fact, expected);
    }

    public IEnumerable<string> Variables => Fact.VariablesOf(Pattern);

    public Condition Substitute(IReadOnlyDictionary<string, string> bindings)
        => this with { Pattern = Fact.Substitute(Pattern, bindings) };

    public override string ToString() => Expected ? Pattern : $"!{Pattern}";
}

/// <summary>
/// Effet d'une action : un fait pose (<c>"door_open(?door)"</c>) ou retire (<c>"!door_locked(?door)"</c>).
///
/// Un effet de suppression accepte le joker <c>*</c> — <c>"!at(agent, *)"</c> retire toute position
/// memorisee de l'agent avant que le deplacement n'en pose une nouvelle. Les suppressions sont
/// toujours appliquees avant les ajouts, sans quoi un tel couple s'annulerait.
/// </summary>
public readonly record struct Effect(string Pattern, bool Value)
{
    public static Effect Parse(string text)
    {
        string trimmed = (text ?? string.Empty).Trim();
        bool value = true;

        if (trimmed.StartsWith('!'))
        {
            value = false;
            trimmed = trimmed[1..].Trim();
        }

        if (trimmed.Length == 0)
            throw new FormatException("Effet vide.");

        string fact = Fact.Normalize(trimmed);

        if (value && Fact.HasWildcard(fact))
            throw new FormatException($"« {text} » : un joker ne peut pas etre ajoute a l'etat, seulement retire.");

        return new Effect(fact, value);
    }

    public IEnumerable<string> Variables => Fact.VariablesOf(Pattern);

    public Effect Substitute(IReadOnlyDictionary<string, string> bindings)
        => this with { Pattern = Fact.Substitute(Pattern, bindings) };

    public override string ToString() => Value ? Pattern : $"!{Pattern}";
}

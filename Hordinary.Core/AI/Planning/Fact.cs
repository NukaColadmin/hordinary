namespace Hordinary.AI.Planning;

/// <summary>
/// Grammaire des faits symboliques (specification, livrable 5 : « Representation d'un etat »).
///
/// Un fait est une chaine : soit un predicat nu (<c>medicine_found</c>), soit un predicat applique
/// a des arguments (<c>at(agent, pharmacy)</c>). Le vocabulaire n'est pas fige dans le code :
/// c'est <c>config/planner_domain.json</c> qui decide quels predicats existent, exactement comme
/// <c>config/utility_goals.json</c> decide quels objectifs existent.
///
/// Trois formes d'arguments :
/// une constante (<c>pharmacy</c>), une variable (<c>?door</c>) liee au moment de l'instanciation,
/// et le joker <c>*</c>, reserve aux effets de suppression (<c>!at(agent, *)</c> : « oublier ou que
/// l'agent se trouve »). Sans lui, un deplacement laisserait derriere lui la position precedente.
/// </summary>
public static class Fact
{
    public const char VariablePrefix = '?';
    public const string Wildcard = "*";

    /// <summary>Met un fait sous forme canonique : espaces normalises, arguments separes par « , ».</summary>
    public static string Normalize(string text)
    {
        string trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0) return string.Empty;

        int open = trimmed.IndexOf('(');
        if (open < 0) return trimmed;

        if (!trimmed.EndsWith(')'))
            throw new FormatException($"Fait mal forme « {text} » : parenthese fermante manquante.");

        string name = trimmed[..open].Trim();
        if (name.Length == 0)
            throw new FormatException($"Fait mal forme « {text} » : predicat sans nom.");

        var arguments = trimmed[(open + 1)..^1]
            .Split(',', StringSplitOptions.TrimEntries)
            .Where(argument => argument.Length > 0)
            .ToArray();

        return arguments.Length == 0 ? name : $"{name}({string.Join(", ", arguments)})";
    }

    /// <summary>Nom du predicat, sans ses arguments.</summary>
    public static string PredicateOf(string fact)
    {
        int open = fact.IndexOf('(');
        return open < 0 ? fact : fact[..open];
    }

    /// <summary>Arguments du fait, vides pour un predicat nu.</summary>
    public static string[] ArgumentsOf(string fact)
    {
        int open = fact.IndexOf('(');
        if (open < 0 || !fact.EndsWith(')')) return [];

        return fact[(open + 1)..^1].Split(',', StringSplitOptions.TrimEntries);
    }

    public static bool IsVariable(string token) => token.Length > 1 && token[0] == VariablePrefix;

    /// <summary>Noms des variables presentes dans un fait, sans le prefixe <c>?</c>.</summary>
    public static IEnumerable<string> VariablesOf(string fact)
        => ArgumentsOf(fact).Where(IsVariable).Select(argument => argument[1..]);

    /// <summary>
    /// Remplace les variables par leur liaison. Une variable non liee est laissee telle quelle :
    /// c'est <see cref="PlanningDomain.Validate"/> qui refuse ce cas au chargement, pas la recherche.
    /// </summary>
    public static string Substitute(string fact, IReadOnlyDictionary<string, string> bindings)
    {
        if (bindings.Count == 0) return fact;

        var arguments = ArgumentsOf(fact);
        if (arguments.Length == 0) return fact;

        bool changed = false;
        for (int i = 0; i < arguments.Length; i++)
        {
            if (!IsVariable(arguments[i])) continue;
            if (!bindings.TryGetValue(arguments[i][1..], out string? bound)) continue;

            arguments[i] = bound;
            changed = true;
        }

        return changed ? $"{PredicateOf(fact)}({string.Join(", ", arguments)})" : fact;
    }

    /// <summary>
    /// Vrai si un fait concret correspond a un motif pouvant contenir des jokers. Meme predicat,
    /// meme nombre d'arguments, et chaque argument egal ou joker.
    /// </summary>
    public static bool Matches(string pattern, string fact)
    {
        if (!PredicateOf(pattern).Equals(PredicateOf(fact), StringComparison.OrdinalIgnoreCase)) return false;

        var expected = ArgumentsOf(pattern);
        var actual = ArgumentsOf(fact);
        if (expected.Length != actual.Length) return false;

        for (int i = 0; i < expected.Length; i++)
        {
            if (expected[i] == Wildcard) continue;
            if (!expected[i].Equals(actual[i], StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }

    public static bool HasWildcard(string fact) => ArgumentsOf(fact).Contains(Wildcard);
}

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hordinary.AI.Instructions;

/// <summary>Ce que le jeu envoie au service : une phrase, une graine, et de quoi la traduire.</summary>
/// <param name="Seed">
/// La graine de la mission. Elle ne sert pas au modele mais au cache : deux parties differentes ne
/// partagent pas une traduction, meme phrase pour meme phrase, parce que le vocabulaire nommable
/// depend de la carte tiree.
/// </param>
public sealed record InstructionRequest(
    string Utterance,
    int Seed,
    DomainDigest DomainDigest,
    int MaxChars)
{
    public string ToJson() => JsonSerializer.Serialize(this, InstructionJson.Options);
}

/// <summary>La contrainte telle que le service la rend : jamais du PDDL, jamais du texte libre.</summary>
/// <param name="Form">L'une des cinq formes de la grammaire fermee.</param>
/// <param name="Predicate">Le fait exige, pour les trois formes qui en portent un.</param>
/// <param name="ActionClass">Le schema (<c>ForceDoor</c>) ou la classe (<c>enter:building</c>).</param>
/// <param name="Factor">
/// Le multiplicateur de <c>penalize</c>. Il ne figurait pas au contrat initial de la specification, et
/// son absence rendait la cinquieme forme intraduisible : « evitez de forcer les portes » n'a pas de
/// sens sans un <c>k</c>. Le portillon 1 le borne par <c>instructions.min_penalty</c> et
/// <c>max_penalty</c>, donc le rendre libre ici ne libere rien.
/// </param>
public sealed record ServiceConstraint(
    string Form,
    string? Predicate = null,
    string? ActionClass = null,
    float? Factor = null)
{
    /// <summary>
    /// La contrainte reecrite dans la grammaire fermee, telle que <see cref="ConstraintGrammar"/> la
    /// relit. Rien n'est verifie ici : le texte produit peut parfaitement citer un predicat
    /// inexistant, et c'est le portillon 1 qui le refusera. La separation est voulue — ce code met en
    /// forme, il ne valide pas, et il n'y a donc qu'un seul endroit ou une validation peut manquer.
    /// </summary>
    public string? ToGrammar() => Form?.Trim().ToLowerInvariant() switch
    {
        "require_before" when Predicate is not null && ActionClass is not null => $"require_before({Predicate}, {ActionClass})",
        "require_after" when Predicate is not null && ActionClass is not null => $"require_after({Predicate}, {ActionClass})",
        "forbid" when ActionClass is not null => $"forbid({ActionClass})",
        "require_always" when Predicate is not null => $"require_always({Predicate})",
        "penalize" when ActionClass is not null => $"penalize({ActionClass}, {(Factor ?? 1f).ToString("0.###", CultureInfo.InvariantCulture)})",
        _ => null,
    };

    public override string ToString() => ToGrammar() ?? $"{Form} (incomplet)";
}

/// <summary>Le compte des jetons d'un appel, tel que le fournisseur le rend.</summary>
public sealed record ServiceTokens(int Prompt = 0, int Completion = 0);

/// <summary>
/// La reponse du service (livrable 15, noeud <c>project</c>).
///
/// <para>
/// Deux statuts, et deux seulement : <c>constraint</c> et <c>rejected</c>. Le service ne renvoie
/// jamais de patch de domaine, jamais de PDDL, jamais de JSON d'action, jamais de texte libre a
/// afficher tel quel — <see cref="Message"/> et <see cref="Rationale"/> sont montres au joueur, mais
/// rien de ce qu'ils contiennent n'atteint un planificateur.
/// </para>
///
/// <para>
/// <c>status: "constraint"</c> ne veut pas dire « appliquee ». Les quatre portillons tournent
/// ensuite, dans <c>Hordinary.Core</c>, et peuvent encore refuser.
/// </para>
/// </summary>
public sealed record InstructionReply
{
    public const string StatusConstraint = "constraint";
    public const string StatusRejected = "rejected";

    public string Status { get; init; } = StatusRejected;

    public ServiceConstraint? Constraint { get; init; }

    /// <summary>Pourquoi le service a traduit ainsi. Affiche, jamais interprete.</summary>
    public string? Rationale { get; init; }

    /// <summary>La famille du refus : <c>preference_not_constraint</c>, <c>out_of_scope</c>…</summary>
    public string? Reason { get; init; }

    /// <summary>Le refus en francais, pour le joueur.</summary>
    public string? Message { get; init; }

    public string? Model { get; init; }

    [JsonPropertyName("latency_ms")]
    public int LatencyMs { get; init; }

    public ServiceTokens? Tokens { get; init; }

    [JsonPropertyName("cost_usd")]
    public double CostUsd { get; init; }

    public bool Accepted => Status.Equals(StatusConstraint, StringComparison.OrdinalIgnoreCase) && Constraint is not null;

    public static InstructionReply? FromJson(string json)
        => JsonSerializer.Deserialize<InstructionReply>(json, InstructionJson.Options);

    public override string ToString()
        => Accepted ? $"{Constraint}" : $"refus ({Reason}) : {Message}";
}

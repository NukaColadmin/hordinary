using System.Numerics;
using Hordinary.World;

namespace Hordinary.Agents;

/// <summary>Origine d'une connaissance (specification section 9.3).</summary>
public enum KnowledgeSource
{
    DirectObservation = 0,
    AllyCommunication,
    Inference,
    Sound,
}

/// <summary>
/// Champs communs a toute connaissance (specification section 9.3) : une valeur crue, la confiance
/// qu'on lui accorde, la date de la derniere observation et sa provenance.
/// </summary>
public interface IBelief
{
    /// <summary>Confiance au moment de l'observation, entre 0 et 1.</summary>
    float Confidence { get; }

    /// <summary>Horodatage de jeu, en secondes.</summary>
    double LastObservedAt { get; }

    KnowledgeSource Source { get; }
}

/// <summary>
/// La fusion des croyances du livrable 7, telle que la specification l'ecrit :
///
/// <code>
/// def merge_belief(current, incoming):
///     if incoming.source == DIRECT_OBSERVATION: return incoming
///     if incoming.last_observed_at &gt; current.last_observed_at: return incoming
///     return current
/// </code>
///
/// Les trois lignes disent trois choses differentes. La premiere donne le dernier mot a ce que
/// l'agent a vu de ses yeux : un temoignage, meme frais, ne dement pas une observation directe.
/// La deuxieme arbitre entre deux temoignages par leur date. La troisieme, implicite, est celle qui
/// compte le plus : a egalite, on ne change pas d'avis — sans quoi deux allies en conversation se
/// renverraient indefiniment la meme information, chacun la degradant d'un cran a chaque passage.
/// </summary>
public static class BeliefMerge
{
    /// <summary>
    /// Celle des deux croyances qui doit etre retenue. <paramref name="current"/> a <c>null</c>
    /// signifie « l'agent n'en savait rien » : tout renseignement vaut mieux que rien.
    /// </summary>
    public static T Merge<T>(T? current, T incoming) where T : class, IBelief
    {
        ArgumentNullException.ThrowIfNull(incoming);

        if (current is null) return incoming;
        if (incoming.Source == KnowledgeSource.DirectObservation) return incoming;
        if (incoming.LastObservedAt > current.LastObservedAt) return incoming;

        return current;
    }

    /// <summary>Vrai si la croyance transmise l'emporte sur celle deja memorisee.</summary>
    public static bool Accepts<T>(T? current, T incoming) where T : class, IBelief
        => ReferenceEquals(Merge(current, incoming), incoming);
}

public static class BeliefExtensions
{
    /// <summary>
    /// Confiance restante apres decroissance lineaire (specification section 9.5) :
    /// <c>max(0, confiance - taux * temps ecoule)</c>. Un taux nul decrit une information
    /// statique, qui ne perd jamais en fiabilite.
    /// </summary>
    public static float ConfidenceAt(this IBelief belief, double now, float decayRatePerSecond)
    {
        if (decayRatePerSecond <= 0f) return belief.Confidence;

        double elapsed = Math.Max(0d, now - belief.LastObservedAt);
        return Math.Max(0f, belief.Confidence - decayRatePerSecond * (float)elapsed);
    }
}

/// <summary>Ce que l'agent croit savoir d'une cellule. Son absence signifie « jamais observee ».</summary>
public sealed record CellBelief(
    GridPosition Position,
    CellType Value,
    bool Indoor,
    float Confidence,
    double LastObservedAt,
    KnowledgeSource Source) : IBelief
{
    /// <summary>
    /// Une porte peut etre ouverte, fermee ou verrouillee par quelqu'un d'autre : sa description
    /// vieillit. Les murs, fenetres et sols sont statiques (specification sections 9.4 et 9.5).
    /// </summary>
    public bool IsDynamic => Value.IsDoor();
}

public enum EntityKind
{
    Survivor = 0,
    Zombie,
}

/// <summary>
/// Derniere position connue d'une entite, et le cap qu'on lui prete. Toujours dynamique : une
/// entite se deplace, y compris — surtout — quand on ne la regarde plus.
/// </summary>
/// <param name="HealthRatio">
/// Sante de l'allie, entre 0 et 1, ou <c>null</c> tant qu'elle est inconnue. On ne lit pas la sante
/// de quelqu'un a distance : elle n'arrive que par la parole (livrable 7), quand deux survivants se
/// parlent a portee. C'est ce qui empeche Claire de courir soigner un blesse dont personne ne lui a
/// dit qu'il l'etait.
/// </param>
/// <param name="Panic">Panique rapportee par l'interesse, entre 0 et 1, ou <c>null</c> si inconnue.</param>
/// <param name="SpareWeapon">
/// L'arme que l'allie dit porter en plus de celle qu'il a en main : <c>null</c> tant qu'il n'en a rien
/// dit, une chaine vide s'il a dit n'en avoir aucune. La difference compte — « je ne sais pas » laisse
/// l'ancien renseignement en place, « je n'en ai plus » l'efface. Comme la sante, elle n'arrive que
/// par la parole : on voit ce qu'un camarade tient, pas ce qu'il a dans le sac.
/// </param>
public sealed record EntityBelief(
    string Id,
    EntityKind Kind,
    GridPosition Value,
    float Confidence,
    double LastObservedAt,
    KnowledgeSource Source,
    Vector2 Heading = default,
    float? HealthRatio = null,
    float? Panic = null,
    string? SpareWeapon = null) : IBelief
{
    /// <summary>Vrai si l'allie a dit porter une arme qu'il peut preter.</summary>
    public bool CanLend => !string.IsNullOrEmpty(SpareWeapon);

    /// <summary>Vrai si l'agent a vu l'entite se deplacer et sait dans quel sens, en cellules par seconde.</summary>
    public bool IsMoving => Heading.LengthSquared() > 0f;

    /// <summary>
    /// Ou l'entite devrait se trouver maintenant si elle a garde son cap. Sans cap connu, la
    /// supposition la plus prudente reste la derniere position vue.
    ///
    /// Passe <paramref name="horizonSeconds"/>, l'extrapolation s'arrete : deviner la trajectoire
    /// d'un zombie dix secondes apres l'avoir perdu de vue n'est plus une croyance, c'est une
    /// invention. Au-dela, seule la decroissance de confiance (section 9.5) continue de travailler.
    /// </summary>
    public GridPosition PredictedPositionAt(double now, float horizonSeconds)
    {
        if (!IsMoving) return Value;

        float elapsed = Math.Clamp((float)(now - LastObservedAt), 0f, Math.Max(0f, horizonSeconds));
        if (elapsed <= 0f) return Value;

        var drift = Heading * elapsed;
        return new GridPosition(
            Value.X + (int)MathF.Round(drift.X),
            Value.Y + (int)MathF.Round(drift.Y));
    }
}

/// <summary>
/// Objet repere sur la carte. Statique tant que personne ne le ramasse — et l'agent n'a aucun moyen
/// de savoir que quelqu'un l'a fait tant qu'il n'est pas revenu voir. Sa confiance ne decroit donc
/// pas avec le temps : elle tombe d'un coup, le jour ou la case se revele vide.
/// </summary>
/// <param name="Id">Identifiant de l'exemplaire pose sur la carte.</param>
/// <param name="ItemId">
/// Identifiant du catalogue (<c>config/items.json</c>, <c>config/weapons.json</c>). L'agent ne
/// connait rien d'autre de l'objet : ni son poids, ni ses effets, seulement ce qu'il en a vu.
/// </param>
public sealed record ItemBelief(
    string Id,
    string ItemId,
    GridPosition Value,
    float Confidence,
    double LastObservedAt,
    KnowledgeSource Source) : IBelief
{
    /// <summary>Vrai si l'agent est retourne sur place et a constate que l'objet n'y etait plus.</summary>
    public bool IsGone => Confidence <= 0f;
}

/// <summary>
/// Meuble poussable apercu (livrable 11). Comme un objet, il ne bouge pas tout seul : sa confiance
/// ne s'erode pas avec le temps, elle tombe le jour ou l'on constate qu'il n'est plus la.
///
/// Il en existe une croyance separee du terrain parce que le terrain ne dit pas tout : la carte
/// mentale sait qu'une case est un obstacle, elle ne sait pas que cet obstacle est une benne de
/// cinq cents kilos qu'il faudra etre deux a bouger.
/// </summary>
/// <param name="Mass">Ce que le survivant estime en le voyant. C'est ce qui decide s'il tente seul.</param>
public sealed record HeavyBelief(
    string Id,
    string Kind,
    GridPosition Value,
    float Mass,
    float Confidence,
    double LastObservedAt,
    KnowledgeSource Source) : IBelief
{
    /// <summary>Vrai si l'agent est repasse et a constate que le meuble n'y etait plus.</summary>
    public bool IsGone => Confidence <= 0f;
}

/// <summary>
/// Souvenir d'une fouille : ce lieu a ete fouille, et on y a trouve — ou non — ce qu'on cherchait.
///
/// C'est ce qui distingue « je n'ai pas encore cherche la » de « j'ai cherche la, il n'y avait
/// rien ». Sans cette memoire, un survivant dont la fouille echoue replanifie la meme fouille
/// indefiniment : le plan est reconstruit depuis une carte mentale qui a oublie la deception.
/// </summary>
public sealed record SearchBelief(
    string Place,
    bool Found,
    float Confidence,
    double LastObservedAt,
    KnowledgeSource Source) : IBelief
{
    public override string ToString()
        => $"{Place} fouille a t={LastObservedAt:0.0} : {(Found ? "trouve" : "rien")}";
}

/// <summary>
/// Souvenir d'un bruit entendu (specification section 18.3, etape 1 : « il memorise la position
/// approximative »). <see cref="Value"/> n'est pas la position de la source mais celle que
/// l'auditeur lui prete : plus le bruit est arrive attenue, plus elle peut etre fausse.
///
/// L'agent retient <b>quoi</b> il a entendu (<see cref="Action"/>) et <b>a quel point</b>
/// (<see cref="PerceivedIntensity"/>), jamais <b>qui</b> : une vitre brisee ne dit pas si c'est un
/// allie ou un zombie qui l'a brisee. C'est pour cela que <see cref="NoiseEvent.SourceId"/> ne
/// traverse pas la perception.
/// </summary>
public sealed record NoiseBelief(
    string Id,
    GridPosition Value,
    float PerceivedIntensity,
    float Confidence,
    double LastObservedAt,
    KnowledgeSource Source,
    NoiseAction Action = NoiseAction.Walk) : IBelief
{
    /// <summary>
    /// Vrai si le bruit ne peut pas etre un simple passage : quelque chose a force une entree ou
    /// jete un objet. Un pas s'oublie, un fracas se va voir.
    /// </summary>
    public bool IsRemarkable => Action is not (NoiseAction.Walk or NoiseAction.Run);

    public override string ToString()
        => $"{Action} ({PerceivedIntensity:0.#}) vers {Value} a t={LastObservedAt:0.0}";
}

/// <summary>
/// Ce qu'un survivant a annonce vouloir faire, et ou : « je vais voir au bout de la ruelle ».
///
/// C'est la seule croyance qui ne porte pas sur le monde mais sur quelqu'un. Elle n'existe que parce
/// que la specification demande que deux survivants ne choisissent pas automatiquement la meme
/// frontiere d'exploration (livrable 7) : sans intention partagee, la penalite de duplication n'aurait
/// rien a lire, et la repartition se ferait par hasard.
///
/// Elle ne circule que par la parole. Un survivant hors de portee de communication ne sait pas ou
/// vont les autres — et deux equipes separees se marchent donc bel et bien dessus, ce qui est le
/// comportement attendu.
/// </summary>
public sealed record IntentBelief(
    string Id,
    string Goal,
    GridPosition Value,
    float Confidence,
    double LastObservedAt,
    KnowledgeSource Source) : IBelief
{
    public override string ToString() => $"{Id} vise {Goal} en {Value} (t={LastObservedAt:0.0})";
}

/// <summary>Lieu nomme connu de l'agent (pharmacie, extraction...). Statique.</summary>
public sealed record LocationBelief(
    string Name,
    ZoneKind Kind,
    GridPosition Value,
    float Confidence,
    double LastObservedAt,
    KnowledgeSource Source) : IBelief;

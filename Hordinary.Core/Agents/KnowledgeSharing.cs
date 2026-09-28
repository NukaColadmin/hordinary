using Hordinary.Config;
using Hordinary.World;

namespace Hordinary.Agents;

/// <summary>Un echange : qui a parle a qui, ce que l'auditeur y a gagne, et a quelle distance.</summary>
/// <param name="ByRadio">
/// Vrai quand les deux etaient hors de portee de voix et que seules leurs radios les reliaient. C'est
/// la ligne que le mode demonstration filme : sans ce drapeau, un echange a douze cases et un echange a
/// deux se liraient pareil au journal.
/// </param>
public readonly record struct KnowledgeExchange(string From, string To, int Learned, int Distance, double At, bool ByRadio = false)
{
    public override string ToString()
        => $"{From} informe {To} a {Distance} cases{(ByRadio ? " par radio" : string.Empty)} : {Learned} croyance(s)";
}

/// <summary>
/// Le partage d'informations entre survivants (specification, livrable 7).
///
/// Deux survivants a moins de <see cref="KnowledgeSettings.CommunicationRange"/> cellules se disent
/// ce qu'ils savent. La classe ne fait que cadencer et appareiller : la fusion elle-meme est
/// <see cref="AgentKnowledge.LearnFrom"/>, dont la signature ne peut atteindre que la memoire d'un
/// autre agent.
///
/// C'est le point du livrable, et il est structurel plutot que documentaire : rien ici ne prend de
/// <see cref="WorldState"/>. Un survivant ne peut donc pas apprendre par la conversation ce que son
/// interlocuteur ignorait — le test « le partage de connaissances n'accede pas au WorldState » n'a
/// aucune facon d'echouer sans que le code cesse de compiler.
///
/// La seule chose que cette classe lise dans le monde physique est la distance entre deux corps, et
/// c'est bien ce qu'elle doit lire : la portee de la voix n'est pas une croyance. Avec
/// <see cref="KnowledgeSettings.RadioEnabled"/>, meme cela disparait.
/// </summary>
public sealed class KnowledgeSharing(KnowledgeSettings? settings = null)
{
    private double _lastPassAt = double.NegativeInfinity;

    public KnowledgeSettings Settings { get; } = settings ?? new KnowledgeSettings();

    /// <summary>Nombre d'echanges depuis le debut de la partie. Alimente le HUD.</summary>
    public int ExchangeCount { get; private set; }

    /// <summary>Emis a chaque echange fructueux : c'est la ligne « Alice informe Bob » du journal.</summary>
    public event Action<KnowledgeExchange>? Exchanged;

    /// <summary>
    /// Vrai si les deux survivants peuvent se parler : tous deux debout, et a portee de voix — ou
    /// n'importe ou, si le groupe dispose d'une radio.
    /// </summary>
    public bool CanTalk(Survivor speaker, Survivor listener)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        ArgumentNullException.ThrowIfNull(listener);

        if (ReferenceEquals(speaker, listener)) return false;
        if (!speaker.Alive || !listener.Alive) return false;

        return Settings.RadioEnabled
            || speaker.Position.ChebyshevDistanceTo(listener.Position) <= RangeBetween(speaker, listener);
    }

    /// <summary>La caracteristique qu'une radio de <c>config/items.json</c> declare modifier.</summary>
    public const string RangeStat = "knowledge.communication_range";

    /// <summary>
    /// A quelle distance ces deux-la s'entendent : la portee de voix, multipliee par ce que leur
    /// equipement fait de <see cref="RangeStat"/> — le <b>plus faible</b> des deux.
    ///
    /// <para>
    /// Le plus faible, parce qu'une radio n'est utile qu'aux deux bouts. Un seul poste dans le groupe
    /// ne relie personne ; c'est au joueur, au paquetage, d'en confier deux. L'effet etait declare dans
    /// le catalogue depuis le livrable 11 et personne ne le lisait : une radio emportee ne changeait
    /// rien a qui entendait quoi.
    /// </para>
    /// </summary>
    public int RangeBetween(Survivor speaker, Survivor listener)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        ArgumentNullException.ThrowIfNull(listener);

        float factor = Math.Min(speaker.Inventory.StatMultiplier(RangeStat), listener.Inventory.StatMultiplier(RangeStat));
        return (int)MathF.Floor(Settings.CommunicationRange * Math.Max(1f, factor));
    }

    /// <summary>
    /// Cadence le partage a <see cref="KnowledgeSettings.SharingInterval"/> (une fois par seconde,
    /// document A). Renvoie le nombre de croyances transmises lors de cette passe, ou zero si l'heure
    /// n'y etait pas.
    /// </summary>
    public int Advance(IReadOnlyList<Survivor> band, double now)
    {
        if (now - _lastPassAt < Settings.SharingInterval) return 0;

        _lastPassAt = now;
        return Share(band, now);
    }

    /// <summary>
    /// Une passe de conversation. Chaque paire a portee echange dans les deux sens : ecouter n'est
    /// pas parler, et un survivant qui rapporte trois rues n'apprend pas pour autant celles de son
    /// interlocuteur.
    ///
    /// Les paires sont parcourues dans l'ordre du groupe, deterministe : deux parties identiques
    /// produisent le meme journal.
    /// </summary>
    public int Share(IReadOnlyList<Survivor> band, double now)
    {
        ArgumentNullException.ThrowIfNull(band);

        int learned = 0;

        for (int i = 0; i < band.Count; i++)
        {
            for (int j = i + 1; j < band.Count; j++)
            {
                if (!CanTalk(band[i], band[j])) continue;

                learned += Talk(band[i], band[j], now);
                learned += Talk(band[j], band[i], now);
            }
        }

        return learned;
    }

    /// <summary>
    /// <paramref name="speaker"/> raconte ce qu'il sait a <paramref name="listener"/>.
    ///
    /// Se parler, c'est aussi se voir : l'auditeur note ou se tient son interlocuteur, et — c'est
    /// nouveau au livrable 7 — dans quel etat il dit etre. La sante et la panique ne se lisent pas a
    /// distance ; elles n'entrent dans une carte mentale que par cette ligne. Depuis le livrable 15
    /// aucun objectif ne les consomme plus directement, mais elles restent ce qu'un survivant peut dire
    /// de lui-meme, et le materiau des strategies que le joueur compose.
    /// </summary>
    public int Talk(Survivor speaker, Survivor listener, double now)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        ArgumentNullException.ThrowIfNull(listener);

        if (!CanTalk(speaker, listener)) return 0;

        listener.Knowledge.RememberEntity(
            speaker.Id,
            EntityKind.Survivor,
            speaker.Position,
            now,
            health: speaker.HealthRatio,
            panic: speaker.Panic,
            spareWeapon: speaker.Inventory.SpareWeapon ?? string.Empty);

        int learned = listener.Knowledge.LearnFrom(speaker.Knowledge);
        int distance = speaker.Position.ChebyshevDistanceTo(listener.Position);

        ExchangeCount++;
        Exchanged?.Invoke(new KnowledgeExchange(
            speaker.Name,
            listener.Name,
            learned,
            distance,
            now,
            ByRadio: !Settings.RadioEnabled && distance > Settings.CommunicationRange));

        return learned;
    }

    /// <summary>
    /// <paramref name="speaker"/> annonce a voix haute ou il va, et seuls ceux qui sont a portee
    /// l'entendent. Renvoie le nombre d'allies que l'annonce a effectivement renseignes.
    ///
    /// <para>
    /// Ce n'est pas un <see cref="Talk"/> au rabais mais un echange d'une autre <b>cadence</b>.
    /// Se raconter ce qu'on a vu attend la passe d'une fois par seconde, ce qui est realiste et sans
    /// consequence. Une annonce, non : elle ne sert qu'a ce que l'allie qui decide juste apres en
    /// tienne compte, et arrivee au tour suivant elle ne sert plus a rien. Trois survivants partis
    /// du meme point s'en apercevaient — decidant tous les trois depuis le meme instantane
    /// d'intentions perimees, ils choisissaient la meme frontiere, puis la quittaient ensemble pour
    /// la meme suivante.
    /// </para>
    ///
    /// <para>
    /// Rien n'est tire au sort : c'est l'ordre du groupe qui departage, donc a graine egale le
    /// resultat reste egal.
    /// </para>
    /// </summary>
    public int Announce(Survivor speaker, IEnumerable<Survivor> listeners)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        ArgumentNullException.ThrowIfNull(listeners);

        int heard = 0;

        foreach (var listener in listeners)
        {
            if (!CanTalk(speaker, listener)) continue;
            if (listener.Knowledge.LearnIntentFrom(speaker.Knowledge, speaker.Id)) heard++;
        }

        return heard;
    }

    /// <summary>Oublie la cadence en cours : nouvelle partie.</summary>
    public void Reset()
    {
        _lastPassAt = double.NegativeInfinity;
        ExchangeCount = 0;
    }
}

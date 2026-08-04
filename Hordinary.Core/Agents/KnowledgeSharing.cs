using Hordinary.Config;
using Hordinary.World;

namespace Hordinary.Agents;

/// <summary>Un echange : qui a parle a qui, ce que l'auditeur y a gagne, et a quelle distance.</summary>
public readonly record struct KnowledgeExchange(string From, string To, int Learned, int Distance, double At)
{
    public override string ToString()
        => $"{From} informe {To} a {Distance} cases : {Learned} croyance(s)";
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
            || speaker.Position.ChebyshevDistanceTo(listener.Position) <= Settings.CommunicationRange;
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
    /// distance ; elles n'entrent dans une carte mentale que par cette ligne, ce qui est exactement
    /// ce qui rend <c>HealAlly</c> conditionne a une rencontre.
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
            panic: speaker.Panic);

        int learned = listener.Knowledge.LearnFrom(speaker.Knowledge);

        ExchangeCount++;
        Exchanged?.Invoke(new KnowledgeExchange(
            speaker.Name,
            listener.Name,
            learned,
            speaker.Position.ChebyshevDistanceTo(listener.Position),
            now));

        return learned;
    }

    /// <summary>Oublie la cadence en cours : nouvelle partie.</summary>
    public void Reset()
    {
        _lastPassAt = double.NegativeInfinity;
        ExchangeCount = 0;
    }
}

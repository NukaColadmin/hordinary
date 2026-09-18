using System.Text;
using Hordinary.AI.Planning.Pddl;

namespace Hordinary.AI.Instructions;

/// <summary>D'ou vient le verdict. Le joueur a le droit de savoir si un modele l'a lu ou non.</summary>
public enum InstructionSource
{
    /// <summary>Le service a traduit la phrase.</summary>
    Service = 0,

    /// <summary>La meme phrase avait deja ete traduite dans cette partie : rien n'est reparti sur le reseau.</summary>
    Cache,

    /// <summary><c>instructions.enabled</c> est a <c>false</c>. Aucune requete n'a ete emise.</summary>
    Disabled,

    /// <summary>Le plafond de depense de la session est atteint.</summary>
    Budget,

    /// <summary>Le service n'a pas repondu dans <c>timeout_ms</c>.</summary>
    Timeout,

    /// <summary>Le service est injoignable, ou a repondu autre chose que le contrat.</summary>
    Transport,

    /// <summary>Le service a refuse la phrase lui-meme (noeud <c>project</c>).</summary>
    ServiceRefusal,

    /// <summary>Le service a rendu une contrainte que le portillon 1 refuse.</summary>
    Grammar,
}

/// <summary>Le verdict d'une traduction, avant que les portillons 2 et 4 n'aient leur mot a dire.</summary>
/// <param name="Constraint">La contrainte dans la grammaire fermee, quand il y en a une.</param>
/// <param name="Reason">Ce qui est montre au joueur.</param>
/// <param name="Fallback">Vrai si le joueur doit se rabattre sur les instructions pre-ecrites.</param>
public sealed record InstructionOutcome(
    bool Accepted,
    string? Constraint,
    string Reason,
    InstructionSource Source,
    bool Fallback,
    InstructionReply? Reply = null)
{
    public override string ToString() => Accepted ? $"{Constraint} ({Source})" : $"{Reason} ({Source})";
}

/// <summary>
/// Ce que le service sait faire, vu du coeur : une phrase entre, une reponse au contrat sort.
///
/// <para>
/// C'est un delegue et non une interface a implementer, et c'est ce qui permet a
/// <see cref="InstructionBroker"/> de vivre dans <c>Hordinary.Core</c> : le transport est HTTP dans
/// le jeu, une reponse en dur dans les tests, et le coeur ne connait ni l'un ni l'autre. L'invariant
/// « <c>Hordinary.Core</c> ne reference aucun client HTTP » n'est donc pas une discipline mais une
/// consequence — il n'y a pas d'endroit ou en mettre un.
/// </para>
/// </summary>
public delegate Task<InstructionReply?> InstructionTransport(InstructionRequest request, CancellationToken cancellation);

/// <summary>
/// Tout ce qu'il y a a decider autour d'un appel au service de langage, et rien de ce qu'il faut pour
/// l'emettre (livrable 15).
///
/// <para>
/// La repartition est le point de la classe. Le cache, le plafond de depense, le delai, la reprise,
/// le portillon 1 sur la reponse et le repli sur les instructions pre-ecrites sont des <b>decisions</b>,
/// donc testables sans reseau et testees comme telles. Le HTTP est un <b>transport</b>, donc dans
/// <c>Scripts/</c>, ou il tient en une vingtaine de lignes qu'aucune logique n'habite.
/// </para>
///
/// <para>
/// Le determinisme du jeu ne vient jamais du modele. Il vient de deux choses : ce cache, dont la cle
/// est <c>(phrase normalisee, graine, empreinte du domaine)</c>, et le fait qu'une contrainte
/// acceptee est figee dans la mission. Rejouer une mission ne reinterroge aucun modele — c'est ce qui
/// permet d'utiliser un fournisseur distant sans rendre les parties irreproductibles.
/// </para>
/// </summary>
public sealed class InstructionBroker(InstructionSettings settings, InstructionTransport transport)
{
    private readonly Dictionary<string, InstructionOutcome> _cache = new(StringComparer.Ordinal);

    public InstructionSettings Settings { get; } = settings ?? throw new ArgumentNullException(nameof(settings));

    private readonly InstructionTransport _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    /// <summary>Ce que la session a deja coute, en dollars, tel que le fournisseur le rapporte.</summary>
    public double Spent { get; private set; }

    /// <summary>Nombre de requetes reellement emises. Un test s'en sert pour prouver qu'il n'y en a eu aucune.</summary>
    public int Calls { get; private set; }

    /// <summary>Vrai si une requete est encore possible : service actif et plafond non atteint.</summary>
    public bool Available => Settings.Enabled && Spent < Settings.OpenRouter.MaxCostUsdPerSession;

    /// <summary>
    /// Traduit une phrase de joueur en contrainte de la grammaire fermee.
    ///
    /// <para>
    /// La methode ne leve jamais : un service absent, lent ou bavard est un cas de fonctionnement
    /// normal, pas une exception. Ce qui en sort est toujours un verdict affichable, et
    /// <see cref="InstructionOutcome.Fallback"/> dit si le joueur doit se rabattre sur les
    /// instructions pre-ecrites.
    /// </para>
    /// </summary>
    public async Task<InstructionOutcome> TranslateAsync(
        string? utterance,
        int seed,
        PddlDomain domain,
        IEnumerable<TacticalConstraint>? active = null,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(domain);

        string phrase = Normalize(utterance, Settings.MaxChars);

        if (phrase.Length == 0)
            return new InstructionOutcome(false, null, "Instruction vide.", InstructionSource.Grammar, false);

        if (!Settings.Enabled)
        {
            return new InstructionOutcome(
                false,
                null,
                "Service de langage coupe (instructions.enabled). Les instructions pre-ecrites portent les memes contraintes.",
                InstructionSource.Disabled,
                Fallback: true);
        }

        var digest = DomainDigest.From(domain, active);
        string key = CacheKey(phrase, seed, digest.DomainHash);

        if (Settings.CacheEnabled && _cache.TryGetValue(key, out var cached))
            return cached with { Source = InstructionSource.Cache };

        if (Spent >= Settings.OpenRouter.MaxCostUsdPerSession)
        {
            // Signale, jamais silencieux : un repli muet ferait croire au joueur que le modele a
            // compris sa phrase alors que personne ne l'a lue.
            return new InstructionOutcome(
                false,
                null,
                $"Plafond de depense atteint ({Spent:0.####} / {Settings.OpenRouter.MaxCostUsdPerSession:0.##} USD). "
              + "Les instructions pre-ecrites restent disponibles.",
                InstructionSource.Budget,
                Fallback: true);
        }

        var request = new InstructionRequest(phrase, seed, digest, Settings.MaxChars);
        var outcome = await AskAsync(request, domain, cancellation).ConfigureAwait(false);

        // Un refus de transport ne se met pas en cache : le service peut etre en train de demarrer,
        // et figer « injoignable » condamnerait la phrase pour toute la partie. Un refus du service
        // ou de la grammaire, lui, est une propriete de la phrase : il se garde.
        if (Settings.CacheEnabled && outcome.Source is not (InstructionSource.Timeout or InstructionSource.Transport))
            _cache[key] = outcome;

        return outcome;
    }

    private async Task<InstructionOutcome> AskAsync(InstructionRequest request, PddlDomain domain, CancellationToken cancellation)
    {
        InstructionOutcome? last = null;

        for (int attempt = 0; attempt <= Math.Max(0, Settings.Retries); attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(Settings.TimeoutMs);

            InstructionReply? reply;

            try
            {
                Calls++;
                reply = await _transport(request, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                last = new InstructionOutcome(
                    false,
                    null,
                    $"Le service n'a pas repondu en {Settings.TimeoutMs} ms.",
                    InstructionSource.Timeout,
                    Fallback: true);

                continue;
            }
            catch (Exception error)
            {
                last = new InstructionOutcome(
                    false, null, $"Service injoignable : {error.Message}", InstructionSource.Transport, Fallback: true);

                continue;
            }

            if (reply is null)
            {
                last = new InstructionOutcome(
                    false, null, "Le service n'a rien renvoye au contrat.", InstructionSource.Transport, Fallback: true);

                continue;
            }

            Spent += reply.CostUsd;

            return Project(reply, domain);
        }

        return last ?? new InstructionOutcome(false, null, "Service injoignable.", InstructionSource.Transport, Fallback: true);
    }

    /// <summary>
    /// Le portillon 1 sur la sortie du service. C'est <b>la meme ligne de code</b> qui refuse une
    /// contrainte tapee a la main dans le panneau : rien n'est verifie parce qu'on se mefie des
    /// modeles, tout est verifie parce que c'est une contrainte.
    /// </summary>
    private InstructionOutcome Project(InstructionReply reply, PddlDomain domain)
    {
        if (!reply.Accepted)
        {
            string message = reply.Message ?? "Phrase refusee par le service.";
            string family = reply.Reason is null ? string.Empty : $" [{reply.Reason}]";

            return new InstructionOutcome(false, null, message + family, InstructionSource.ServiceRefusal, Fallback: false, reply);
        }

        string? text = reply.Constraint!.ToGrammar();

        if (text is null)
        {
            return new InstructionOutcome(
                false,
                null,
                $"Le service a rendu une contrainte incomplete : « {reply.Constraint} ». Formes attendues : {ConstraintGrammar.Forms()}.",
                InstructionSource.Grammar,
                Fallback: true,
                reply);
        }

        var grammar = ConstraintGrammar.Parse(text, domain, Settings);

        return grammar.Accepted
            ? new InstructionOutcome(true, grammar.Constraint!.Text, grammar.Constraint.Text, InstructionSource.Service, false, reply)
            : new InstructionOutcome(false, null, grammar.Reason, InstructionSource.Grammar, Fallback: true, reply);
    }

    /// <summary>
    /// La cle du cache. Les trois termes sont necessaires : la phrase parce que c'est elle qu'on
    /// traduit, la graine parce que le vocabulaire nommable depend de la carte tiree, l'empreinte du
    /// domaine parce qu'une contrainte reste vraie tant que le domaine qui la nomme n'a pas bouge.
    /// </summary>
    public static string CacheKey(string phrase, int seed, string domainHash)
        => $"{phrase}{seed}{domainHash}";

    /// <summary>
    /// Nettoie la phrase : espaces reduits, casse effacee, coupe au-dela de <c>max_chars</c>.
    ///
    /// <para>
    /// La normalisation sert le cache avant tout — « Regroupez-vous  avant d'entrer » et
    /// « regroupez-vous avant d'entrer » sont la meme demande et ne doivent pas etre payees deux
    /// fois. Le service normalise de son cote aussi (noeud <c>normalize</c>) ; les deux sont
    /// volontairement redondants, parce que le cache est ici et que la troncature doit etre la avant
    /// que les jetons ne partent.
    /// </para>
    /// </summary>
    public static string Normalize(string? utterance, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(utterance)) return string.Empty;

        var builder = new StringBuilder(utterance.Length);
        bool space = false;

        foreach (char character in utterance.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                space = true;
                continue;
            }

            if (space && builder.Length > 0) builder.Append(' ');

            space = false;
            builder.Append(char.ToLowerInvariant(character));
        }

        string phrase = builder.ToString();

        return maxChars > 0 && phrase.Length > maxChars ? phrase[..maxChars] : phrase;
    }
}

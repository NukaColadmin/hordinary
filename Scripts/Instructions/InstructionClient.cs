using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hordinary.AI.Instructions;
using Hordinary.AI.Planning.Pddl;
using Hordinary.Game;

namespace Hordinary.Instructions;

/// <summary>
/// Le client du service de langage (livrable 15). Il ne decide de rien : il poste, il lit, il rend.
///
/// <para>
/// Toute la logique — cache, plafond de depense, delai, reprise, portillon 1 sur la reponse, repli
/// sur les instructions pre-ecrites — est dans <see cref="InstructionBroker"/>, cote
/// <c>Hordinary.Core</c>, ou elle se teste sans reseau. Ce qui reste ici est le transport, et c'est
/// deliberement tout : une classe de transport qui contiendrait une decision serait une decision que
/// la suite de tests ne peut pas atteindre.
/// </para>
///
/// <para>
/// Le jeu ne connait ni cle d'API ni fournisseur. Il parle au service FastAPI de <c>service/</c>, sur
/// <c>instructions.endpoint</c>, qui parle a OpenRouter a sa place. C'est ce qui permet a la cle de
/// vivre dans une variable d'environnement du service et de n'entrer ni dans le depot, ni dans le
/// binaire du jeu, ni dans la memoire du processus Godot.
/// </para>
/// </summary>
public sealed class InstructionClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly InstructionSettings _settings;
    private readonly bool _owned;

    /// <param name="http">
    /// Injectable pour que le transport soit remplacable — par un faux dans un banc d'essai, par un
    /// client partage dans le jeu. Null : le client en cree un et le fermera.
    /// </param>
    public InstructionClient(InstructionSettings settings, HttpClient? http = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _owned = http is null;

        // Le delai est tenu par le courtier, jeton d'annulation a l'appui : un Timeout de HttpClient
        // leve une TaskCanceledException que rien ne distingue d'une annulation du joueur.
        _http = http ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        Broker = new InstructionBroker(_settings, PostAsync);
    }

    /// <summary>Le courtier, pour lire ce que la session a depense et si une requete reste possible.</summary>
    public InstructionBroker Broker { get; }

    /// <summary>
    /// Traduit une phrase du joueur. Ne leve jamais : un service absent est un cas de fonctionnement
    /// normal, et ce qui revient est toujours un verdict affichable.
    /// </summary>
    public Task<InstructionOutcome> TranslateAsync(
        string? utterance,
        int seed,
        PddlDomain domain,
        System.Collections.Generic.IEnumerable<TacticalConstraint>? active = null,
        CancellationToken cancellation = default)
        => Broker.TranslateAsync(utterance, seed, domain, active, cancellation);

    private async Task<InstructionReply?> PostAsync(InstructionRequest request, CancellationToken cancellation)
    {
        using var content = new StringContent(request.ToJson(), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(_settings.Endpoint, content, cancellation).ConfigureAwait(false);

        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            Trace.Warn($"[Instruction] service {(int)response.StatusCode} : {Excerpt(body)}");
            return null;
        }

        return InstructionReply.FromJson(body);
    }

    /// <summary>Le journal montre de quoi comprendre, pas le corps entier d'une reponse hors contrat.</summary>
    private static string Excerpt(string body)
        => body.Length <= 200 ? body : body[..200] + "…";

    public void Dispose()
    {
        if (_owned) _http.Dispose();
    }
}

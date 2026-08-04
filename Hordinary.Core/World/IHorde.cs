using Hordinary.Agents;

namespace Hordinary.World;

/// <summary>
/// La horde, vue par ce qui peut la frapper (livrable 8, « combats simples pour le MVP »).
///
/// L'interface est minuscule et volontairement pauvre : le moteur d'execution d'un plan a besoin de
/// retrouver un zombie par son identifiant et de le retirer du jeu quand il tombe, rien de plus. Ni
/// la Utility AI ni le planificateur ne la voient — eux ne connaissent que des croyances, et frapper
/// une menace qu'on croit la est justement une action qui peut echouer.
///
/// C'est aussi ce qui permet de derouler une mission de combat en test : un groupe sans horde recoit
/// <c>null</c>, et les actions offensives echouent proprement au lieu de frapper dans le vide.
/// </summary>
public interface IHorde
{
    IReadOnlyList<Zombie> Zombies { get; }

    /// <summary>Le zombie portant cet identifiant, s'il est encore debout.</summary>
    Zombie? Find(string id);

    /// <summary>Retire un zombie du jeu — de la horde comme du monde.</summary>
    bool Remove(string id);
}

/// <summary>
/// Horde resolue au moment de frapper, et non a la construction de l'agent.
///
/// Les survivants sont enroles avant que la horde n'existe : ils choisissent ou apparaitre, et c'est
/// leur point de depart qui decide ensuite ou les zombies n'ont pas le droit d'apparaitre. Plutot que
/// d'imposer un ordre de construction que le premier refactoring casserait en silence — les actions
/// de combat echoueraient alors sans que rien ne le signale —, l'agent recoit ce relais et la
/// question « ou est la horde ? » est posee au moment ou elle a une reponse.
/// </summary>
public sealed class DeferredHorde(Func<IHorde?> resolve) : IHorde
{
    private readonly Func<IHorde?> _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));

    public IReadOnlyList<Zombie> Zombies => _resolve()?.Zombies ?? [];

    public Zombie? Find(string id) => _resolve()?.Find(id);

    public bool Remove(string id) => _resolve()?.Remove(id) ?? false;
}

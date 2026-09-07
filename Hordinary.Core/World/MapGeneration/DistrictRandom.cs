using Hordinary.Config;

namespace Hordinary.World.MapGeneration;

/// <summary>
/// Le hasard du generateur : <see cref="Deterministic"/>, avec un compteur d'etapes.
///
/// <see cref="Deterministic"/> est volontairement adresse plutot que sequentiel, pour qu'ajouter un
/// tirage au milieu du code ne decale pas tous les suivants. Une generation de carte, elle, ne peut
/// pas nommer l'adresse de chacun de ses milliers de tirages — elle en fait un par mur, par porte,
/// par zombie. Le compteur est donc assume ici, et son inconvenient tenu par un seul moyen : chaque
/// passe recoit son propre canal (<see cref="Fork"/>). Retoucher le peuplement ne peut alors pas
/// deplacer un mur, ce qui est la seule regression qu'on avait a craindre.
/// </summary>
public sealed class DistrictRandom(int seed, string channel)
{
    private int _step;

    public int Seed { get; } = seed;

    public string Channel { get; } = channel;

    /// <summary>Un canal derive : le meme quartier, un tirage qui n'interfere pas avec le voisin.</summary>
    public DistrictRandom Fork(string name) => new(Seed, $"{Channel}.{name}");

    /// <summary>Un tirage dans <c>[0, 1[</c>. Le role nomme ce qu'on tire, pour la lisibilite du journal.</summary>
    public float Unit(string role) => Deterministic.Unit(Seed, Channel, _step++, role);

    /// <summary>Un entier dans <c>[min, max]</c>, bornes incluses.</summary>
    public int Between(int min, int max, string role)
        => max <= min ? min : min + (int)(Unit(role) * (max - min + 1));

    /// <summary>Un entier tire dans un intervalle de configuration, bornes arrondies.</summary>
    public int Pick(RollRange range, string role)
        => Between((int)MathF.Round(range.Min), (int)MathF.Round(range.Max), role);

    /// <summary>Un reel tire dans un intervalle de configuration.</summary>
    public float Range(RollRange range, string role) => range.Roll(() => Unit(role));

    public bool Chance(float probability, string role)
        => probability > 0f && Unit(role) < probability;

    public T Choose<T>(IReadOnlyList<T> items, string role)
    {
        if (items.Count == 0) throw new InvalidOperationException($"Rien a tirer pour « {role} ».");
        return items[Between(0, items.Count - 1, role)];
    }

    /// <summary>Melange en place (Fisher-Yates). Deux appels a la meme etape donnent le meme ordre.</summary>
    public void Shuffle<T>(IList<T> items, string role)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = Between(0, i, role);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}

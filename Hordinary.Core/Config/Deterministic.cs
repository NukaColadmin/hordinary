namespace Hordinary.Config;

/// <summary>
/// Le hasard reproductible du projet : un nombre entre 0 et 1, entierement determine par une graine,
/// un canal et un numero d'etape.
///
/// Ce n'est volontairement pas un generateur qu'on avance. Un generateur sequentiel rendrait le
/// resultat dependant de <b>l'ordre</b> des appels : ajouter un tirage au milieu du code decalerait
/// tous les suivants, et une expedition ou une zone qu'on croyait figees changeraient sans que la
/// configuration ait bouge. Ici, chaque tirage a une <b>adresse</b> — et deux tirages faits a la meme
/// adresse donnent la meme valeur, ce qui est exactement ce qu'on veut d'un monde qu'on rejoue.
/// </summary>
public static class Deterministic
{
    /// <summary>
    /// Un tirage dans <c>[0, 1[</c>. Le canal nomme ce qu'on tire — « check », « loot », « door » —
    /// pour que deux tirages differents de la meme etape ne se confondent pas.
    /// </summary>
    public static float Unit(int seed, string channel, int step, string? role = null)
    {
        unchecked
        {
            uint hash = 2166136261u;

            foreach (char c in channel) hash = (hash ^ c) * 16777619u;
            if (role is not null) foreach (char c in role) hash = (hash ^ c) * 16777619u;

            hash = (hash ^ (uint)seed) * 16777619u;
            hash = (hash ^ (uint)step) * 16777619u;

            // Melange final (xorshift) : sans lui, deux etapes voisines donnent deux valeurs voisines,
            // et une expedition entiere se deroulerait en pente douce.
            hash ^= hash >> 15;
            hash *= 2246822519u;
            hash ^= hash >> 13;
            hash *= 3266489917u;
            hash ^= hash >> 16;

            return (hash & 0xFFFFFF) / (float)0x1000000;
        }
    }

    /// <summary>Un entier dans <c>[min, max]</c>, bornes incluses.</summary>
    public static int Between(int seed, string channel, int step, int min, int max, string? role = null)
        => max <= min ? min : min + (int)(Unit(seed, channel, step, role) * (max - min + 1));
}

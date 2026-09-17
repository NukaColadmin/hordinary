using Godot;

namespace Hordinary.Agents;

/// <summary>
/// La barre de points de vie qui surmonte un corps, survivant comme zombie.
///
/// Elle est ecrite une fois pour les deux : un survivant a 40 % et un zombie a 40 % doivent se lire
/// de la meme facon, sinon la barre cesse d'etre une jauge pour devenir une decoration propre a
/// chaque camp. Ce qui distingue les deux est la <b>couleur</b> que l'appelant passe — celle du
/// vivant qu'on protege, celle du mort qu'on abat — et la largeur, prise sur la taille de tuile pour
/// qu'un changement de zoom ne fasse pas deborder la barre du corps.
///
/// C'est une methode statique et non un noeud : ajouter un enfant par entite ferait trois noeuds de
/// plus pour trois survivants, et une vingtaine pour la horde, la ou <c>_Draw</c> les dessine deja.
/// </summary>
public static class HealthBar
{
    /// <summary>Hauteur de la barre, en pixels d'ecran.</summary>
    private const float Height = 3f;

    /// <summary>Epaisseur du cadre sombre qui detache la barre du sol.</summary>
    private const float Border = 1f;

    private static readonly Color Frame = new(0.06f, 0.05f, 0.04f, 0.75f);

    /// <summary>Fond de la portion perdue : pas du vide, sans quoi on ne verrait plus la jauge pleine.</summary>
    private static readonly Color Empty = new(0.20f, 0.18f, 0.16f, 0.85f);

    /// <summary>
    /// Dessine la barre au-dessus de l'origine du noeud.
    /// </summary>
    /// <param name="canvas">Le noeud en train de se dessiner : l'appel vient de son <c>_Draw</c>.</param>
    /// <param name="ratio">Sante restante, de 0 a 1. Hors bornes, elle est ramenee dedans.</param>
    /// <param name="width">Largeur de la barre, en pixels.</param>
    /// <param name="above">Hauteur a laquelle la poser au-dessus du centre du corps, en pixels.</param>
    /// <param name="fill">Couleur de la portion restante.</param>
    public static void Draw(CanvasItem canvas, float ratio, float width, float above, Color fill)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        float left = -width * 0.5f;
        float top = -above;

        canvas.DrawRect(
            new Rect2(left - Border, top - Border, width + 2f * Border, Height + 2f * Border),
            Frame);

        canvas.DrawRect(new Rect2(left, top, width, Height), Empty);

        float remaining = width * Mathf.Clamp(ratio, 0f, 1f);
        if (remaining > 0f) canvas.DrawRect(new Rect2(left, top, remaining, Height), fill);
    }
}

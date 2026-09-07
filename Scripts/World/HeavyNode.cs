using Godot;

namespace Hordinary.World;

/// <summary>
/// Le corps visible d'un meuble poussable : un sprite pose sur l'empreinte que le meuble occupe
/// reellement, et rien de plus. Toute la regle vit dans <see cref="HeavyObject"/> — ce noeud lit une
/// position et une taille, comme <c>SurvivorNode</c> lit une position.
///
/// Il existe parce que la couche de tuiles ne savait pas dire de quoi elle parlait. Un rayonnage, une
/// benne et une epave de voiture sont tous du <see cref="CellType.Obstacle"/>, et la legende ne
/// connait qu'une tuile par terrain : le quartier genere se retrouvait donc pave de voitures
/// miniatures, une par meuble, quelle que soit sa nature. Un sprite par meuble rend a chacun son
/// image — et permet a l'epave de faire les quatre cases qu'une voiture doit faire, ce qu'une tuile
/// de trente-deux pixels ne pourra jamais.
/// </summary>
public partial class HeavyNode : Sprite2D
{
    /// <summary>
    /// La seule table du programme qui nomme une image. Une categorie sans entree retombe sur les
    /// gravats : mieux vaut un tas anonyme qu'une case vide ou l'on se cogne.
    /// </summary>
    private static readonly Dictionary<string, string> Textures = new(StringComparer.OrdinalIgnoreCase)
        {
            ["wreck"] = "res://Assets/Obstacles/ObstacleCarWreck.png",
            ["shelf"] = "res://Assets/Obstacles/ObstacleShelfFull.png",
            ["wardrobe"] = "res://Assets/Obstacles/ObstacleCounter.png",
            ["dumpster"] = "res://Assets/Obstacles/ObstacleBin.png",
        };

    private const string Fallback = "res://Assets/Obstacles/ObstacleRubble.png";

    public void Configure(HeavyObject heavy, int tileSize)
    {
        ArgumentNullException.ThrowIfNull(heavy);

        Texture = GD.Load<Texture2D>(Textures.GetValueOrDefault(heavy.Kind, Fallback));
        if (Texture is null) return;

        Centered = false;

        // L'image couvre exactement l'empreinte : ce que le joueur contourne est ce qu'il voit. La
        // mise a l'echelle non uniforme reste assumee — un meuble n'est pas toujours carre, et
        // l'image doit suivre l'empreinte plutot que l'inverse.
        Scale = new Vector2(
            heavy.Width * tileSize / (float)Texture.GetWidth(),
            heavy.Height * tileSize / (float)Texture.GetHeight());

        Position = new Vector2(heavy.Position.X * tileSize, heavy.Position.Y * tileSize);
    }
}

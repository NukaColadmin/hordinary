using Godot;

namespace Hordinary.World;

/// <summary>
/// Habillage visuel de la zone d'extraction : un skin, rien de plus. Le van ne stocke rien, ne
/// s'ameliore pas et n'entre dans aucun calcul — c'est <see cref="ZoneKind.Extraction"/> qui fait
/// tout le travail de jeu, comme n'importe quelle autre zone. Ce noeud se contente de poser
/// <c>Assets/Others/Van.png</c> au centre de la zone de camp.
/// </summary>
public partial class VanNode : Sprite2D
{
    /// <summary>
    /// L'emprise du van, en cases : deux de large, quatre de long. Un van est plus long qu'une
    /// voiture, et c'est ce qui le distingue d'une epave a l'ecran. Le sens est celui du dessin, qui
    /// montre le van de trois quarts arriere : l'etirer en largeur en ferait une caisse. Rien de tout
    /// cela n'entre dans le jeu ; la zone d'extraction, elle, garde sa forme.
    /// </summary>
    private const int Width = 2;
    private const int Length = 4;

    public void Configure(MapZone camp, int tileSize)
    {
        ArgumentNullException.ThrowIfNull(camp);

        Texture = GD.Load<Texture2D>("res://Assets/Others/Van.png");
        if (Texture is null) return;

        Scale = new Vector2(
            Width * tileSize / (float)Texture.GetWidth(),
            Length * tileSize / (float)Texture.GetHeight());

        Position = new Vector2(
            camp.Center.X * tileSize + tileSize * 0.5f,
            camp.Center.Y * tileSize + tileSize * 0.5f);
    }
}

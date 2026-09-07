using Godot;
using Hordinary.Config;
using Hordinary.Game;
using Hordinary.World;

namespace Hordinary.UI;

/// <summary>
/// Les vignettes d'inventaire, decoupees dans le TileSet.
///
/// Un objet n'a <b>qu'une</b> image dans Hordinary : la tuile de 32 px qui le pose au sol sert aussi
/// d'icone dans un emplacement d'inventaire, agrandie a l'entier. La direction artistique prevoyait
/// une seconde serie de dessins en trois quarts ; elle a ete abandonnee, et ce n'est pas seulement une
/// economie de quinze images. Deux dessins pour un objet, ce sont deux verites a tenir accordees : la
/// premiere fois qu'on repeint l'une sans l'autre, une conserve n'a plus la meme tete selon qu'on la
/// regarde par terre ou dans un sac. Ici, repeindre l'atlas repeint l'inventaire.
///
/// L'agrandissement se fait au plus proche voisin — c'est le reglage de projet
/// (<c>default_texture_filter = 0</c>), et il ne se redit donc pas ici : une tuile de 32 px affichee
/// a 64 reste du pixel art, pas une image floue.
/// </summary>
public sealed class ItemIcons(TileLegend legend, TileSet? tileSet, ItemCatalogue? items = null)
{
    private readonly TileLegend _legend = legend ?? throw new ArgumentNullException(nameof(legend));
    private readonly TileSet? _tileSet = tileSet;
    private readonly ItemCatalogue _items = items ?? ItemCatalogue.Empty;

    /// <summary>
    /// Les vignettes deja decoupees. Un <see cref="AtlasTexture"/> ne copie aucun pixel — il designe
    /// une region de la planche — mais un inventaire redemande la meme dix fois par image, et rien ne
    /// justifie de recreer dix ressources pour la meme conserve.
    /// </summary>
    private readonly Dictionary<string, AtlasTexture?> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// La vignette d'un objet du catalogue, ou <c>null</c> si la legende ne lui connait pas de tuile.
    /// Un appelant qui recoit <c>null</c> affiche le nom : une icone manquante ne doit jamais faire
    /// disparaitre l'objet lui-meme.
    /// </summary>
    public Texture2D? For(string? itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId) || _tileSet is null) return null;

        if (_cache.TryGetValue(itemId, out var cached)) return cached;

        var icon = Cut(itemId);
        _cache[itemId] = icon;
        return icon;
    }

    /// <summary>
    /// La vignette de la reserve du camp : celle de l'objet qui la remplit. Aucun nom d'objet n'est
    /// ecrit ici — c'est <see cref="CampStock.StockerOf"/> qui lit dans le catalogue lequel alimente
    /// la nourriture, et changer d'objet nourricier reste une modification de <c>config/items.json</c>.
    /// </summary>
    public Texture2D? ForResource(string resource)
        => For(CampStock.StockerOf(resource, _items)?.Id);

    /// <summary>
    /// Un emplacement pret a poser dans une interface : la vignette a la taille demandee, ou
    /// <c>null</c> quand l'objet n'a pas de tuile — l'appelant garde alors sa mise en page en texte.
    /// </summary>
    public TextureRect? Slot(string? itemId, int size = 32)
    {
        if (For(itemId) is not { } icon) return null;

        return new TextureRect
        {
            Texture = icon,
            CustomMinimumSize = new Vector2(size, size),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
    }

    /// <summary>Le meme emplacement, pour la reserve du camp plutot que pour un objet nomme.</summary>
    public TextureRect? SlotForResource(string resource, int size = 32)
        => Slot(CampStock.StockerOf(resource, _items)?.Id, size);

    /// <summary>
    /// Decoupe la region de la planche. La legende donne l'adresse, le TileSet donne le rectangle :
    /// une tuile qui bouge dans l'atlas suit sa cle, et rien ici ne connait de coordonnee en pixels.
    /// </summary>
    private AtlasTexture? Cut(string itemId)
    {
        if (_legend.TileOf(itemId) is not { } address) return null;

        if (_tileSet!.GetSource(address.Source) is not TileSetAtlasSource source) return null;

        var coords = new Vector2I(address.X, address.Y);
        if (!source.HasTile(coords)) return null;

        return new AtlasTexture
        {
            Atlas = source.Texture,
            Region = source.GetTileTextureRegion(coords),

            // Sans ce drapeau, l'agrandissement va chercher les pixels des tuiles voisines sur les
            // bords : une conserve porterait un lisere du bidon d'eau qui la suit dans la planche.
            FilterClip = true,
        };
    }
}

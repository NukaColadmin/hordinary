using Godot;

namespace Hordinary.UI;

/// <summary>
/// Les images d'interface du livrable 12 : les trois cadres 9-slice, les trois etats de bouton et les
/// six icones d'etat, chargees une fois et partagees par tous les ecrans.
///
/// Aucun ecran ne connait un chemin de fichier. C'est ce qui permet de repeindre l'interface entiere
/// en remplacant les images d'<c>Assets/UI/</c>, sans toucher une ligne de mise en page — et ce qui
/// evite qu'un cadre manquant fasse tomber un ecran : une texture absente rend un <c>null</c>, et
/// l'appelant retombe sur un aplat de la palette.
/// </summary>
public static class UiSkin
{
    private const string Root = "res://Assets/UI/";

    /// <summary>
    /// Largeur de bordure de chaque cadre, en pixels de l'image. C'est la seule donnee normative d'un
    /// 9-slice : le centre est etire par le moteur, les bordures ne le sont jamais. Elle est mesuree
    /// sur l'image et non deduite d'une regle — les trois cadres n'ont pas le meme ornement de coin.
    /// </summary>
    private const int LargeBorder = 38;
    private const int MediumBorder = 30;
    private const int SmallBorder = 25;
    private const int ButtonBorder = 13;

    /// <summary>
    /// Hauteur en deca de laquelle un bouton 9-slice s'ecrase : ses bordures haute et basse ne
    /// tiennent plus dans sa hauteur, et le moteur les comprime au lieu de les poser. Un bouton plus
    /// court que cela n'est pas une mise en page serree, c'est un dessin casse.
    /// </summary>
    public const int MinimumButtonHeight = ButtonBorder * 2 + 12;

    /// <summary>Air laisse entre la bordure de metal et le contenu, en pixels.</summary>
    private const int Padding = 6;

    private static readonly Dictionary<string, Texture2D?> Cache = [];

    /// <summary>
    /// La texture d'<c>Assets/UI/</c>, ou <c>null</c> si elle n'a pas ete livree. Une image manquante
    /// n'est pas une erreur d'execution : l'interface se rabat sur un aplat, et le jeu reste jouable.
    /// </summary>
    public static Texture2D? Texture(string fileName)
    {
        if (Cache.TryGetValue(fileName, out var cached)) return cached;

        string path = Root + fileName;
        var texture = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;

        Cache[fileName] = texture;
        return texture;
    }

    // --- Cadres ------------------------------------------------------------------------------------

    public static StyleBox? FrameLarge() => Frame("ui_frame_large.png", LargeBorder);

    public static StyleBox? FrameMedium() => Frame("ui_frame_medium.png", MediumBorder);

    public static StyleBox? FrameSmall() => Frame("ui_frame_small.png", SmallBorder);

    /// <summary>
    /// Un cadre etirable. <see cref="StyleBoxTexture.TextureMarginLeft"/> et ses trois voisins disent
    /// au moteur ou couper : sans eux, agrandir le cadre etirerait ses rivets avec le reste.
    /// </summary>
    private static StyleBox? Frame(string fileName, int border)
    {
        if (Texture(fileName) is not { } texture) return null;

        var box = new StyleBoxTexture { Texture = texture };

        box.TextureMarginLeft = border;
        box.TextureMarginRight = border;
        box.TextureMarginTop = border;
        box.TextureMarginBottom = border;

        // Le contenu se pose a l'interieur du metal, pas dessus. La marge est donc au moins l'epaisseur
        // de la bordure : plus petite, un portrait recouvrirait les rivets du coin.
        float padded = border + Padding;

        box.ContentMarginLeft = padded;
        box.ContentMarginRight = padded;
        box.ContentMarginTop = padded;
        box.ContentMarginBottom = padded;

        return box;
    }

    // --- Boutons -----------------------------------------------------------------------------------

    /// <summary>
    /// Habille un bouton des trois plaques de metal. Sans images, le bouton garde le style du theme
    /// Godot — moche mais fonctionnel, ce qui est exactement ce qu'on veut d'un repli.
    /// </summary>
    public static void Dress(Button button)
    {
        ArgumentNullException.ThrowIfNull(button);

        if (ButtonBox("ui_button_idle.png") is { } idle)
        {
            button.AddThemeStyleboxOverride("normal", idle);
            button.AddThemeStyleboxOverride("disabled", Faded(idle));
        }

        if (ButtonBox("ui_button_hover.png") is { } hover) button.AddThemeStyleboxOverride("hover", hover);
        if (ButtonBox("ui_button_pressed.png") is { } pressed) button.AddThemeStyleboxOverride("pressed", pressed);

        // Le texte se lit sur du metal clair : il est sombre, et non blanc comme sur le theme par defaut.
        button.AddThemeColorOverride("font_color", new Color(0.16f, 0.12f, 0.08f));
        button.AddThemeColorOverride("font_hover_color", new Color(0.1f, 0.07f, 0.04f));
        button.AddThemeColorOverride("font_pressed_color", new Color(0.1f, 0.07f, 0.04f));
        button.AddThemeColorOverride("font_disabled_color", new Color(0.42f, 0.38f, 0.33f));

        // Le plancher de hauteur va avec la plaque : un appelant qui demande plus est libre, un
        // appelant qui n'a rien demande ne se retrouve pas avec des rivets ecrases.
        button.CustomMinimumSize = new Vector2(
            button.CustomMinimumSize.X,
            Math.Max(button.CustomMinimumSize.Y, MinimumButtonHeight));
    }

    /// <summary>
    /// Une ligne de liste : un aplat, pas la plaque de metal. Etiree sur 500 px de large et 40 de
    /// haut, la plaque devient une bavure — ses taches de rouille et ses rivets sont dessines pour un
    /// carre. La plaque reste donc au bouton d'action, et les listes prennent la palette sans l'image.
    /// </summary>
    public static void DressRow(Button button)
    {
        ArgumentNullException.ThrowIfNull(button);

        button.AddThemeStyleboxOverride("normal", Row(new Color(0.24f, 0.21f, 0.17f), new Color(0.36f, 0.31f, 0.26f)));
        button.AddThemeStyleboxOverride("hover", Row(new Color(0.32f, 0.27f, 0.21f), new Color(0.52f, 0.44f, 0.34f)));
        button.AddThemeStyleboxOverride("pressed", Row(new Color(0.45f, 0.32f, 0.16f), new Color(0.93f, 0.72f, 0.36f)));
        button.AddThemeStyleboxOverride("focus", Row(new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0f)));

        button.AddThemeColorOverride("font_color", new Color(0.82f, 0.78f, 0.72f));
        button.AddThemeColorOverride("font_hover_color", new Color(0.96f, 0.92f, 0.86f));
        button.AddThemeColorOverride("font_pressed_color", new Color(1f, 0.95f, 0.86f));
    }

    private static StyleBoxFlat Row(Color background, Color border)
    {
        var box = new StyleBoxFlat { BgColor = background, BorderColor = border };

        box.SetCornerRadiusAll(3);
        box.SetBorderWidthAll(1);

        box.ContentMarginLeft = 12f;
        box.ContentMarginRight = 12f;
        box.ContentMarginTop = 6f;
        box.ContentMarginBottom = 6f;

        return box;
    }

    private static StyleBoxTexture? ButtonBox(string fileName)
    {
        if (Texture(fileName) is not { } texture) return null;

        var box = new StyleBoxTexture { Texture = texture };

        box.TextureMarginLeft = ButtonBorder;
        box.TextureMarginRight = ButtonBorder;
        box.TextureMarginTop = ButtonBorder;
        box.TextureMarginBottom = ButtonBorder;

        box.ContentMarginLeft = 14f;
        box.ContentMarginRight = 14f;
        box.ContentMarginTop = 8f;
        box.ContentMarginBottom = 8f;

        return box;
    }

    private static StyleBoxTexture Faded(StyleBoxTexture source)
    {
        var box = (StyleBoxTexture)source.Duplicate();
        box.ModulateColor = new Color(0.6f, 0.6f, 0.6f, 0.7f);
        return box;
    }

    // --- Icones d'etat -----------------------------------------------------------------------------

    /// <summary>
    /// Les six etats de la grille du bas, dans l'ordre du livrable 12. Ce sont les seuls : en ajouter
    /// un demande une image, ce qui est la garantie qu'aucun etat ne s'affiche sans avoir ete dessine.
    /// </summary>
    public static Texture2D? Status(string state) => Texture($"ui_status_{state}.png");

    // --- Portraits ---------------------------------------------------------------------------------

    /// <summary>
    /// Le portrait d'un personnage (<c>female_1</c>, <c>male_3</c>…). Il vit dans
    /// <c>Assets/Portraits/</c> et non dans <c>Assets/UI/</c> : c'est un personnage, pas une piece
    /// d'interface.
    /// </summary>
    public static Texture2D? Portrait(string character)
    {
        string path = $"res://Assets/Portraits/portrait_{character}.png";

        if (Cache.TryGetValue(path, out var cached)) return cached;

        var texture = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
        Cache[path] = texture;
        return texture;
    }

    /// <summary>
    /// Les six personnages livres, dans l'ordre. Un survivant sans <c>character</c> declare en recoit
    /// un par son rang dans le roster : deux survivants peuvent partager un personnage, et c'est
    /// l'anneau de couleur qui les separe.
    /// </summary>
    public static readonly string[] Characters =
        ["female_1", "male_1", "female_2", "male_2", "female_3", "male_3"];

    public static string CharacterAt(int index)
        => Characters[((index % Characters.Length) + Characters.Length) % Characters.Length];
}

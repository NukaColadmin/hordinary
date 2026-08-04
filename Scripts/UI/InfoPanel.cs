using Godot;

namespace Hordinary.UI;

/// <summary>
/// Socle commun des trois panneaux d'inspection du livrable 8 (<c>UI/HUD</c>, <c>UI/AgentPanel</c>,
/// <c>UI/PlanPanel</c> de la section A).
///
/// Il ne porte aucune information : seulement la mise en forme — police, contour, ancrage — pour que
/// les panneaux se distinguent par ce qu'ils disent et non par la facon dont ils le disent. Le
/// contour noir n'est pas une coquetterie : le texte se lit par-dessus une carte claire comme par
/// dessus une rue sombre, et sans lui la moitie du HUD disparait selon l'endroit ou l'on regarde.
/// </summary>
public partial class InfoPanel : Label
{
    /// <summary>Marge, en pixels, entre le panneau et le bord de l'ecran auquel il est ancre.</summary>
    private const float Margin = 12f;

    protected InfoPanel(string name, int fontSize = 13)
    {
        Name = name;
        Modulate = new Color(1f, 1f, 1f, 0.92f);

        AddThemeFontSizeOverride("font_size", fontSize);
        AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
        AddThemeConstantOverride("outline_size", 4);
    }

    /// <summary>Ancre le panneau dans un coin de l'ecran, quelle que soit la taille de la fenetre.</summary>
    public void AnchorTo(HorizontalAlignment horizontal, VerticalAlignment vertical)
    {
        (float left, float right) = horizontal switch
        {
            HorizontalAlignment.Right => (1f, 1f),
            _ => (0f, 0f),
        };

        (float top, float bottom) = vertical switch
        {
            VerticalAlignment.Bottom => (1f, 1f),
            _ => (0f, 0f),
        };

        AnchorLeft = left;
        AnchorRight = right;
        AnchorTop = top;
        AnchorBottom = bottom;

        HorizontalAlignment = horizontal;
        GrowHorizontal = horizontal == HorizontalAlignment.Right ? GrowDirection.Begin : GrowDirection.End;
        GrowVertical = vertical == VerticalAlignment.Bottom ? GrowDirection.Begin : GrowDirection.End;

        OffsetLeft = horizontal == HorizontalAlignment.Right ? -Margin : Margin;
        OffsetRight = horizontal == HorizontalAlignment.Right ? -Margin : Margin;
        OffsetTop = vertical == VerticalAlignment.Bottom ? -Margin : Margin;
        OffsetBottom = vertical == VerticalAlignment.Bottom ? -Margin : Margin;
    }

    /// <summary>Ecrit le panneau, en evitant le travail d'affichage quand rien n'a change.</summary>
    protected void Write(string text)
    {
        if (Text == text) return;
        Text = text;
    }
}

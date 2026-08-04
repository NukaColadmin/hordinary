using Godot;
using Hordinary.Agents;

namespace Hordinary.UI;

/// <summary>
/// La fiche d'un agent dans la grille du bas : son nom, son role, et ses deux jauges.
///
/// <code>
/// +----------------+
/// |     ALICE      |
/// |   Eclaireuse   |
/// | HP      45/100 |
/// | [========----] |
/// | SANITY 150/200 |
/// | [==========--] |
/// +----------------+
/// </code>
///
/// Les deux jauges disent deux choses differentes, et c'est pour cela qu'elles sont cote a cote.
/// <c>HP</c> est ce que le corps encaisse pendant une mission ; <c>SANITY</c> est la motivation, ce
/// qui reste une fois la peur passee. Un agent peut rentrer indemne et vide, ou couvert de plaies et
/// pret a repartir — et le joueur choisit sa colonne en regardant les deux.
///
/// La fiche sert aux deux ecrans : dans la preparation elle se clique pour enroler ou retirer un
/// agent, en mission elle se clique pour changer de point de vue. C'est le meme geste, sur le meme
/// dessin, et cela evite au joueur d'apprendre deux interfaces.
/// </summary>
public partial class AgentCard : PanelContainer
{
    public const int CardWidth = 168;

    private static readonly Color HealthColor = new(0.85f, 0.29f, 0.29f);
    private static readonly Color SanityColor = new(0.35f, 0.62f, 0.95f);
    private static readonly Color TrackColor = new(0.12f, 0.12f, 0.14f);

    private Label _name = null!;
    private Label _role = null!;
    private Label _health = null!;
    private Label _sanity = null!;
    private ProgressBar _healthBar = null!;
    private ProgressBar _sanityBar = null!;
    private Label _state = null!;

    private bool _selected;
    private bool _dimmed;

    /// <summary>Le profil affiche. Dans l'ecran de preparation, il n'y a pas encore de survivant.</summary>
    public SurvivorProfile? Profile { get; private set; }

    /// <summary>Emis au clic. L'ecran qui la contient decide de ce que cela veut dire.</summary>
    public event Action<AgentCard>? Clicked;

    /// <summary>Enrole, ou selectionne : la fiche s'eclaire et prend un lisere.</summary>
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            Restyle();
        }
    }

    /// <summary>Hors jeu — rentre au camp ou hors de combat. La fiche s'eteint sans disparaitre.</summary>
    public bool Dimmed
    {
        get => _dimmed;
        set
        {
            if (_dimmed == value) return;
            _dimmed = value;
            Restyle();
        }
    }

    /// <summary>
    /// Les etiquettes sont construites ici et non dans <c>_Ready</c> : une fiche doit pouvoir etre
    /// remplie a la ligne qui suit sa creation, sans dependre du moment ou elle entre dans l'arbre.
    /// </summary>
    public AgentCard()
    {
        CustomMinimumSize = new Vector2(CardWidth, 0);
        MouseFilter = MouseFilterEnum.Stop;

        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 8);

        AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 2);
        margin.AddChild(column);

        _name = Caption(column, 15, new Color(1f, 1f, 1f));
        _role = Caption(column, 11, new Color(0.72f, 0.72f, 0.76f));

        (_health, _healthBar) = Gauge(column, HealthColor);
        (_sanity, _sanityBar) = Gauge(column, SanityColor);

        _state = Caption(column, 11, new Color(0.95f, 0.78f, 0.35f));
        _state.Visible = false;

        Restyle();
    }

    // --- Contenu ---------------------------------------------------------------------------------

    /// <summary>
    /// Une fiche d'avant-mission : le profil, et les jauges pleines qu'il aura au depart. La ligne de
    /// base vient de <c>config/game_config.json</c> — sans elle, un profil qui ne redit pas sa sante
    /// s'afficherait avec les valeurs par defaut du code plutot qu'avec celles du fichier.
    /// </summary>
    public void Show(SurvivorProfile profile, SurvivorSettings baseline)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(baseline);

        Profile = profile;

        var settings = profile.ApplyTo(baseline);

        _name.Text = profile.Name.ToUpperInvariant();
        _role.Text = profile.Role ?? "Survivant";

        Write(_health, _healthBar, "HP", settings.MaxHealth, settings.MaxHealth);
        Write(_sanity, _sanityBar, "SANITY", settings.MaxSanity, settings.MaxSanity);

        TooltipText = profile.Description ?? string.Empty;
        _state.Visible = false;
    }

    /// <summary>Une fiche en mission : les jauges suivent le survivant, seconde apres seconde.</summary>
    public void Refresh(Survivor survivor)
    {
        ArgumentNullException.ThrowIfNull(survivor);

        _name.Text = survivor.Name.ToUpperInvariant();
        _role.Text = survivor.Profile.Role ?? "Survivant";

        Write(_health, _healthBar, "HP", survivor.Health, survivor.Settings.MaxHealth);
        Write(_sanity, _sanityBar, "SANITY", survivor.Sanity, survivor.Settings.MaxSanity);

        _state.Text = survivor switch
        {
            { Returned: true } => "rentre au camp",
            { Alive: false } => "hors de combat",
            { Recalled: true } => "rappele",
            _ => survivor.CurrentGoal ?? string.Empty,
        };

        _state.Visible = _state.Text.Length > 0;
        Dimmed = survivor.Returned || !survivor.Alive;
    }

    // --- Mise en forme ---------------------------------------------------------------------------

    private static Label Caption(Container parent, int size, Color color)
    {
        var label = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        parent.AddChild(label);
        return label;
    }

    private static (Label Text, ProgressBar Bar) Gauge(Container parent, Color color)
    {
        var text = new Label();
        text.AddThemeFontSizeOverride("font_size", 11);
        text.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.88f));
        parent.AddChild(text);

        var bar = new ProgressBar
        {
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 7),
        };

        bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = TrackColor, CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2 });
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = color, CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2 });

        parent.AddChild(bar);
        return (text, bar);
    }

    /// <summary>Une jauge : le chiffre exact au-dessus, la barre en dessous. Les deux se lisent.</summary>
    private static void Write(Label text, ProgressBar bar, string label, float value, float maximum)
    {
        text.Text = $"{label} {value:0}/{maximum:0}";

        bar.MaxValue = Math.Max(1d, maximum);
        bar.Value = Math.Clamp(value, 0f, maximum);
    }

    private void Restyle()
    {
        var background = new StyleBoxFlat
        {
            BgColor = _selected ? new Color(0.16f, 0.21f, 0.28f) : new Color(0.09f, 0.09f, 0.11f),
            BorderColor = _selected ? new Color(0.45f, 0.78f, 1f) : new Color(0.22f, 0.22f, 0.26f),
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
        };

        background.SetBorderWidthAll(_selected ? 2 : 1);
        AddThemeStyleboxOverride("panel", background);

        Modulate = new Color(1f, 1f, 1f, _dimmed ? 0.4f : 1f);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;

        Clicked?.Invoke(this);
        AcceptEvent();
    }
}

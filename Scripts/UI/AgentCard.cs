using Godot;
using Hordinary.Agents;

namespace Hordinary.UI;

/// <summary>
/// La vignette d'un survivant dans la grille du bas (livrable 12).
///
/// <code>
/// +----------------------+
/// |  ####   ALICE        |
/// |  ####   [=======--]  |   sante
/// |  ####   [===------]  |   panique
/// |  (!) (~) (@)         |   etats
/// +----------------------+
/// </code>
///
/// Le portrait est cercle de la <b>couleur d'anneau</b> du survivant — la meme que celle dessinee sous
/// ses pieds sur la carte. C'est le seul signe qui tient quand deux survivants partagent un
/// personnage : ni le portrait ni le sprite ne les distinguent, la couleur et le nom si.
///
/// La fiche sert aux deux ecrans, comme avant : dans la preparation elle se clique pour enroler, en
/// mission pour changer de point de vue. C'est le meme geste sur le meme dessin.
/// </summary>
public partial class AgentCard : PanelContainer
{
    public const int CardWidth = 130;

    /// <summary>Cote du portrait, en pixels. C'est la taille native de l'image : un entier, ou rien.</summary>
    private const int PortraitSize = 32;

    private static readonly Color HealthColor = new(0.44f, 0.75f, 0.35f);
    private static readonly Color PanicColor = new(0.24f, 0.35f, 0.42f);
    private static readonly Color TrackColor = new(0.16f, 0.14f, 0.12f);

    /// <summary>
    /// Les cinq etats du livrable 12, dans l'ordre d'affichage. La liste n'est pas decorative : chaque
    /// entree a une image livree dans <c>Assets/UI/</c> et un predicat ci-dessous. En ajouter un
    /// demande les deux.
    /// </summary>
    private static readonly string[] States =
        ["wounded", "panicked", "overloaded", "armed", "radio"];

    private readonly Dictionary<string, TextureRect> _icons = [];

    private TextureRect _portrait = null!;
    private Panel _ring = null!;
    private Label _name = null!;
    private ProgressBar _healthBar = null!;
    private ProgressBar _panicBar = null!;
    private Label _state = null!;

    private bool _selected;
    private bool _dimmed;
    private Color _ringColor = new(0.31f, 0.78f, 1f);

    /// <summary>Le profil affiche. Dans l'ecran de preparation, il n'y a pas encore de survivant.</summary>
    public SurvivorProfile? Profile { get; private set; }

    /// <summary>Emis au clic. L'ecran qui la contient decide de ce que cela veut dire.</summary>
    public event Action<AgentCard>? Clicked;

    /// <summary>Emis au double-clic : centrer la camera sur ce survivant.</summary>
    public event Action<AgentCard>? DoubleClicked;

    /// <summary>
    /// La couleur d'anneau du survivant sur la carte. C'est elle, et non le portrait, qui identifie :
    /// deux survivants peuvent partager un personnage, jamais une couleur.
    /// </summary>
    public Color RingColor
    {
        get => _ringColor;
        set
        {
            _ringColor = value;
            RestyleRing();
        }
    }

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

    /// <summary>Hors jeu — hors de combat. La fiche s'eteint sans disparaitre.</summary>
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

    public AgentCard()
    {
        CustomMinimumSize = new Vector2(CardWidth, 0);
        MouseFilter = MouseFilterEnum.Stop;

        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 3);

        AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 2);
        margin.AddChild(column);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 4);
        column.AddChild(top);

        top.AddChild(BuildPortrait());

        var details = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        details.AddThemeConstantOverride("separation", 2);
        top.AddChild(details);

        _name = Caption(details, 10, new Color(0.18f, 0.13f, 0.08f));
        _name.ClipText = true;

        _healthBar = Gauge(details, HealthColor);
        _panicBar = Gauge(details, PanicColor);

        _state = Caption(details, 8, new Color(0.55f, 0.32f, 0.12f));
        _state.Visible = false;

        // Un objectif porte un nom long (« ExploreUnknownArea ») : il se coupe plutot que d'elargir la
        // vignette, dont la largeur doit rester la meme pour tout le monde.
        _state.ClipText = true;

        column.AddChild(BuildStatusRow());

        Restyle();
    }

    /// <summary>
    /// Le portrait dans son anneau. L'anneau est un <see cref="Panel"/> derriere l'image plutot qu'un
    /// contour dessine dessus : il reste visible quel que soit le portrait, et sa couleur se change
    /// sans toucher a la texture.
    /// </summary>
    private Control BuildPortrait()
    {
        _ring = new Panel
        {
            CustomMinimumSize = new Vector2(PortraitSize + 3, PortraitSize + 3),
            MouseFilter = MouseFilterEnum.Ignore,
        };

        _portrait = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };

        _portrait.SetAnchorsPreset(LayoutPreset.FullRect);
        _portrait.OffsetLeft = 1.5f;
        _portrait.OffsetTop = 1.5f;
        _portrait.OffsetRight = -1.5f;
        _portrait.OffsetBottom = -1.5f;

        _ring.AddChild(_portrait);
        RestyleRing();

        return _ring;
    }

    private Control BuildStatusRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 2);

        foreach (string state in States)
        {
            var icon = new TextureRect
            {
                Texture = UiSkin.Status(state),
                CustomMinimumSize = new Vector2(12, 12),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
                Visible = false,
            };

            row.AddChild(icon);
            _icons[state] = icon;
        }

        return row;
    }

    // --- Contenu ---------------------------------------------------------------------------------

    /// <summary>
    /// Une fiche d'avant-mission : le profil, et les jauges pleines qu'il aura au depart. La ligne de
    /// base vient de <c>config/game_config.json</c> — c'est la meme pour tout le monde, un survivant
    /// n'ayant plus de caracteristiques a lui.
    /// </summary>
    public void Show(SurvivorProfile profile, SurvivorSettings baseline, int rank = 0)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(baseline);

        Profile = profile;

        _portrait.Texture = UiSkin.Portrait(profile.Character ?? UiSkin.CharacterAt(rank));

        _name.Text = profile.Name.ToUpperInvariant();

        Write(_healthBar, baseline.MaxHealth, baseline.MaxHealth);
        Write(_panicBar, 0f, 1f);

        TooltipText = string.Empty;
        _state.Visible = false;

        foreach (var icon in _icons.Values) icon.Visible = false;
    }

    /// <summary>Une fiche en mission : les jauges suivent le survivant, seconde apres seconde.</summary>
    public void Refresh(Survivor survivor, int rank = 0)
    {
        ArgumentNullException.ThrowIfNull(survivor);

        Profile = survivor.Profile;

        _portrait.Texture = UiSkin.Portrait(survivor.Profile.Character ?? UiSkin.CharacterAt(rank));

        _name.Text = survivor.Name.ToUpperInvariant();

        Write(_healthBar, survivor.Health, survivor.MaxHealth);
        Write(_panicBar, survivor.Panic, 1f);

        _state.Text = survivor switch
        {
            { Alive: false } => "hors de combat",
            _ => survivor.CurrentGoal ?? string.Empty,
        };

        _state.Visible = _state.Text.Length > 0;

        RefreshStates(survivor);

        Dimmed = !survivor.Alive;
    }

    /// <summary>
    /// Les cinq etats, chacun lu dans le survivant plutot que decide ici. « Blesse » est un seuil de
    /// sante et non un drapeau : c'est la meme grandeur que la jauge juste au-dessus, et l'icone ne
    /// fait que la rendre reperable de loin.
    /// </summary>
    private void RefreshStates(Survivor survivor)
    {
        bool alive = survivor.Alive;

        Set("wounded", alive && survivor.HealthRatio < 0.5f);
        Set("panicked", alive && survivor.Panic > 0.5f);
        Set("overloaded", alive && survivor.Inventory.IsOverweight);
        Set("armed", alive && survivor.Inventory.EquippedWeapon is not null);
        Set("radio", alive && survivor.Inventory.EquippedTools.Contains("radio"));

        void Set(string state, bool on)
        {
            if (_icons.TryGetValue(state, out var icon)) icon.Visible = on;
        }
    }

    // --- Mise en forme ---------------------------------------------------------------------------

    private static Label Caption(Container parent, int size, Color color)
    {
        var label = new Label();
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        parent.AddChild(label);
        return label;
    }

    /// <summary>
    /// Une jauge courte, sans son chiffre : la grille du bas se lit d'un coup d'oeil, et c'est le
    /// panneau lateral qui donne les nombres.
    /// </summary>
    private static ProgressBar Gauge(Container parent, Color color)
    {
        var bar = new ProgressBar
        {
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 4),
        };

        bar.AddThemeStyleboxOverride("background", Bar(TrackColor));
        bar.AddThemeStyleboxOverride("fill", Bar(color));

        parent.AddChild(bar);
        return bar;
    }

    private static StyleBoxFlat Bar(Color color)
    {
        var box = new StyleBoxFlat { BgColor = color };
        box.SetCornerRadiusAll(1);
        return box;
    }

    private static void Write(ProgressBar bar, float value, float maximum)
    {
        bar.MaxValue = Math.Max(0.0001d, maximum);
        bar.Value = Math.Clamp(value, 0f, maximum);
    }

    private void RestyleRing()
    {
        var box = new StyleBoxFlat { BgColor = _ringColor };
        box.SetCornerRadiusAll(2);

        _ring.AddThemeStyleboxOverride("panel", box);
    }

    /// <summary>
    /// Le fond de la fiche : la plaque de metal du livrable 12, ou un aplat quand elle n'a pas ete
    /// livree. Selectionnee, la fiche s'eclaircit — la couleur de son anneau, elle, ne bouge pas :
    /// c'est une identite, pas un etat.
    /// </summary>
    private void Restyle()
    {
        if (UiSkin.FrameSmall() is StyleBoxTexture frame)
        {
            var box = (StyleBoxTexture)frame.Duplicate();

            // La texture garde ses bords 9-slice, mais ses marges de contenu suivent la nouvelle
            // echelle. Les 31 px du cadre original annuleraient sinon la reduction de la fiche.
            box.ContentMarginLeft = 3f;
            box.ContentMarginRight = 3f;
            box.ContentMarginTop = 3f;
            box.ContentMarginBottom = 3f;

            // La fiche selectionnee garde sa couleur pleine ; les autres reculent d'un cran. C'est
            // l'inverse d'un lisere ajoute : le point de vue courant est ce qui est <b>normal</b>, et
            // le reste de l'equipe ce qui attend — la barre entiere reste lisible dans les deux cas.
            box.ModulateColor = _selected ? Colors.White : new Color(0.74f, 0.72f, 0.7f);
            AddThemeStyleboxOverride("panel", box);
        }
        else
        {
            var box = new StyleBoxFlat
            {
                BgColor = _selected ? new Color(0.31f, 0.27f, 0.22f) : new Color(0.18f, 0.16f, 0.14f),
                BorderColor = _selected ? new Color(0.85f, 0.72f, 0.45f) : new Color(0.32f, 0.28f, 0.24f),
            };

            box.SetCornerRadiusAll(2);
            box.SetBorderWidthAll(_selected ? 2 : 1);
            AddThemeStyleboxOverride("panel", box);
        }

        Modulate = new Color(1f, 1f, 1f, _dimmed ? 0.45f : 1f);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click) return;

        if (click.DoubleClick) DoubleClicked?.Invoke(this);
        else Clicked?.Invoke(this);

        AcceptEvent();
    }
}

using Godot;
using Hordinary.Agents;
using Hordinary.Game;

namespace Hordinary.UI;

/// <summary>
/// L'ecran de camp (livrable 12) : ce qu'il reste, qui est la, et ou on les envoie.
///
/// C'est l'ecran ou le joueur decide reellement. Pendant la mission, les survivants se debrouillent —
/// le joueur regarde. Ici il choisit ce qu'il regardera : la mission, et qui part la jouer.
///
/// <code>
/// +------------------------------------------------------------------+
/// |  CAMP — jour 3                                                    |
/// |  RESERVES   [#] 12 nourriture  [#] 10 eau  [#] 40 essence  ...    |
/// |                                                                   |
/// |  EQUIPE                        |  MISSION                         |
/// |  [ALICE] [BOB] [CLAIRE]        |  > Ravitaillement pharmacie      |
/// |                                |    Fouille de l'entrepot ouest   |
/// |                                |  EXPEDITION                      |
/// |                                |    Supermarche (1 j, danger 30%) |
/// |                                |                                  |
/// |  « La pharmacie est a deux rues du camp. »   [ PARTIR ]           |
/// +------------------------------------------------------------------+
/// </code>
///
/// L'ecran ne connait ni carte ni horde : il rend un choix, et <c>GameRoot</c> en fait une partie ou
/// un recit. Les seuils qui disent qu'une reserve est basse viennent de <c>config/camp.json</c>, comme
/// partout ailleurs — un ecran qui les redeciderait ferait exister deux verites pour la meme chose.
/// </summary>
public partial class CampScreen : CanvasLayer
{
    private const int IconSize = 32;

    private static readonly Color Ink = new(0.93f, 0.9f, 0.85f);
    private static readonly Color Heading = new(0.92f, 0.72f, 0.4f);
    private static readonly Color Faint = new(0.68f, 0.63f, 0.56f);

    private readonly List<AgentCard> _cards = [];
    private readonly HashSet<string> _enlisted = new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<SurvivorProfile> _profiles = [];
    private IReadOnlyList<MissionDefinition> _missions = [];

    private SurvivorSettings _baseline = SurvivorSettings.Default;
    private CampStock _camp = new();
    private ItemIcons? _icons;

    private HBoxContainer _reserves = null!;
    private GridContainer _grid = null!;
    private VBoxContainer _missionList = null!;
    private Label _title = null!;
    private Label _briefing = null!;
    private Label _teamCount = null!;
    private Button _go = null!;

    /// <summary>La graine du quartier : montree, et saisissable. Cachee pour la carte dessinee.</summary>
    private HBoxContainer _seedRow = null!;
    private LineEdit _seed = null!;
    private int _defaultSeed;

    /// <summary>Ce que le joueur a retenu : la mission a jouer.</summary>
    private int _mission;

    /// <summary>Emis quand le joueur part en mission : la mission, et l'equipe dans l'ordre du roster.</summary>
    public event Action<MissionDefinition, IReadOnlyList<SurvivorProfile>>? MissionChosen;

    public void Configure(
        IReadOnlyList<SurvivorProfile> profiles,
        IReadOnlyList<MissionDefinition> missions,
        SurvivorSettings baseline,
        CampStock camp,
        ItemIcons? icons = null,
        int defaultSeed = 0)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(missions);
        ArgumentNullException.ThrowIfNull(camp);

        _profiles = profiles;
        _missions = missions;
        _baseline = baseline ?? SurvivorSettings.Default;
        _camp = camp;
        _icons = icons;
        _defaultSeed = defaultSeed;

        Layer = 20;
        Build();
    }

    // --- Construction ------------------------------------------------------------------------------

    private void Build()
    {
        BuildBackdrop();

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 24);

        AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        margin.AddChild(column);

        _title = Text(column, 22, Ink);

        BuildReserves(column);

        var middle = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        middle.AddThemeConstantOverride("separation", 20);
        column.AddChild(middle);

        BuildDestinations(middle);
        BuildTeam(middle);

        BuildFooter(column);

        BuildMissionButtons();
        BuildCards();

        Refresh();
    }

    /// <summary>
    /// Le camp dessine, et un voile sombre par-dessus.
    ///
    /// C'est le camp qu'on voit derriere l'ecran de camp, et non la rue ou la mission se jouera : le
    /// joueur est <b>chez lui</b> quand il compose son equipe, et la carte n'a rien a faire la. Le
    /// voile n'est pas une coquetterie — sans lui, un titre clair passe sur une toile de tente claire
    /// et disparait. Sans l'image, il reste l'aplat opaque : l'ecran ne depend pas du dessin.
    /// </summary>
    private void BuildBackdrop()
    {
        if (UiSkin.Texture("ui_title_background.png") is { } camp)
        {
            var scene = new TextureRect
            {
                Texture = camp,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                MouseFilter = Control.MouseFilterEnum.Stop,
            };

            scene.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddChild(scene);
        }

        var veil = new ColorRect
        {
            Color = UiSkin.Texture("ui_title_background.png") is null
                ? new Color(0.09f, 0.08f, 0.07f, 0.96f)
                : new Color(0.04f, 0.035f, 0.03f, 0.62f),

            MouseFilter = Control.MouseFilterEnum.Stop,
        };

        veil.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(veil);
    }

    /// <summary>
    /// Les quatre reserves. La couleur dit l'etat, le chiffre dit de combien : on voit d'abord ce qui
    /// manque, on lit ensuite. La vignette est la tuile de l'objet qui remplit la reserve — la conserve
    /// ramassee au sol est celle qu'on compte avant de partir.
    /// </summary>
    private void BuildReserves(Container parent)
    {
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 18);
        parent.AddChild(line);

        var label = Text(line, 13, Heading);
        label.Text = "RESERVES";
        label.CustomMinimumSize = new Vector2(110, 0);

        _reserves = new HBoxContainer();
        _reserves.AddThemeConstantOverride("separation", 18);
        line.AddChild(_reserves);

        foreach (string resource in CampStock.Resources)
        {
            var reserve = new HBoxContainer { Name = resource };
            reserve.AddThemeConstantOverride("separation", 5);
            _reserves.AddChild(reserve);

            if (_icons?.SlotForResource(resource, IconSize) is { } icon) reserve.AddChild(icon);

            var amount = Text(reserve, 14, Ink);
            amount.Name = "Amount";
        }
    }

    private void BuildDestinations(Container parent)
    {
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
        column.AddThemeConstantOverride("separation", 4);
        parent.AddChild(column);

        var missions = Text(column, 13, Heading);
        missions.Text = "MISSION — jouee sur la carte";

        _missionList = new VBoxContainer();
        _missionList.AddThemeConstantOverride("separation", 2);
        column.AddChild(_missionList);

        BuildSeed(column);
    }

    /// <summary>
    /// La graine du quartier (livrable 13). Elle n'apparait que pour une mission qui en genere un :
    /// la carte dessinee est toujours la meme, et lui proposer une graine serait un mensonge.
    ///
    /// Elle est montree parce qu'un quartier interessant doit pouvoir se retrouver, et saisissable
    /// parce qu'un quartier casse doit pouvoir se remontrer a l'identique. Le champ accepte un mot
    /// autant qu'un nombre : ce qui n'est pas un entier est resume en un — un joueur qui tape le nom
    /// de sa rue obtient un quartier stable, sans avoir a penser en nombres.
    /// </summary>
    private void BuildSeed(Container parent)
    {
        _seedRow = new HBoxContainer();
        _seedRow.AddThemeConstantOverride("separation", 8);
        parent.AddChild(_seedRow);

        var label = Text(_seedRow, 12, Faint);
        label.Text = "GRAINE";
        label.CustomMinimumSize = new Vector2(60, 26);
        label.VerticalAlignment = VerticalAlignment.Center;

        _seed = new LineEdit
        {
            PlaceholderText = "au hasard",
            CustomMinimumSize = new Vector2(160, 26),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };

        _seed.AddThemeFontSizeOverride("font_size", 12);
        _seedRow.AddChild(_seed);

        var reroll = new Button { Text = "AUTRE QUARTIER", CustomMinimumSize = new Vector2(150, 26) };
        reroll.AddThemeFontSizeOverride("font_size", 11);
        UiSkin.DressRow(reroll);

        // Le tirage n'a pas a etre reproductible : c'est celui qui remplit le champ, et c'est le
        // champ, lui, qui l'est.
        reroll.Pressed += () => _seed.Text = Random.Shared.Next(1, 1_000_000).ToString();
        _seedRow.AddChild(reroll);
    }

    /// <summary>
    /// La graine a employer pour le quartier. Vide, c'est celle de la partie ; un entier est pris tel
    /// quel ; tout le reste est resume par un hachage ecrit ici plutot qu'emprunte a
    /// <c>string.GetHashCode</c>, qui change d'une execution a l'autre et rendrait « la meme graine »
    /// faux des la fermeture du jeu.
    /// </summary>
    public int MapSeed
    {
        get
        {
            string text = _seed?.Text.Trim() ?? string.Empty;
            if (text.Length == 0) return _defaultSeed;
            if (int.TryParse(text, out int number)) return number;

            int hash = 17;
            foreach (char letter in text) hash = unchecked((hash * 31) + char.ToLowerInvariant(letter));

            return hash & 0x7fffffff;
        }
    }

    private void BuildTeam(Container parent)
    {
        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            ClipContents = true,
        };

        column.AddThemeConstantOverride("separation", 4);
        parent.AddChild(column);

        _teamCount = Text(column, 13, Heading);

        // Deux fiches par rangee : trois cote a cote depassaient du bord droit et poussaient le bouton
        // de depart hors de l'ecran. Le roster grandira, la grille aussi.
        _grid = new GridContainer { Columns = 2 };
        _grid.AddThemeConstantOverride("h_separation", 10);
        _grid.AddThemeConstantOverride("v_separation", 8);
        column.AddChild(_grid);
    }

    private void BuildFooter(Container parent)
    {
        _briefing = Text(parent, 12, Faint);
        _briefing.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _briefing.CustomMinimumSize = new Vector2(0, 42);

        var row = new HBoxContainer();
        parent.AddChild(row);

        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        _go = new Button { Text = "PARTIR", CustomMinimumSize = new Vector2(200, 44) };
        _go.AddThemeFontSizeOverride("font_size", 15);
        UiSkin.Dress(_go);

        _go.Pressed += Confirm;
        row.AddChild(_go);
    }

    private void BuildMissionButtons()
    {
        for (int index = 0; index < _missions.Count; index++)
        {
            int choice = index;
            var mission = _missions[index];

            var button = Choice($"{mission.Name}     {mission.Difficulty ?? "—"}     equipe de {mission.RecommendedTeam}");

            button.Pressed += () =>
            {
                _mission = choice;

                // Changer de mission remet la graine a celle que le fichier annonce, s'il en annonce
                // une : la graine appartient au quartier, pas a l'ecran.
                _seed.Text = mission.MapSeed?.ToString() ?? string.Empty;

                Refresh();
            };

            _missionList.AddChild(button);
        }
    }

    private static Button Choice(string text)
    {
        var button = new Button
        {
            Text = text,
            Alignment = HorizontalAlignment.Left,
            ToggleMode = true,
            CustomMinimumSize = new Vector2(0, 30),
        };

        button.AddThemeFontSizeOverride("font_size", 12);
        UiSkin.DressRow(button);

        return button;
    }

    private void BuildCards()
    {
        for (int rank = 0; rank < _profiles.Count; rank++)
        {
            var card = new AgentCard();
            card.Show(_profiles[rank], _baseline, rank);
            card.Clicked += Toggle;

            _grid.AddChild(card);
            _cards.Add(card);
        }

        foreach (var profile in _profiles.Take(Math.Max(1, _missions.FirstOrDefault()?.RecommendedTeam ?? 1)))
            _enlisted.Add(profile.Id);
    }

    /// <summary>La couleur d'anneau de chaque survivant, dans l'ordre du roster — celle de la carte.</summary>
    public void Paint(IReadOnlyList<Color> ringColors)
    {
        ArgumentNullException.ThrowIfNull(ringColors);
        if (ringColors.Count == 0) return;

        for (int index = 0; index < _cards.Count; index++)
            _cards[index].RingColor = ringColors[index % ringColors.Count];
    }

    // --- Choix -------------------------------------------------------------------------------------

    private void Toggle(AgentCard card)
    {
        if (card.Profile is not { } profile) return;

        if (!_enlisted.Remove(profile.Id)) _enlisted.Add(profile.Id);

        Refresh();
    }

    /// <summary>Remet l'ecran a jour — d'une sortie a l'autre, les reserves ont bouge.</summary>
    public void Refresh()
    {
        _title.Text = _camp.Day == 0 ? "CAMP — avant la premiere sortie" : $"CAMP — jour {_camp.Day}";

        RefreshReserves();

        for (int index = 0; index < _missionList.GetChildCount(); index++)
        {
            if (_missionList.GetChild(index) is Button button) button.ButtonPressed = index == _mission;
        }

        foreach (var card in _cards)
            card.Selected = card.Profile is { } profile && _enlisted.Contains(profile.Id);

        int count = _enlisted.Count;
        _teamCount.Text = count switch
        {
            0 => "EQUIPE — personne d'enrole",
            1 => "EQUIPE — 1 enrole",
            _ => $"EQUIPE — {count} enroles",
        };

        // La graine ne concerne que les quartiers generes : la carte dessinee n'en a pas.
        _seedRow.Visible = SelectedMission is { IsGenerated: true };

        _briefing.Text = SelectedMission?.Description ?? "Aucune mission disponible.";
        _go.Text = "PARTIR EN MISSION";
        _go.Disabled = count == 0 || SelectedMission is null;
    }

    private void RefreshReserves()
    {
        foreach (string resource in CampStock.Resources)
        {
            if (_reserves.GetNodeOrNull<HBoxContainer>(resource) is not { } reserve) continue;
            if (reserve.GetNodeOrNull<Label>("Amount") is not { } amount) continue;

            amount.Text = $"{_camp.Of(resource):0.#} {Named(resource)}";
            amount.AddThemeColorOverride("font_color", Tint(_camp.LevelOf(resource)));
        }

        static string Named(string resource) => resource switch
        {
            "food" => "nourriture",
            "water" => "eau",
            "fuel" => "essence",
            "ammo" => "munitions",
            _ => resource,
        };

        static Color Tint(StockLevel level) => level switch
        {
            StockLevel.Full => new Color(0.62f, 0.88f, 0.55f),
            StockLevel.Low => new Color(0.95f, 0.78f, 0.36f),
            _ => new Color(0.9f, 0.4f, 0.36f),
        };
    }

    private MissionDefinition? SelectedMission
        => _missions.Count == 0 ? null : _missions[Math.Clamp(_mission, 0, _missions.Count - 1)];

    private void Confirm()
    {
        var team = _profiles.Where(profile => _enlisted.Contains(profile.Id)).ToList();
        if (team.Count == 0) return;

        if (SelectedMission is not { } mission) return;

        Visible = false;
        MissionChosen?.Invoke(mission, team);
    }

    private static Label Text(Container parent, int size, Color color)
    {
        var label = new Label();
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        parent.AddChild(label);
        return label;
    }
}

using Godot;
using Hordinary.AI.Instructions;
using Hordinary.AI.Planning.Pddl;
using Hordinary.Agents;
using Hordinary.Game;
using Hordinary.World;

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
/// |  MISSION                       |  EQUIPE                          |
/// |  > Ravitaillement pharmacie    |  [ALICE] [BOB] [CLAIRE]          |
/// |    Fouille de l'entrepot ouest |  PAQUETAGE                       |
/// |  GRAINE [______] [AUTRE]       |   ALICE  emporte [Trousse x1 X]  |
/// |                                |          donner  [Bandage (3)]   |
/// |                                |                                  |
/// |  « La pharmacie est a deux rues du camp. »   [ PARTIR ]           |
/// +------------------------------------------------------------------+
/// </code>
///
/// Il se rouvre apres chaque mission, augmente du verdict de la sortie precedente : une mission
/// finie ramene au camp, elle ne fige pas la partie sur une carte ou plus rien ne bouge.
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

    /// <summary>
    /// Les vignettes de l'etape « equipe », dans l'ordre du roster. Ce sont les memes que celles de
    /// la barre du bas pendant la mission : le portrait et les jauges d'un survivant se dessinent au
    /// meme endroit, qu'on soit en train de le choisir ou de le regarder courir.
    /// </summary>
    private readonly List<AgentCard> _survivorCards = [];

    /// <summary>Les points de vie chiffres sous chaque vignette, dans le meme ordre.</summary>
    private readonly List<Label> _survivorHealth = [];

    /// <summary>« Enrole » ou « reste au camp », sous les points de vie, dans le meme ordre.</summary>
    private readonly List<Label> _survivorStates = [];

    private readonly List<Button> _stepChoices = [];
    private readonly HashSet<string> _enlisted = new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<SurvivorProfile> _profiles = [];
    private IReadOnlyList<MissionDefinition> _missions = [];

    private SurvivorSettings _baseline = SurvivorSettings.Default;
    private CampStock _camp = new();
    private ItemIcons? _icons;

    /// <summary>Les limites du sac, pour que le paquetage se compose sous les regles du jeu.</summary>
    private InventorySettings _inventory = InventorySettings.Default;

    /// <summary>Ce que le joueur confie a chacun. Rien n'est debite tant que le van n'est pas parti.</summary>
    private CampLoadout _loadout = null!;

    /// <summary>Ce que la sortie precedente a donne, ou <c>null</c> avant la premiere.</summary>
    private string? _debrief;

    /// <summary>Le catalogue, pour dire « Fusil a pompe » la ou l'offre ecrit <c>shotgun</c>.</summary>
    private ItemCatalogue _items = ItemCatalogue.Empty;

    /// <summary>Le domaine exporte (spec G), vocabulaire des instructions tactiques du livrable 15.</summary>
    private PddlDomain? _domain;
    private InstructionPresetCatalogue? _presets;
    private InstructionSettings? _instructionSettings;

    private HBoxContainer _reserves = null!;

    /// <summary>Les recompenses que l'entrepot n'a pas pu prendre. Cachee quand la file est vide.</summary>
    private HBoxContainer _pendingRow = null!;
    private Label _pending = null!;

    private LoadoutPanel _packs = null!;
    private InstructionPanel? _instructions;
    private Hordinary.Instructions.InstructionClient? _instructionClient;
    private VBoxContainer _missionList = null!;
    private Label _title = null!;
    private Label _steps = null!;
    private ScrollContainer _stepScroll = null!;
    private VBoxContainer _missionStep = null!;
    private VBoxContainer _teamStep = null!;
    private VBoxContainer _loadoutStep = null!;

    /// <summary>Le verdict de la sortie precedente. Cachee au premier jour, qui n'en a pas.</summary>
    private Label _lastRun = null!;
    private Label _briefing = null!;
    private Label _teamCount = null!;
    private Button _go = null!;
    private Button _back = null!;
    private Button _next = null!;

    /// <summary>La graine du quartier : montree, et saisissable. Cachee pour la carte dessinee.</summary>
    private HBoxContainer _seedRow = null!;
    private LineEdit _seed = null!;
    private int _defaultSeed;

    /// <summary>Ce que le joueur a retenu : la mission a jouer.</summary>
    private int _mission;
    private int _step;

    /// <summary>
    /// Emis quand le joueur part en mission : la mission, l'equipe dans l'ordre du roster, le
    /// paquetage et les instructions tactiques retenues. Les trois partent ensemble parce qu'ils se decident ensemble — envoyer l'equipe
    /// sans ce qu'elle emporte obligerait <c>GameRoot</c> a aller le rechercher, et ferait de
    /// l'ecran un etat consultable plutot qu'un choix rendu.
    /// </summary>
    public event Action<MissionDefinition, IReadOnlyList<SurvivorProfile>, CampLoadout, IReadOnlyList<string>>? MissionChosen;

    public void Configure(
        IReadOnlyList<SurvivorProfile> profiles,
        IReadOnlyList<MissionDefinition> missions,
        SurvivorSettings baseline,
        CampStock camp,
        ItemIcons? icons = null,
        int defaultSeed = 0,
        ItemCatalogue? items = null,
        InventorySettings? inventory = null,
        string? debrief = null,
        PddlDomain? domain = null,
        InstructionPresetCatalogue? presets = null,
        InstructionSettings? instructions = null,
        Hordinary.Instructions.InstructionClient? client = null)
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
        _items = items ?? ItemCatalogue.Empty;
        _inventory = inventory ?? InventorySettings.Default;
        _debrief = debrief;

        // Le domaine PDDL est le vocabulaire de reference du portillon 1 : c'est lui, et pas une
        // liste tenue a part, qui dit quels predicats et quels schemas une instruction peut nommer.
        _domain = domain;
        _presets = presets;
        _instructionSettings = instructions;
        _instructionClient = client;

        _loadout = new CampLoadout(_camp, _items, _inventory);

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

        // Le retour de la sortie precedente, la ou le joueur revient : le jeu n'a pas d'ecran de
        // resultat separe, et n'en gagne pas un ici. Le verdict s'affiche sur l'ecran qui suit la
        // mission, au-dessus des reserves qu'il vient de changer.
        _lastRun = Text(column, 13, Heading);
        _lastRun.AutowrapMode = TextServer.AutowrapMode.WordSmart;

        BuildReserves(column);
        BuildPending(column);

        BuildStepHeader(column);

        _stepScroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        column.AddChild(_stepScroll);

        var pages = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        pages.AddThemeConstantOverride("separation", 10);
        _stepScroll.AddChild(pages);

        _missionStep = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _teamStep = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _loadoutStep = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        pages.AddChild(_missionStep);
        pages.AddChild(_teamStep);
        pages.AddChild(_loadoutStep);

        BuildDestinations(_missionStep);
        BuildTeam(_teamStep);
        BuildLoadout(_loadoutStep);

        BuildFooter(column);

        BuildMissionButtons();
        BuildCards();

        ShowStep(0);
        Refresh();
    }

    private void BuildStepHeader(Container parent)
    {
        _steps = Text(parent, 13, Heading);
        _steps.Text = "1  MISSION     2  EQUIPE     3  PAQUETAGE & INSTRUCTIONS";

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        parent.AddChild(row);

        foreach (string label in new[] { "1  Mission", "2  Survivants", "3  Preparation" })
        {
            int page = _stepChoices.Count;
            var button = new Button { Text = label, ToggleMode = true, CustomMinimumSize = new Vector2(150, 30) };
            button.AddThemeFontSizeOverride("font_size", 11);
            UiSkin.DressRow(button);
            button.Pressed += () => ShowStep(page);
            row.AddChild(button);
            _stepChoices.Add(button);
        }
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

    /// <summary>
    /// La file des recompenses que l'entrepot n'a pas pu prendre (livrable 16). Elle n'apparait que
    /// lorsqu'il y a quelque chose dedans, et le bouton range ce qui rentre.
    ///
    /// C'est le seul endroit du jeu ou une recompense promise peut encore attendre — et elle attend
    /// visiblement, plutot que de disparaitre : le joueur a choisi son expedition sur ce qu'elle
    /// annoncait.
    /// </summary>
    private void BuildPending(Container parent)
    {
        _pendingRow = new HBoxContainer();
        _pendingRow.AddThemeConstantOverride("separation", 12);
        parent.AddChild(_pendingRow);

        _pending = Text(_pendingRow, 12, Heading);
        _pending.VerticalAlignment = VerticalAlignment.Center;

        var claim = new Button { Text = "RECUPERER", CustomMinimumSize = new Vector2(130, 24) };
        claim.AddThemeFontSizeOverride("font_size", 11);
        UiSkin.DressRow(claim);

        claim.Pressed += () =>
        {
            var taken = _camp.ClaimPending();
            if (taken.Count > 0) Refresh();
        };

        _pendingRow.AddChild(claim);
    }

    private void BuildDestinations(Container parent)
    {
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 4);
        parent.AddChild(column);

        var missions = Text(column, 13, Heading);
        missions.Text = "MISSION — jouee sur la carte";

        _missionList = new VBoxContainer();
        _missionList.AddThemeConstantOverride("separation", 2);
        column.AddChild(_missionList);

        BuildSeed(column);
    }

    private void BuildLoadout(Container parent)
    {
        var heading = Text(parent, 13, Heading);
        heading.Text = "3  PREPARATION — paquetage et consignes";

        var hint = Text(parent, 12, Faint);
        hint.Text = "Ajustez le sac de chaque survivant, puis ajoutez une consigne pour toute l'equipe (optionnel).";
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;

        _packs = new LoadoutPanel { Name = "Loadout" };
        _packs.Configure(_loadout, _items, _inventory, _icons);
        _packs.Changed += Refresh;
        parent.AddChild(_packs);

        // Sans domaine exporte, il n'y a pas de vocabulaire a valider : le panneau ne s'affiche pas
        // plutot que de proposer une saisie que rien ne pourrait accepter.
        if (_domain is null) return;

        _instructions = new InstructionPanel { Name = "TacticalInstructions" };
        _instructions.Configure(_domain, _presets, _instructionSettings, _instructionClient, () => MapSeed);
        _instructions.Changed += Refresh;
        parent.AddChild(_instructions);
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
        _teamCount = Text(parent, 13, Heading);

        var hint = Text(parent, 12, Faint);
        hint.Text = "Selectionnez les personnes qui partent. La recommandation de la mission est preselectionnee.";
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;

        // La meme vignette qu'en mission : portrait, anneau de couleur et jauges. Composer une equipe
        // et suivre une sortie se regardent sur le meme dessin — une ligne de texte ici obligerait a
        // reconnaitre deux fois la meme personne, une fois par son nom au camp et une fois par son
        // portrait sur la carte.
        var grid = new HBoxContainer();
        grid.AddThemeConstantOverride("separation", 8);
        parent.AddChild(grid);

        for (int rank = 0; rank < _profiles.Count; rank++)
        {
            var profile = _profiles[rank];

            var entry = new VBoxContainer { Name = $"Survivor_{profile.Id}" };
            entry.AddThemeConstantOverride("separation", 2);
            grid.AddChild(entry);

            var card = new AgentCard();
            card.Show(profile, _baseline, rank);
            card.Clicked += _ => Toggle(profile.Id);
            entry.AddChild(card);

            // Les points de vie en clair sous la jauge. Au camp on compare des gens avant d'en choisir
            // un, et deux barres presque pleines ne se comparent pas a l'oeil ; en mission la vignette
            // garde sa jauge seule, parce qu'on y surveille une tendance et non un nombre.
            var health = Text(entry, 11, Ink);
            health.HorizontalAlignment = HorizontalAlignment.Center;

            var state = Text(entry, 10, Faint);
            state.HorizontalAlignment = HorizontalAlignment.Center;

            _survivorCards.Add(card);
            _survivorHealth.Add(health);
            _survivorStates.Add(state);
        }
    }

    private void BuildFooter(Container parent)
    {
        _briefing = Text(parent, 12, Faint);
        _briefing.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _briefing.CustomMinimumSize = new Vector2(0, 42);

        var row = new HBoxContainer();
        parent.AddChild(row);

        _back = new Button { Text = "RETOUR", CustomMinimumSize = new Vector2(130, 42) };
        _back.AddThemeFontSizeOverride("font_size", 13);
        UiSkin.DressRow(_back);
        _back.Pressed += () => ShowStep(_step - 1);
        row.AddChild(_back);

        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        _next = new Button { Text = "SUIVANT", CustomMinimumSize = new Vector2(160, 42) };
        _next.AddThemeFontSizeOverride("font_size", 13);
        UiSkin.DressRow(_next);
        _next.Pressed += () => ShowStep(_step + 1);
        row.AddChild(_next);

        _go = new Button { Text = "PARTIR", CustomMinimumSize = new Vector2(200, 44), Visible = false };
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

            var button = Choice(Headline(mission));

            button.Pressed += () =>
            {
                _mission = choice;
                SetDefaultTeam(mission);

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
        SetDefaultTeam(SelectedMission);
    }

    /// <summary>La couleur d'anneau de chaque survivant, dans l'ordre du roster — celle de la carte.</summary>
    public void Paint(IReadOnlyList<Color> ringColors)
    {
        ArgumentNullException.ThrowIfNull(ringColors);
        if (ringColors.Count == 0) return;

        // Le meme tableau que celui qui teinte l'anneau sous les pieds sur la carte. C'est ce qui fait
        // qu'une bordure de vignette au camp et un rond sur le terrain se lisent comme le meme signe —
        // deux survivants peuvent partager un portrait, jamais une couleur.
        for (int rank = 0; rank < _survivorCards.Count; rank++)
            _survivorCards[rank].RingColor = ringColors[rank % ringColors.Count];
    }

    // --- Choix -------------------------------------------------------------------------------------

    private void Toggle(string survivorId)
    {
        if (_enlisted.Remove(survivorId))
        {
            // Qui ne part pas rend ce qu'on lui avait confie. Sans cela, un survivant retire de
            // l'equipe garderait l'unique trousse du camp en otage : promise a quelqu'un qui reste,
            // et introuvable pour ceux qui partent.
            _loadout.Clear(survivorId);
        }
        else
        {
            _enlisted.Add(survivorId);
        }

        Refresh();
    }

    private void SetDefaultTeam(MissionDefinition? mission)
    {
        var previous = _enlisted.ToList();
        _enlisted.Clear();

        if (_profiles.Count == 0) return;

        int count = Math.Clamp(mission?.RecommendedTeam ?? 1, 1, _profiles.Count);
        foreach (var profile in _profiles.Take(count)) _enlisted.Add(profile.Id);

        foreach (string survivorId in previous.Where(id => !_enlisted.Contains(id)))
            _loadout.Clear(survivorId);
    }

    /// <summary>Remet l'ecran a jour — d'une sortie a l'autre, les reserves ont bouge.</summary>
    public void Refresh()
    {
        _title.Text = _camp.Day == 0 ? "CAMP — avant la premiere sortie" : $"CAMP — jour {_camp.Day}";

        _lastRun.Visible = !string.IsNullOrWhiteSpace(_debrief);
        _lastRun.Text = _debrief ?? string.Empty;

        RefreshReserves();

        _pendingRow.Visible = _camp.Pending.Count > 0;
        _pending.Text = _camp.Pending.Count == 0
            ? string.Empty
            : "RECOMPENSES EN ATTENTE — "
            + string.Join(", ", _camp.Pending.Select(reward => $"{Named(reward.Item)} x{reward.Quantity}"));

        for (int index = 0; index < _missionList.GetChildCount(); index++)
        {
            if (_missionList.GetChild(index) is Button button) button.ButtonPressed = index == _mission;
        }

        // Les vignettes suivent l'enrolement : la fiche retenue garde sa couleur pleine, celle qui
        // reste au camp recule d'un cran. Les points de vie sont ceux du depart — la ligne de base
        // de config/game_config.json, la meme pour tout le monde — parce qu'aucun etat de survivant
        // ne survit d'une mission a la suivante. Le jour ou il en existera un, c'est cette ligne qui
        // dira qu'Alice part a 62 sur 100.
        for (int rank = 0; rank < _survivorCards.Count && rank < _profiles.Count; rank++)
        {
            bool enlisted = _enlisted.Contains(_profiles[rank].Id);

            _survivorCards[rank].Selected = enlisted;
            _survivorCards[rank].Dimmed = !enlisted;

            _survivorHealth[rank].Text = $"PV {_baseline.MaxHealth:0} / {_baseline.MaxHealth:0}";
            _survivorStates[rank].Text = enlisted ? "ENROLE" : "reste au camp";
            _survivorStates[rank].AddThemeColorOverride("font_color", enlisted ? Heading : Faint);
        }

        _packs.Show(Enlisted());

        int count = _enlisted.Count;
        _teamCount.Text = count switch
        {
            0 => "EQUIPE — personne d'enrole",
            1 => "EQUIPE — 1 enrole",
            _ => $"EQUIPE — {count} enroles",
        };

        // La graine ne concerne que les quartiers generes : la carte dessinee n'en a pas.
        _seedRow.Visible = SelectedMission is { IsGenerated: true };

        _briefing.Text = Detail(SelectedMission);

        bool affordable = SelectedMission is { } chosen && MissionRewardService.CanAfford(_camp, chosen);

        _go.Text = SelectedMission is { EntryFuelCost: > 0 } paying
            ? $"ENTRER DANS LA MISSION — {Essence(paying.EntryFuelCost)} "
            + $"(stock : {_camp.Of(MissionRewardService.FuelResource):0.#})"
            : "PARTIR EN MISSION";

        _go.Disabled = count == 0 || SelectedMission is null || !affordable;
        _back.Disabled = _step == 0;
        _next.Disabled = _step >= 2 || (_step == 0 && SelectedMission is null) || (_step == 1 && count == 0);
        _next.Visible = _step < 2;
        _go.Visible = _step == 2;

        _steps.Text = _step switch
        {
            0 => "1  MISSION     2  EQUIPE     3  PAQUETAGE & INSTRUCTIONS",
            1 => "1  MISSION [OK]  2  EQUIPE     3  PAQUETAGE & INSTRUCTIONS",
            _ => "1  MISSION [OK]  2  EQUIPE [OK]  3  PAQUETAGE & INSTRUCTIONS",
        };

        for (int index = 0; index < _stepChoices.Count; index++)
            _stepChoices[index].ButtonPressed = index == _step;
    }

    private void ShowStep(int step)
    {
        int target = Math.Clamp(step, 0, 2);
        if (target > 0 && SelectedMission is null) target = 0;
        if (target > 1 && _enlisted.Count == 0) target = 1;

        _step = target;
        _missionStep.Visible = target == 0;
        _teamStep.Visible = target == 1;
        _loadoutStep.Visible = target == 2;
        Refresh();
    }

    /// <summary>
    /// La ligne d'une offre dans la liste : de quoi comparer trois missions d'un coup d'oeil, et
    /// rien de plus. Le detail est sous la liste, pour l'offre selectionnee seulement.
    /// </summary>
    private static string Headline(MissionDefinition mission)
    {
        string level = mission.Difficulty ?? "—";
        string team = $"equipe de {mission.RecommendedTeam}";

        return mission.EntryFuelCost > 0
            ? $"{mission.Name}     {level}     {team}     {Essence(mission.EntryFuelCost)}"
            : $"{mission.Name}     {level}     {team}";
    }

    /// <summary>
    /// Ce qu'une offre dit d'elle-meme (livrable 16), sans une phrase de recit : la zone, l'objectif
    /// chiffre, l'equipe, le temps, le prix et les recompenses exactes.
    ///
    /// Une mission ecrite a la main, elle, a un briefing et l'affiche : les deux formes coexistent
    /// parce que le champ <c>description</c> est ce qui les distingue, et non un mode d'affichage.
    /// </summary>
    private string Detail(MissionDefinition? mission)
    {
        if (mission is null) return "Aucune mission disponible.";
        if (!mission.IsOffer) return mission.Description ?? string.Empty;

        var lines = new List<string>
        {
            $"Difficulte : {mission.Difficulty}     Zone : {mission.ObjectiveZone ?? mission.Objective.ToString()}",
            $"Objectif : {Objective(mission)}",
            $"Equipe conseillee : {mission.RecommendedTeam}     Temps : {mission.TimeLimit ?? 0d:0} s"
          + $"     Cout d'entree : {Essence(mission.EntryFuelCost)}",
        };

        if (mission.Rewards.Count > 0)
        {
            lines.Add("Recompenses : " + string.Join("   ", mission.Rewards.Select(Reward)));
        }

        return string.Join("\n", lines);
    }

    /// <summary>L'objectif chiffre, dans le vocabulaire du type de mission.</summary>
    private string Objective(MissionDefinition mission)
    {
        var requirements = mission.Requirements ?? MissionRequirements.None;

        return mission.Type switch
        {
            MissionObjectiveKind.KillZombies => $"0 / {requirements.Zombies} zombies elimines",

            MissionObjectiveKind.CollectObjects => string.Join(
                ", ", requirements.Items.Select(line => $"{Named(line.Item)} x{line.Quantity}")),

            _ => string.Join(
                ", ",
                requirements.Resources.Select(
                    line => $"{MissionObjectiveNames.Resource(line.Resource)} x{line.Quantity}")),
        };
    }

    private string Reward(MissionReward reward) => reward.Category switch
    {
        RewardCategory.Rare => $"Objet rare : {Named(reward.Item)} x{reward.Quantity}",
        _ => $"{Named(reward.Item)} x{reward.Quantity}",
    };

    /// <summary>Le nom lisible d'un objet, ou son identifiant si le catalogue ne le connait pas.</summary>
    private string Named(string itemId) => _items.Find(itemId)?.Name ?? itemId;

    private static string Essence(int cost) => cost == 1 ? "1 essence" : $"{cost} essences";

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

    /// <summary>L'equipe retenue, dans l'ordre du roster — celui des couleurs d'anneau.</summary>
    private List<SurvivorProfile> Enlisted()
        => [.. _profiles.Where(profile => _enlisted.Contains(profile.Id))];

    private void Confirm()
    {
        var team = Enlisted();
        if (team.Count == 0) return;

        if (SelectedMission is not { } mission) return;

        // Le paquetage de ceux qui restent ne part pas avec ceux qui partent. Le desenrolement le
        // defait deja ; cette ligne tient la promesse meme si un jour l'equipe se composait ailleurs.
        _loadout.KeepOnly(team.Select(profile => profile.Id));

        Visible = false;
        // Les contraintes retenues partent avec le reste : elles ont ete acceptees ici, par les memes
        // portillons qui les recompileront sur la carte de la mission — ou un avertissement de plus
        // pourra tomber, parce que « aucun batiment ici » ne se sait qu'une fois la carte tiree.
        MissionChosen?.Invoke(mission, team, _loadout, _instructions?.Constraints ?? []);
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

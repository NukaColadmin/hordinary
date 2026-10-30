using Godot;
using Hordinary.AI.Instructions;
using Hordinary.AI.Planning.Pddl;
using Hordinary.Game;
using Hordinary.Instructions;

namespace Hordinary.UI;

/// <summary>
/// L'ecran du mode demonstration : une grille, un scenario, et rien d'autre.
///
/// <code>
/// +------------------------------------------------------------------+
/// |  DEMONSTRATION                                                   |
/// |  TERRAIN [La superette v]  SCENARIO [Carte blanche v]             |
/// |  VOTRE CONSIGNE                                                  |
/// |  [Soignez les blesses avant d'entrer.]  [AJOUTER]                 |
/// |  + Consignes pre-ecrites   + Details du scenario                 |
/// |                                                    [ LANCER ]   |
/// +------------------------------------------------------------------+
/// </code>
///
/// Il remplace l'ecran de camp quand <c>demo.enabled</c> est vrai, et c'est voulu qu'il n'en garde
/// rien : ni reserves, ni equipe a composer, ni paquetage. Tout cela est ecrit dans le scenario,
/// parce qu'un clip se refait a l'identique ou ne prouve rien — une prise qui dependrait de ce que
/// la precedente a consomme au camp ne serait plus la meme prise.
///
/// La case « consignes » est la seule decision laissee a l'ecran : c'est le contraste que filme un
/// clip de consigne, le meme scenario joue avec puis sans. L'ecran ne connait ni carte ni horde ; il
/// rend un choix, et <c>GameRoot</c> en fait une partie.
///
/// <para>
/// Une <b>carte blanche</b> (<see cref="DemoFocus.Strategy"/>) remplace cette case par le panneau
/// d'instructions du camp, le meme <see cref="InstructionPanel"/> : le joueur y ecrit sa strategie, en
/// francais quand le service de langage est actif, et elle passe les memes portillons qu'avant une
/// mission. L'ecran de demonstration ne traduit ni ne verifie rien lui-meme — un second chemin de
/// saisie serait un second endroit ou une consigne pourrait etre acceptee a tort.
/// </para>
/// </summary>
public partial class DemoScreen : CanvasLayer
{
    private static readonly Color Ink = new(0.93f, 0.9f, 0.85f);
    private static readonly Color Heading = new(0.92f, 0.72f, 0.4f);
    private static readonly Color Faint = new(0.68f, 0.63f, 0.56f);

    private DemoCatalogue _catalogue = DemoCatalogue.Empty;

    /// <summary>Le nom affiche de chaque survivant, par identifiant de profil.</summary>
    private IReadOnlyDictionary<string, string> _names = new Dictionary<string, string>();

    private PddlDomain? _domain;
    private InstructionPresetCatalogue _presets = InstructionPresetCatalogue.Empty;
    private InstructionSettings _instructionSettings = new();
    private InstructionClient? _client;

    private string? _debrief;
    private int _grid;
    private int _scenario;

    private OptionButton _grids = null!;
    private OptionButton _scenarios = null!;
    private Label _summary = null!;
    private Label _gridDetail = null!;
    private Label _detail = null!;
    private CheckBox _withInstructions = null!;
    private VBoxContainer _strategyBox = null!;
    private InstructionPanel? _strategy;
    private Button _go = null!;

    /// <summary>
    /// Le scenario retenu, si ses consignes s'appliquent, et — pour une carte blanche — celles du
    /// joueur : la contrainte et la phrase qui l'a produite, pour que le journal et le bilan disent
    /// les deux.
    /// </summary>
    public event Action<DemoScenario, bool, IReadOnlyList<(string Constraint, string Said)>>? ScenarioChosen;

    /// <param name="selected">Le scenario a presenter d'emblee : celui qu'on vient de jouer, pour la prise suivante.</param>
    /// <param name="domain">
    /// Le domaine contre lequel une carte blanche valide les consignes du joueur. Absent, une carte
    /// blanche se joue sans consigne : il n'y aurait rien pour les accepter.
    /// </param>
    /// <param name="client">Le service de langage, quand il est actif ; absent, la saisie attend la grammaire fermee.</param>
    public void Configure(
        DemoCatalogue catalogue,
        IReadOnlyDictionary<string, string> names,
        string? debrief = null,
        string? selected = null,
        bool withInstructions = true,
        PddlDomain? domain = null,
        InstructionPresetCatalogue? presets = null,
        InstructionSettings? instructionSettings = null,
        InstructionClient? client = null)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        _catalogue = catalogue;
        _names = names ?? new Dictionary<string, string>();
        _debrief = debrief;
        _domain = domain;
        _presets = presets ?? InstructionPresetCatalogue.Empty;
        _instructionSettings = instructionSettings ?? new InstructionSettings();
        _client = client;

        if (catalogue.Find(selected) is { } scenario)
        {
            _grid = Math.Max(0, catalogue.Grids.FindIndex(grid => grid.Id.Equals(scenario.Grid, StringComparison.OrdinalIgnoreCase)));
            _scenario = Math.Max(0, IndexOf(scenario));
        }

        Layer = 20;
        Build(withInstructions);
    }

    // --- Construction ------------------------------------------------------------------------------

    private void Build(bool withInstructions)
    {
        var veil = new ColorRect { Color = new Color(0.07f, 0.065f, 0.06f, 0.97f), MouseFilter = Control.MouseFilterEnum.Stop };
        veil.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(veil);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 24);

        AddChild(margin);

        var frame = new VBoxContainer();
        frame.AddThemeConstantOverride("separation", 8);
        margin.AddChild(frame);

        var title = Text(frame, 22, Ink);
        title.Text = "DEMONSTRATION";

        // Le titre et le lancement restent fixes quand on deploie les exemples ou les details.
        var body = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };

        frame.AddChild(body);

        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };

        column.AddThemeConstantOverride("separation", 8);
        body.AddChild(column);

        if (!string.IsNullOrWhiteSpace(_debrief))
        {
            // Le verdict et la consigne suffisent pour le clip ; la chronologie complete reste
            // accessible sans repousser le choix de la prise suivante hors de l'ecran.
            var lines = _debrief.Split('\n');
            var lastRun = Text(column, 14, Heading);
            lastRun.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            lastRun.Text = string.Join("\n", lines.Where((line, index) => index < 2
                    || line.StartsWith("Vos consignes :", StringComparison.Ordinal))
                .Select(line => line.StartsWith("PRISE ", StringComparison.Ordinal)
                    ? line.Split(" — ", 2)[0] : line));
            var fullReport = Disclosure(column, "Bilan complet");
            var report = Text(fullReport, 12, Faint);
            report.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            report.Text = _debrief;
        }

        var choices = new HBoxContainer();
        choices.AddThemeConstantOverride("separation", 16);
        column.AddChild(choices);
        _grids = Selector(choices, "TERRAIN");
        _scenarios = Selector(choices, "SCENARIO");
        foreach (var grid in _catalogue.Grids) _grids.AddItem(grid.Name);
        _grids.ItemSelected += index =>
        {
            _grid = (int)index;
            _scenario = 0;
            Refresh();
        };
        _scenarios.ItemSelected += index =>
        {
            _scenario = (int)index;
            Refresh();
        };

        _summary = Text(column, 14, Ink);
        _summary.AutowrapMode = TextServer.AutowrapMode.WordSmart;

        BuildStrategy(column);

        var details = Disclosure(column, "Details du scenario");
        _gridDetail = Text(details, 12, Faint);
        _gridDetail.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _detail = Text(details, 12, Ink);
        _detail.AutowrapMode = TextServer.AutowrapMode.WordSmart;

        var footer = new HBoxContainer();
        frame.AddChild(footer);

        _withInstructions = new CheckBox { Text = "Appliquer les consignes du scenario", ButtonPressed = withInstructions };
        _withInstructions.AddThemeFontSizeOverride("font_size", 13);
        _withInstructions.Toggled += _ => Refresh();
        footer.AddChild(_withInstructions);

        footer.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        _go = new Button { Text = "LANCER LA PRISE", CustomMinimumSize = new Vector2(220, 44) };
        _go.AddThemeFontSizeOverride("font_size", 15);
        UiSkin.Dress(_go);
        _go.Pressed += Confirm;
        footer.AddChild(_go);

        Refresh();
    }

    /// <summary>
    /// Le panneau d'une carte blanche. Il est construit une fois et garde ses consignes d'une prise a
    /// l'autre : on refait une prise pour voir l'effet d'une strategie, et la retaper a chaque fois ne
    /// ferait que payer une seconde traduction pour la meme phrase.
    ///
    /// Il n'a pas de defilement a lui : l'ecran entier defile, et deux defilements imbriques se
    /// disputeraient la molette — celui du panneau la garderait des que la souris passe dessus.
    /// </summary>
    private void BuildStrategy(Container parent)
    {
        _strategyBox = new VBoxContainer { Visible = false };
        parent.AddChild(_strategyBox);

        if (_domain is null) return;

        _strategy = new InstructionPanel
        {
            Name = "StrategyInstructions",
            Compact = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };

        _strategy.Configure(_domain, _presets, _instructionSettings, _client, () => SelectedScenario?.Seed ?? 0);
        _strategy.Changed += Refresh;
        _strategyBox.AddChild(_strategy);
    }

    private static OptionButton Selector(Container parent, string heading)
    {
        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };

        column.AddThemeConstantOverride("separation", 4);
        parent.AddChild(column);

        var label = Text(column, 13, Heading);
        label.Text = heading;

        var select = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 36), FitToLongestItem = false };
        select.AddThemeFontSizeOverride("font_size", 15);
        column.AddChild(select);
        return select;
    }

    private static VBoxContainer Disclosure(Container parent, string heading)
    {
        var toggle = new Button { Text = $"+ {heading}", ToggleMode = true,
            Alignment = HorizontalAlignment.Left };
        toggle.AddThemeFontSizeOverride("font_size", 12);
        parent.AddChild(toggle);
        var content = new VBoxContainer { Visible = false };
        parent.AddChild(content);
        toggle.Toggled += open =>
        {
            content.Visible = open;
            toggle.Text = $"{(open ? "−" : "+")} {heading}";
        };
        return content;
    }

    // --- Choix -------------------------------------------------------------------------------------

    private DemoGrid? SelectedGrid
        => _catalogue.Grids.Count == 0 ? null : _catalogue.Grids[Math.Clamp(_grid, 0, _catalogue.Grids.Count - 1)];

    private DemoScenario? SelectedScenario
    {
        get
        {
            if (SelectedGrid is not { } grid) return null;

            var scenarios = _catalogue.On(grid);
            return scenarios.Count == 0 ? null : scenarios[Math.Clamp(_scenario, 0, scenarios.Count - 1)];
        }
    }

    private int IndexOf(DemoScenario scenario)
    {
        var grid = _catalogue.FindGrid(scenario.Grid);
        if (grid is null) return 0;

        var scenarios = _catalogue.On(grid);
        for (int index = 0; index < scenarios.Count; index++)
        {
            if (scenarios[index].Id.Equals(scenario.Id, StringComparison.OrdinalIgnoreCase)) return index;
        }

        return 0;
    }

    /// <summary>
    /// Actualise les choix et le detail sans perdre les consignes deja saisies.
    /// </summary>
    private void Refresh()
    {
        _grids.Select(_grid);
        _scenarios.Clear();

        if (SelectedGrid is { } grid)
        {
            var scenarios = _catalogue.On(grid);

            for (int index = 0; index < scenarios.Count; index++)
            {
                var scenario = scenarios[index];
                _scenarios.AddItem(scenario.TakesPlayerInstructions ? "Carte blanche" : scenario.Name);
            }

            if (scenarios.Count > 0) _scenarios.Select(Math.Clamp(_scenario, 0, scenarios.Count - 1));

            _gridDetail.Text = grid.Description ?? string.Empty;
        }

        var selected = SelectedScenario;
        bool open = selected is { TakesPlayerInstructions: true };
        _summary.Text = open
            ? "Donnez une consigne au groupe, puis lancez la prise."
            : selected?.Summary ?? "Aucun scenario sur ce terrain.";

        _withInstructions.Visible = !open;
        _strategyBox.Visible = open;
        _withInstructions.Disabled = selected is not { Instructions.Count: > 0 };
        _detail.Text = selected is null ? "Aucun scenario sur cette grille." : Detail(selected);
        _go.Disabled = selected is null;
    }

    /// <summary>
    /// Tout ce que le scenario met en place, et ce qu'il doit montrer. L'attente est affichee pour la
    /// prise qu'on va lancer — avec ou sans consignes — parce que c'est elle que le bilan comparera.
    /// </summary>
    private string Detail(DemoScenario scenario)
    {
        bool withInstructions = _withInstructions.ButtonPressed;

        var lines = new List<string>
        {
            $"{Tag(scenario.Focus)}  {scenario.Name}",
            $"Situation : {scenario.Summary}",
        };

        if (!string.IsNullOrWhiteSpace(scenario.Watch)) lines.Add($"A regarder : {scenario.Watch}");

        lines.Add($"Equipe : {string.Join(" · ", scenario.Team.Select(Member))}");

        string time = scenario.TimeLimit is { } limit ? $"{limit:0} s" : "temps de mission";
        string panic = scenario.Panic switch
        {
            true => "active",
            false => "coupee",
            null => "selon debug.panic_enabled",
        };

        lines.Add($"Horde : {scenario.Zombies.Count} zombie(s)     Temps : {time}     Panique : {panic}     Graine : {scenario.Seed}");

        if (scenario.Mapped) lines.Add("Plan du quartier remis au groupe : le terrain est connu, pas la horde.");

        if (scenario.TakesPlayerInstructions)
        {
            var player = PlayerInstructions();

            lines.Add(_strategy is null
                ? "Carte blanche : aucun domaine exporte, rien ne peut valider une consigne — la prise se joue sans."
                : Translating
                    ? "Carte blanche : ecrivez vos consignes en francais ci-dessous — le service de langage les traduit, les portillons les verifient."
                    : "Carte blanche : service de langage coupe (instructions.enabled), la saisie attend la grammaire fermee ; les consignes pre-ecrites portent les memes contraintes.");

            lines.Add(player.Count == 0
                ? "Aucune consigne pour l'instant : le groupe se debrouillera seul."
                : $"Vos consignes : {string.Join("  ·  ", player.Select(entry => entry.Constraint))}");

            lines.Add(scenario.Expected(withInstructions: true, player.Count) is { } alone
                ? $"Attendu : {alone}"
                : "Attendu : rien d'ecrit — le bilan dira ce que votre strategie a fait faire au groupe.");

            return string.Join("\n", lines);
        }

        if (scenario.Instructions.Count > 0)
        {
            lines.Add(withInstructions
                ? $"Consignes : {string.Join("  ·  ", scenario.Instructions)}"
                : $"Consignes RETIREES pour cette prise : {string.Join("  ·  ", scenario.Instructions)}");
        }

        lines.Add(scenario.Expected(withInstructions) is { } expected
            ? $"Attendu : {expected}"
            : "Attendu : rien d'ecrit pour cette prise sans consignes.");

        return string.Join("\n", lines);
    }

    private string Member(DemoMember member)
    {
        string name = _names.TryGetValue(member.Survivor, out string? known) ? known : member.Survivor;
        var parts = new List<string> { member.At is null ? $"{name} (au van)" : $"{name} (repere {member.At})" };

        if (member.Carry.Count > 0) parts.Add(string.Join(", ", member.Carry));
        if (member.Health is { } health) parts.Add($"{health:0} PV");

        return string.Join(" — ", parts);
    }

    private static string Tag(DemoFocus focus) => focus switch
    {
        DemoFocus.Constraint => "[consigne]",
        DemoFocus.Cooperation => "[cooperation]",
        DemoFocus.Strategy => "[carte blanche]",
        DemoFocus.Combination => "[combinaison]",
        _ => "[action]",
    };

    private bool Translating => _instructionSettings.Enabled && _client is not null;

    /// <summary>Les consignes que le joueur a fait accepter au panneau, avec la phrase de chacune.</summary>
    private IReadOnlyList<(string Constraint, string Said)> PlayerInstructions()
        => _strategy is null
            ? []
            : [.. _strategy.Constraints.Zip(_strategy.Utterances, (constraint, said) => (constraint, said))];

    private void Confirm()
    {
        if (SelectedScenario is not { } scenario) return;

        Visible = false;

        if (scenario.TakesPlayerInstructions)
        {
            var player = PlayerInstructions();
            ScenarioChosen?.Invoke(scenario, player.Count > 0, player);
            return;
        }

        ScenarioChosen?.Invoke(scenario, _withInstructions.ButtonPressed && scenario.Instructions.Count > 0, []);
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

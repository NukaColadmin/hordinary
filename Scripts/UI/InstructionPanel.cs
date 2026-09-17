using Godot;
using Hordinary.AI.Instructions;
using Hordinary.AI.Planning.Pddl;

namespace Hordinary.UI;

/// <summary>
/// Les instructions tactiques de la mission (livrable 15).
///
/// <para>
/// Le panneau ne decide de rien et ne compile rien lui-meme : il appelle
/// <see cref="TacticalInstructions"/>, qui fait passer les portillons dans <c>Hordinary.Core</c>, et
/// il affiche ce qui en revient — la contrainte acceptee, ses avertissements, ou le refus avec le
/// portillon qui l'a produit et sa raison. Un systeme qui n'affiche que ses succes ne prouve rien :
/// le refus est montre au meme endroit que l'acceptation, et le journal garde les deux.
/// </para>
///
/// <para>
/// La saisie libre ici est celle de la <b>grammaire fermee</b>, pas du francais. Traduire une phrase
/// de joueur est le travail du service de langage, et lui seul ; <c>instructions.enabled</c> est
/// livre a <c>false</c>. Ce que ce panneau demontre est ce qui reste quand le modele est absent —
/// c'est-a-dire toute la mecanique : les instructions pre-ecrites portent exactement les memes
/// contraintes, et passent exactement les memes portillons.
/// </para>
/// </summary>
public partial class InstructionPanel : VBoxContainer
{
    private static readonly Color Ink = new(0.93f, 0.9f, 0.85f);
    private static readonly Color Heading = new(0.92f, 0.72f, 0.4f);
    private static readonly Color Faint = new(0.68f, 0.63f, 0.56f);
    private static readonly Color Good = new(0.62f, 0.88f, 0.55f);
    private static readonly Color Warn = new(0.95f, 0.78f, 0.36f);
    private static readonly Color Bad = new(0.9f, 0.4f, 0.36f);

    private TacticalInstructions _instructions = null!;
    private InstructionPresetCatalogue _presets = InstructionPresetCatalogue.Empty;
    private InstructionSettings _settings = new();

    private readonly Dictionary<string, Button> _toggles = new(StringComparer.OrdinalIgnoreCase);

    private VBoxContainer _list = null!;
    private VBoxContainer _active = null!;
    private Label _verdict = null!;
    private Label _counter = null!;
    private LineEdit _entry = null!;
    private bool _updating;

    public event Action? Changed;

    /// <summary>Les contraintes retenues, dans la grammaire fermee. C'est ce qui part en mission.</summary>
    public IReadOnlyList<string> Constraints
        => [.. _instructions?.Active.Select(constraint => constraint.Text) ?? []];

    /// <summary>Ce que le joueur a dit, pour le journal de la mission.</summary>
    public IReadOnlyList<string> Utterances
        => [.. _instructions?.Active.Select(constraint => constraint.Utterance ?? constraint.Text) ?? []];

    public InstructionPanel()
    {
        AddThemeConstantOverride("separation", 5);
    }

    public void Configure(PddlDomain domain, InstructionPresetCatalogue? presets, InstructionSettings? settings)
    {
        ArgumentNullException.ThrowIfNull(domain);

        _presets = presets ?? InstructionPresetCatalogue.Empty;
        _settings = settings ?? new InstructionSettings();
        _instructions = new TacticalInstructions(domain, _settings);

        var heading = new Label { Text = "INSTRUCTIONS TACTIQUES (OPTIONNELLES)" };
        heading.AddThemeFontSizeOverride("font_size", 13);
        heading.AddThemeColorOverride("font_color", Heading);
        AddChild(heading);

        var hint = new Label
        {
            Text = _settings.Enabled
                ? "Une regle pour toute l'equipe. Elle change le domaine de planification, pas les ordres."
                : "Service de langage coupe : les instructions pre-ecrites portent les memes contraintes "
                + "et passent les memes portillons.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        hint.AddThemeFontSizeOverride("font_size", 11);
        hint.AddThemeColorOverride("font_color", Faint);
        AddChild(hint);

        BuildPresets();
        BuildEntry();

        _verdict = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _verdict.AddThemeFontSizeOverride("font_size", 11);
        _verdict.AddThemeColorOverride("font_color", Faint);
        AddChild(_verdict);

        _active = new VBoxContainer();
        _active.AddThemeConstantOverride("separation", 2);
        AddChild(_active);

        Refresh();
    }

    /// <summary>
    /// Les instructions pre-ecrites de <c>config/instruction_presets.json</c>, une case a cocher
    /// chacune. Le libelle est la phrase qu'un joueur aurait tapee ; la contrainte qu'elle vaut est
    /// montree a cote, parce que c'est elle qui est appliquee et qu'il n'y a rien a cacher.
    /// </summary>
    private void BuildPresets()
    {
        if (_presets.Presets.Count == 0 || (!_settings.PresetsWhenDisabled && !_settings.Enabled)) return;

        _list = new VBoxContainer();
        _list.AddThemeConstantOverride("separation", 2);
        AddChild(_list);

        foreach (var preset in _presets.Presets)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            _list.AddChild(row);

            var toggle = new Button
            {
                Text = preset.Label,
                ToggleMode = true,
                CustomMinimumSize = new Vector2(0, 26),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                Alignment = HorizontalAlignment.Left,
            };

            toggle.AddThemeFontSizeOverride("font_size", 11);
            UiSkin.DressRow(toggle);
            toggle.Toggled += on => OnPresetToggled(preset, on);

            row.AddChild(toggle);
            _toggles[preset.Id] = toggle;

            var formal = new Label { Text = preset.Constraint };
            formal.AddThemeFontSizeOverride("font_size", 10);
            formal.AddThemeColorOverride("font_color", Faint);
            row.AddChild(formal);
        }
    }

    /// <summary>
    /// La saisie. Elle attend une contrainte de la grammaire fermee — les cinq formes, et rien
    /// d'autre — parce que c'est tout ce que le jeu sait appliquer sans modele. Un symbole invente y
    /// est refuse par le portillon 1, et c'est la meme ligne de code qui refuserait la sortie d'un
    /// modele : rien n'est verifie parce qu'on se mefie des modeles, tout est verifie parce que c'est
    /// une contrainte.
    /// </summary>
    private void BuildEntry()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        AddChild(row);

        _entry = new LineEdit
        {
            PlaceholderText = ConstraintGrammar.Forms(),
            MaxLength = _settings.MaxChars,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };

        _entry.AddThemeFontSizeOverride("font_size", 11);
        _entry.AddThemeColorOverride("font_color", Ink);
        _entry.TextSubmitted += _ => Submit();
        _entry.TextChanged += _ => UpdateCounter();
        row.AddChild(_entry);

        _counter = new Label();
        _counter.AddThemeFontSizeOverride("font_size", 10);
        _counter.AddThemeColorOverride("font_color", Faint);
        row.AddChild(_counter);

        var add = new Button { Text = "AJOUTER", CustomMinimumSize = new Vector2(84, 26) };
        add.AddThemeFontSizeOverride("font_size", 10);
        UiSkin.DressRow(add);
        add.Pressed += Submit;
        row.AddChild(add);

        UpdateCounter();
    }

    private void OnPresetToggled(InstructionPreset preset, bool on)
    {
        if (_updating) return;

        if (!on)
        {
            _instructions.Remove(preset.Constraint);
            Report(null);
            return;
        }

        // La phrase du joueur et la contrainte partent ensemble : le journal garde les deux, et un
        // refus qui ne citerait que la forme formelle ne dirait pas ce qu'on avait demande.
        var entry = _instructions.Add(preset.Constraint, utterance: preset.Label);

        if (!entry.Accepted) Untoggle(preset.Id);

        Report(entry);
    }

    private void Submit()
    {
        string text = _entry.Text.Trim();
        if (text.Length == 0) return;

        var entry = _instructions.Add(text);

        if (entry.Accepted) _entry.Text = string.Empty;

        UpdateCounter();
        Report(entry);
    }

    private void Untoggle(string id)
    {
        if (!_toggles.TryGetValue(id, out var toggle)) return;

        _updating = true;
        toggle.ButtonPressed = false;
        _updating = false;
    }

    /// <summary>Le verdict du dernier essai, puis l'etat courant. Les deux comptent.</summary>
    private void Report(InstructionEntry? entry)
    {
        if (entry is null)
        {
            _verdict.Text = string.Empty;
        }
        else if (!entry.Accepted)
        {
            _verdict.Text = $"Refuse (portillon {(int)entry.Gate!.Value + 1} — {Named(entry.Gate.Value)}) : {entry.Reason}";
            _verdict.AddThemeColorOverride("font_color", Bad);
        }
        else if (entry.Warnings.Count > 0)
        {
            _verdict.Text = $"Acceptee, avec reserve : {string.Join(" ", entry.Warnings)}";
            _verdict.AddThemeColorOverride("font_color", Warn);
        }
        else
        {
            _verdict.Text = $"Acceptee : {entry.Constraint}";
            _verdict.AddThemeColorOverride("font_color", Good);
        }

        Refresh();
        Changed?.Invoke();
    }

    private static string Named(InstructionGate gate) => gate switch
    {
        InstructionGate.Grammar => "grammaire",
        InstructionGate.Compilation => "compilation",
        InstructionGate.Satisfiability => "satisfiabilite",
        _ => "conformite",
    };

    /// <summary>Les contraintes actives, chacune avec de quoi la retirer.</summary>
    private void Refresh()
    {
        // Retire avant de liberer : QueueFree est differe a la fin de la frame, et deux rafraichis
        // dans la meme frame afficheraient chaque contrainte en double.
        foreach (var child in _active.GetChildren())
        {
            _active.RemoveChild(child);
            child.QueueFree();
        }

        foreach (var constraint in _instructions.Active)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            _active.AddChild(row);

            var label = new Label
            {
                Text = $"• {constraint.Utterance ?? constraint.Text}",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };

            label.AddThemeFontSizeOverride("font_size", 11);
            label.AddThemeColorOverride("font_color", Ink);
            row.AddChild(label);

            string text = constraint.Text;

            var drop = new Button { Text = "RETIRER", CustomMinimumSize = new Vector2(74, 24) };
            drop.AddThemeFontSizeOverride("font_size", 10);
            UiSkin.DressRow(drop);
            drop.Pressed += () => Drop(text);
            row.AddChild(drop);
        }

        UpdateCounter();
    }

    /// <summary>
    /// Retirer une contrainte recompile ce qui reste depuis le domaine d'origine, et non depuis le
    /// domaine reecrit : tout retirer rend le jeu bit a bit identique a ce qu'il etait.
    /// </summary>
    private void Drop(string text)
    {
        _instructions.Remove(text);

        foreach (var preset in _presets.Presets)
        {
            if (preset.Constraint.Equals(text, StringComparison.OrdinalIgnoreCase)) Untoggle(preset.Id);
        }

        Report(null);
    }

    private void UpdateCounter()
    {
        if (_counter is null || _instructions is null) return;

        _counter.Text = $"{_instructions.Active.Count}/{_settings.MaxActiveConstraints}";
    }
}

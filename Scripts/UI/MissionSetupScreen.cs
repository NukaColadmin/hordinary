using Godot;
using Hordinary.Agents;
using Hordinary.Game;

namespace Hordinary.UI;

/// <summary>
/// L'ecran de preparation : ce que le joueur fait avant que la partie ne commence.
///
/// Trois etapes, dans cet ordre, et toutes les trois visibles en meme temps — c'est un briefing, pas
/// un assistant en trois pages : on choisit ses agents en regardant la mission, et on change d'avis
/// en lisant la difficulte.
///
/// <code>
/// +---------------------------------------------------------------+
/// |  HORDINARY — PREPARATION                                       |
/// |                                                                |
/// |  2. MISSION                                                    |
/// |  > Ravitaillement pharmacie          Facile     equipe de 2    |
/// |    Fouille de l'entrepot ouest       Moyenne    equipe de 3    |
/// |    Traversee du hangar sud           Difficile  equipe de 3    |
/// |                                                                |
/// |  « La pharmacie est a deux rues du camp, derriere une porte  » |
/// |                                                                |
/// |  1. AGENTS — 2 enroles                    [ DEMARRER MISSION ] |
/// |  +--------+ +--------+ +--------+                              |
/// |  | ALICE  | |  BOB   | | CLAIRE |                              |
/// |  | HP ... | | HP ... | | HP ... |                              |
/// |  | SAN... | | SAN... | | SAN... |                              |
/// |  +--------+ +--------+ +--------+                              |
/// +---------------------------------------------------------------+
/// </code>
///
/// La grille d'agents est en bas parce que c'est la qu'elle restera : la meme barre, aux memes
/// coordonnees, sert de fiche d'equipe pendant la mission. Le joueur n'a donc pas a chercher ou sont
/// passees les jauges qu'il vient de lire — elles n'ont pas bouge, elles se sont mises a vivre.
///
/// L'ecran ne connait ni le monde, ni la horde, ni la carte : il rend un choix
/// (<see cref="Started"/>), et <c>GameRoot</c> en fait une partie.
/// </summary>
public partial class MissionSetupScreen : CanvasLayer
{
    private readonly List<AgentCard> _cards = [];
    private readonly HashSet<string> _enlisted = new(StringComparer.OrdinalIgnoreCase);

    private SurvivorSettings _baseline = SurvivorSettings.Default;
    private IReadOnlyList<MissionDefinition> _missions = [];
    private int _mission;

    private VBoxContainer _missionList = null!;
    private Label _briefing = null!;
    private Label _roster = null!;
    private HBoxContainer _grid = null!;
    private Button _start = null!;

    /// <summary>Emis quand le joueur valide : la mission choisie et les profils enroles, dans l'ordre.</summary>
    public event Action<MissionDefinition, IReadOnlyList<SurvivorProfile>>? Started;

    /// <summary>Les profils proposes, dans l'ordre de <c>config/survivors.json</c>.</summary>
    private IReadOnlyList<SurvivorProfile> _profiles = [];

    public void Configure(
        IReadOnlyList<SurvivorProfile> profiles,
        IReadOnlyList<MissionDefinition> missions,
        SurvivorSettings baseline)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(missions);

        _profiles = profiles;
        _missions = missions;
        _baseline = baseline ?? SurvivorSettings.Default;

        Build();
    }

    // --- Construction ----------------------------------------------------------------------------

    private void Build()
    {
        Layer = 20;

        var backdrop = new ColorRect
        {
            Color = new Color(0.04f, 0.04f, 0.06f, 0.88f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };

        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(backdrop);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 28);

        AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        margin.AddChild(column);

        column.AddChild(Title("HORDINARY — PREPARATION DE MISSION", 26, new Color(0.95f, 0.95f, 0.98f)));

        // Etape 2 en haut, etape 1 en bas : la grille d'agents doit rester au meme endroit que la
        // barre d'equipe qui la remplacera, sans quoi le joueur perdrait ses reperes au demarrage.
        column.AddChild(Title("MISSION", 14, new Color(0.55f, 0.78f, 1f)));

        _missionList = new VBoxContainer();
        _missionList.AddThemeConstantOverride("separation", 2);
        column.AddChild(_missionList);

        // La largeur minimale n'est pas cosmetique : une etiquette qui renvoie a la ligne calcule sa
        // hauteur minimale a partir de sa largeur, et la premiere passe de mise en page la lui donne
        // nulle. Sans plancher, le briefing se replie sur une colonne d'un caractere de large, reclame
        // trois mille pixels de haut, et pousse la grille d'agents hors de l'ecran.
        _briefing = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(640, 54),
        };

        _briefing.AddThemeFontSizeOverride("font_size", 13);
        _briefing.AddThemeColorOverride("font_color", new Color(0.78f, 0.78f, 0.82f));
        column.AddChild(_briefing);

        column.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });

        var header = new HBoxContainer();
        column.AddChild(header);

        _roster = Title("AGENTS", 14, new Color(0.55f, 0.78f, 1f));
        _roster.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(_roster);

        _start = new Button { Text = "DEMARRER LA MISSION", CustomMinimumSize = new Vector2(220, 38) };
        _start.Pressed += Confirm;
        header.AddChild(_start);

        _grid = new HBoxContainer();
        _grid.AddThemeConstantOverride("separation", 10);
        column.AddChild(_grid);

        BuildMissions();
        BuildAgents();

        Refresh();
    }

    private void BuildMissions()
    {
        for (int index = 0; index < _missions.Count; index++)
        {
            int choice = index;
            var mission = _missions[index];

            var button = new Button
            {
                Text = $"{mission.Name}          {mission.Difficulty ?? "—"}          equipe de {mission.RecommendedTeam}",
                Alignment = HorizontalAlignment.Left,
                ToggleMode = true,
                CustomMinimumSize = new Vector2(0, 30),
            };

            button.Pressed += () =>
            {
                _mission = choice;
                Refresh();
            };

            _missionList.AddChild(button);
        }
    }

    private void BuildAgents()
    {
        foreach (var profile in _profiles)
        {
            var card = new AgentCard();
            card.Show(profile, _baseline);
            card.Clicked += Toggle;

            _grid.AddChild(card);

            _cards.Add(card);
        }

        // Une equipe par defaut, pour que le bouton soit cliquable sans rien faire : celle que la
        // premiere mission recommande.
        foreach (var profile in _profiles.Take(Math.Max(1, _missions.FirstOrDefault()?.RecommendedTeam ?? 1)))
            _enlisted.Add(profile.Id);
    }

    // --- Choix -----------------------------------------------------------------------------------

    private void Toggle(AgentCard card)
    {
        if (card.Profile is not { } profile) return;

        if (!_enlisted.Remove(profile.Id)) _enlisted.Add(profile.Id);

        Refresh();
    }

    private void Refresh()
    {
        for (int index = 0; index < _missionList.GetChildCount(); index++)
        {
            if (_missionList.GetChild(index) is Button button) button.ButtonPressed = index == _mission;
        }

        var mission = Selected;
        _briefing.Text = mission?.Description ?? "Aucune mission disponible.";

        foreach (var card in _cards)
        {
            card.Selected = card.Profile is { } profile && _enlisted.Contains(profile.Id);
        }

        int count = _enlisted.Count;
        _roster.Text = count switch
        {
            0 => "AGENTS — aucun enrole",
            1 => "AGENTS — 1 enrole",
            _ => $"AGENTS — {count} enroles",
        };

        _start.Disabled = count == 0 || mission is null;
    }

    private MissionDefinition? Selected
        => _missions.Count == 0 ? null : _missions[Math.Clamp(_mission, 0, _missions.Count - 1)];

    private void Confirm()
    {
        if (Selected is not { } mission) return;

        var team = _profiles.Where(profile => _enlisted.Contains(profile.Id)).ToList();
        if (team.Count == 0) return;

        Visible = false;
        Started?.Invoke(mission, team);
    }

    private static Label Title(string text, int size, Color color)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }
}

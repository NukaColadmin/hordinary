using Godot;
using Hordinary.Agents;

namespace Hordinary.UI;

/// <summary>
/// La barre d'equipe du bas d'ecran, pendant la mission : les memes fiches que l'ecran de
/// preparation, aux memes coordonnees, mais vivantes.
///
/// C'est deliberement la seule interface qui ne demande rien au joueur pour etre lue. Les trois
/// panneaux de texte du livrable 8 disent ce que la simulation pense — ils appartiennent au mode
/// debug et disparaissent avec lui (<c>debug.hud_text</c>). Cette barre-ci dit ce qu'un joueur a
/// besoin de savoir : qui est encore dehors, dans quel etat, et sur qui portent ses ordres.
///
/// Un agent rentre au camp ne quitte pas la barre : sa fiche s'eteint et annonce « rentre au camp ».
/// Le faire disparaitre laisserait un trou, et le joueur se demanderait qui manque.
/// </summary>
public partial class AgentRoster : CanvasLayer
{
    private readonly List<AgentCard> _cards = [];
    private IReadOnlyList<SurvivorAgent> _agents = [];

    private HBoxContainer _grid = null!;

    /// <summary>Emis quand le joueur clique une fiche : c'est un changement de point de vue.</summary>
    public event Action<SurvivorAgent>? Picked;

    public void Configure(IReadOnlyList<SurvivorAgent> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);

        _agents = agents;
        Layer = 15;

        var anchor = new MarginContainer();
        anchor.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        anchor.GrowVertical = Control.GrowDirection.Begin;

        anchor.AddThemeConstantOverride("margin_left", 12);
        anchor.AddThemeConstantOverride("margin_right", 12);
        anchor.AddThemeConstantOverride("margin_bottom", 12);

        AddChild(anchor);

        _grid = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _grid.AddThemeConstantOverride("separation", 10);
        anchor.AddChild(_grid);

        foreach (var agent in _agents)
        {
            var card = new AgentCard();
            card.Refresh(agent.Survivor);

            var chosen = agent;
            card.Clicked += _ => Picked?.Invoke(chosen);

            _grid.AddChild(card);
            _cards.Add(card);
        }
    }

    /// <summary>Rafraichit les jauges. Appelee a la cadence de perception, pas a chaque frame.</summary>
    public void Refresh(SurvivorAgent? selected)
    {
        for (int index = 0; index < _cards.Count && index < _agents.Count; index++)
        {
            var agent = _agents[index];

            _cards[index].Refresh(agent.Survivor);
            _cards[index].Selected = ReferenceEquals(agent, selected);
        }
    }
}

using Godot;
using Hordinary.Agents;
using Hordinary.Game;

namespace Hordinary.UI;

/// <summary>
/// La grille des survivants, en bas d'ecran (livrable 12) : une vignette par survivant, toujours
/// visible, et le stock du camp dans le coin droit.
///
/// C'est deliberement la seule interface qui ne demande rien au joueur pour etre lue, et elle ne
/// depend plus du mode debug : les panneaux de texte du livrable 8 disent ce que la simulation pense,
/// cette barre dit ce qu'un joueur a besoin de savoir — qui est encore dehors, dans quel etat, et sur
/// qui portent ses ordres.
///
/// Un agent hors de combat ne quitte pas la barre : sa fiche s'eteint et le dit.
/// Le faire disparaitre laisserait un trou, et le joueur se demanderait qui manque.
/// </summary>
public partial class AgentRoster : CanvasLayer
{
    private readonly List<AgentCard> _cards = [];
    private IReadOnlyList<SurvivorAgent> _agents = [];

    private HBoxContainer _grid = null!;
    private HBoxContainer _stock = null!;

    private CampStock? _camp;
    private ItemIcons? _icons;

    /// <summary>Cote d'une vignette de reserve. La tuile fait 32 : un facteur entier, ou rien.</summary>
    private const int IconSize = 32;

    /// <summary>Emis quand le joueur clique une fiche : c'est un changement de point de vue.</summary>
    public event Action<SurvivorAgent>? Picked;

    /// <summary>Emis au double-clic : le joueur veut voir ce survivant, pas seulement le selectionner.</summary>
    public event Action<SurvivorAgent>? Centered;

    public void Configure(
        IReadOnlyList<SurvivorAgent> agents,
        CampStock? camp = null,
        ItemIcons? icons = null)
    {
        ArgumentNullException.ThrowIfNull(agents);

        _agents = agents;
        _camp = camp;
        _icons = icons;

        Layer = 15;

        var anchor = new MarginContainer();
        anchor.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        anchor.GrowVertical = Control.GrowDirection.Begin;

        anchor.AddThemeConstantOverride("margin_left", 12);
        anchor.AddThemeConstantOverride("margin_right", 12);
        anchor.AddThemeConstantOverride("margin_bottom", 12);

        AddChild(anchor);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        anchor.AddChild(row);

        _grid = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _grid.AddThemeConstantOverride("separation", 7);
        row.AddChild(_grid);

        for (int index = 0; index < _agents.Count; index++)
        {
            var agent = _agents[index];

            var card = new AgentCard();
            card.Refresh(agent.Survivor, index);

            card.Clicked += _ => Picked?.Invoke(agent);
            card.DoubleClicked += _ => Centered?.Invoke(agent);

            _grid.AddChild(card);
            _cards.Add(card);
        }

        _stock = BuildStock();
        row.AddChild(_stock);
    }

    /// <summary>
    /// La couleur d'anneau de chaque survivant, dans l'ordre des fiches. Elle vient de la carte, pas
    /// d'ici : c'est le meme tableau qui teinte l'anneau au sol, et c'est ce qui fait qu'une bordure
    /// de vignette et un rond sous des pieds se lisent comme le meme signe.
    /// </summary>
    public void Paint(IReadOnlyList<Color> ringColors)
    {
        ArgumentNullException.ThrowIfNull(ringColors);
        if (ringColors.Count == 0) return;

        for (int index = 0; index < _cards.Count; index++)
            _cards[index].RingColor = ringColors[index % ringColors.Count];
    }

    /// <summary>Rafraichit les jauges. Appelee a la cadence de perception, pas a chaque frame.</summary>
    public void Refresh(SurvivorAgent? selected)
    {
        for (int index = 0; index < _cards.Count && index < _agents.Count; index++)
        {
            var agent = _agents[index];

            _cards[index].Refresh(agent.Survivor, index);
            _cards[index].Selected = ReferenceEquals(agent, selected);
        }

        RefreshStock();
    }

    // --- Stock du camp -----------------------------------------------------------------------------

    /// <summary>
    /// Les quatre reserves, dans le coin droit de la barre. La vignette est la tuile de l'objet qui
    /// remplit la reserve : la conserve qu'on ramasse au sol est celle qu'on compte ici, sans qu'une
    /// ligne d'explication soit necessaire.
    /// </summary>
    private HBoxContainer BuildStock()
    {
        var line = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        line.AddThemeConstantOverride("separation", 12);

        if (_camp is null) return line;

        foreach (string resource in CampStock.Resources)
        {
            var reserve = new HBoxContainer { Name = resource };
            reserve.AddThemeConstantOverride("separation", 4);
            line.AddChild(reserve);

            if (_icons?.SlotForResource(resource, IconSize) is { } icon) reserve.AddChild(icon);

            var amount = new Label { Name = "Amount" };
            amount.AddThemeFontSizeOverride("font_size", 14);
            amount.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
            amount.AddThemeConstantOverride("outline_size", 4);
            reserve.AddChild(amount);
        }

        return line;
    }

    /// <summary>
    /// La couleur dit l'etat, le texte dit le chiffre : on voit d'abord ce qui manque, on lit ensuite
    /// de combien. Les seuils viennent de <c>config/camp.json</c> et non d'ici — une interface qui
    /// redeciderait a partir de quel chiffre une reserve est basse ferait exister deux verites.
    /// </summary>
    private void RefreshStock()
    {
        if (_camp is null) return;

        foreach (string resource in CampStock.Resources)
        {
            if (_stock.GetNodeOrNull<HBoxContainer>(resource) is not { } reserve) continue;
            if (reserve.GetNodeOrNull<Label>("Amount") is not { } amount) continue;

            amount.Text = $"{_camp.Of(resource):0.#}";
            amount.AddThemeColorOverride("font_color", Tint(_camp.LevelOf(resource)));
        }

        static Color Tint(StockLevel level) => level switch
        {
            StockLevel.Full => new Color(0.62f, 0.88f, 0.55f),
            StockLevel.Low => new Color(0.95f, 0.78f, 0.36f),
            _ => new Color(0.9f, 0.4f, 0.36f),
        };
    }
}

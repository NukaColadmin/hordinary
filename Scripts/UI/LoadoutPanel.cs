using Godot;
using Hordinary.Agents;
using Hordinary.Game;
using Hordinary.World;

namespace Hordinary.UI;

/// <summary>
/// Le paquetage, sur l'ecran de camp (livrable 12) : ce que chacun emporte, pris dans l'entrepot.
///
/// <code>
/// +----------------------------------------------------------------+
/// |  PAQUETAGE — ce que chacun emporte                              |
/// |  ALICE      2/6 emplacements, 3,0 kg                            |
/// |    emporte  [Trousse de soins x1 X] [Batte de baseball X]       |
/// |    donner   [Bandage (3)] [Revolver (1)] [Lampe torche (1)]     |
/// +----------------------------------------------------------------+
/// </code>
///
/// Le panneau ne decide de rien : il rend un <see cref="CampLoadout"/> et lui renvoie les clics.
/// Les emplacements, les piles et le poids sont ceux du sac reel — le paquetage <b>est</b> un
/// inventaire, et un ecran qui recompterait les emplacements finirait par mentir le jour ou
/// <c>inventory.slots</c> changerait.
///
/// Seuls les survivants enroles ont une ligne : composer le paquetage de quelqu'un qui ne part pas
/// n'a pas de sens, et le desenroler defait le sien (<see cref="CampLoadout.Clear"/>) plutot que de
/// lui laisser l'unique trousse du camp en otage.
/// </summary>
public partial class LoadoutPanel : VBoxContainer
{
    private const int IconSize = 20;

    private static readonly Color Ink = new(0.93f, 0.9f, 0.85f);
    private static readonly Color Heading = new(0.92f, 0.72f, 0.4f);
    private static readonly Color Faint = new(0.68f, 0.63f, 0.56f);

    /// <summary>Un sac charge au-dela de <c>max_weight</c> part, mais il part au ralenti.</summary>
    private static readonly Color Warning = new(0.95f, 0.78f, 0.36f);

    private CampLoadout? _loadout;
    private ItemCatalogue _items = ItemCatalogue.Empty;
    private ItemIcons? _icons;
    private InventorySettings _limits = InventorySettings.Default;

    private Label _title = null!;
    private VBoxContainer _rows = null!;

    /// <summary>Emis apres chaque don ou reprise : l'ecran de camp se remet a jour en entier.</summary>
    public event Action? Changed;

    public LoadoutPanel()
    {
        AddThemeConstantOverride("separation", 4);

        _title = new Label();
        _title.AddThemeFontSizeOverride("font_size", 13);
        _title.AddThemeColorOverride("font_color", Heading);
        _title.Text = "PAQUETAGE — ce que chacun emporte";
        AddChild(_title);

        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 6);
        AddChild(_rows);
    }

    public void Configure(
        CampLoadout loadout,
        ItemCatalogue items,
        InventorySettings? limits = null,
        ItemIcons? icons = null)
    {
        _loadout = loadout ?? throw new ArgumentNullException(nameof(loadout));
        _items = items ?? ItemCatalogue.Empty;
        _limits = limits ?? InventorySettings.Default;
        _icons = icons;
    }

    /// <summary>
    /// Redessine les lignes des survivants enroles.
    ///
    /// Le panneau est reconstruit d'un bloc plutot que mis a jour fiche par fiche : le contenu change
    /// a chaque clic — une pile disparait, un bouton se grise, l'etagere se vide —, et une mise a
    /// jour partielle demanderait de retrouver quel bouton correspond a quoi pour economiser une
    /// dizaine de noeuds qu'on ne redessine qu'au clic.
    /// </summary>
    public void Show(IReadOnlyList<SurvivorProfile> enlisted)
    {
        ArgumentNullException.ThrowIfNull(enlisted);

        foreach (var child in _rows.GetChildren())
        {
            _rows.RemoveChild(child);
            child.QueueFree();
        }

        if (_loadout is null) return;

        Visible = enlisted.Count > 0;
        if (!Visible) return;

        var shelf = _loadout.Shelf();

        _title.Text = shelf.Count == 0 && _loadout.IsEmpty
            ? "PAQUETAGE — l'entrepot est vide"
            : "PAQUETAGE — ce que chacun emporte";

        foreach (var profile in enlisted) BuildRow(profile, shelf);
    }

    private void BuildRow(SurvivorProfile profile, IReadOnlyList<InventoryStack> shelf)
    {
        if (_loadout is null) return;

        var row = new VBoxContainer();
        row.AddThemeConstantOverride("separation", 2);
        _rows.AddChild(row);

        float weight = _loadout.WeightOf(profile.Id);

        var header = new Label
        {
            Text = $"{profile.Name.ToUpperInvariant()}     "
                 + $"{_loadout.SlotsOf(profile.Id)}/{_limits.Slots} emplacements, {weight:0.#} kg"
                 + (_loadout.Overweight(profile.Id) ? "  — surcharge, il partira au ralenti" : string.Empty),
        };

        header.AddThemeFontSizeOverride("font_size", 12);
        header.AddThemeColorOverride("font_color", _loadout.Overweight(profile.Id) ? Warning : Ink);
        row.AddChild(header);

        BuildCarried(row, profile);
        BuildShelf(row, profile, shelf);
    }

    /// <summary>Ce qu'il emporte. Cliquer une pile en repose un exemplaire sur l'etagere.</summary>
    private void BuildCarried(Container parent, SurvivorProfile profile)
    {
        if (_loadout is null) return;

        var carried = _loadout.Of(profile.Id);
        if (carried.Count == 0) return;

        var line = Line(parent, "emporte");

        foreach (var stack in carried)
        {
            string label = stack.Count > 1 ? $"{Named(stack.ItemId)} x{stack.Count}" : Named(stack.ItemId);

            var button = Chip($"{label}   X", stack.ItemId);

            button.Pressed += () =>
            {
                if (_loadout.TakeBack(profile.Id, stack.ItemId)) Changed?.Invoke();
            };

            line.AddChild(button);
        }
    }

    /// <summary>Ce que l'entrepot offre encore. Cliquer en confie un exemplaire a ce survivant.</summary>
    private void BuildShelf(Container parent, SurvivorProfile profile, IReadOnlyList<InventoryStack> shelf)
    {
        if (_loadout is null) return;
        if (shelf.Count == 0) return;

        var line = Line(parent, "donner");

        foreach (var stack in shelf)
        {
            var button = Chip($"{Named(stack.ItemId)} ({stack.Count})", stack.ItemId);

            // Le refus se voit avant le clic : un sac plein grise le bouton plutot que d'avaler le
            // geste. C'est le sac lui-meme qui repond, jamais un comptage refait ici.
            button.Disabled = !_loadout.CanGive(profile.Id, stack.ItemId);

            button.Pressed += () =>
            {
                if (_loadout.Give(profile.Id, stack.ItemId)) Changed?.Invoke();
            };

            line.AddChild(button);
        }
    }

    /// <summary>Une ligne etiquetee, dont les vignettes passent a la ligne quand la place manque.</summary>
    private HFlowContainer Line(Container parent, string caption)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        parent.AddChild(row);

        var label = new Label { Text = caption, CustomMinimumSize = new Vector2(70, 0) };
        label.AddThemeFontSizeOverride("font_size", 11);
        label.AddThemeColorOverride("font_color", Faint);
        row.AddChild(label);

        var flow = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        flow.AddThemeConstantOverride("h_separation", 6);
        flow.AddThemeConstantOverride("v_separation", 4);
        row.AddChild(flow);

        return flow;
    }

    /// <summary>
    /// Un bouton d'objet : la vignette du sol et le nom du catalogue. C'est la meme image que celle
    /// qu'on verra sous les pieds du survivant — il n'existe pas de serie d'icones a part.
    /// </summary>
    private Button Chip(string text, string itemId)
    {
        var button = new Button
        {
            Text = text,
            Icon = _icons?.For(itemId),
            ExpandIcon = false,
            CustomMinimumSize = new Vector2(0, 26),
        };

        button.AddThemeFontSizeOverride("font_size", 11);
        button.AddThemeConstantOverride("icon_max_width", IconSize);
        UiSkin.DressRow(button);

        return button;
    }

    /// <summary>Le nom lisible d'un objet, ou son identifiant si le catalogue ne le connait pas.</summary>
    private string Named(string itemId) => _items.Find(itemId)?.Name ?? itemId;
}

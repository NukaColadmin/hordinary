using Godot;
using Hordinary.Agents;

namespace Hordinary.UI;

/// <summary>
/// Le panneau lateral du livrable 12 : tout ce qu'il y a a savoir sur le survivant selectionne, et
/// rien sur les autres.
///
/// Il remplace <see cref="AgentPanel"/> et <see cref="PlanPanel"/> dans l'usage du joueur et cesse
/// d'etre conditionne au mode debug — les informations exigees par le livrable 8 restent toutes
/// presentes, elles ne sont simplement plus reservees a un observateur. Les deux panneaux de texte
/// survivent a cote, pour le mode debug, avec les scores Utility AI et les noeuds GBFS que celui-ci
/// n'affiche pas.
///
/// Il ne decide de rien, pas meme d'un mot : les aptitudes et les competences sont lues dans le
/// dictionnaire du profil sans que leurs noms soient connus d'avance, et une vignette d'inventaire
/// est la tuile de l'objet, decoupee par <see cref="ItemIcons"/> a partir de son identifiant.
/// </summary>
public partial class SurvivorPanel : CanvasLayer
{
    private const int PanelWidth = 380;
    private const int PortraitSize = 64;
    private const int SlotSize = 32;

    private static readonly Color Ink = new(0.18f, 0.13f, 0.08f);
    private static readonly Color Faint = new(0.42f, 0.36f, 0.29f);
    private static readonly Color Heading = new(0.45f, 0.26f, 0.1f);

    private static readonly Color HealthColor = new(0.44f, 0.75f, 0.35f);
    private static readonly Color PanicColor = new(0.24f, 0.35f, 0.42f);
    private static readonly Color TrackColor = new(0.35f, 0.3f, 0.25f, 0.55f);

    private ItemIcons? _icons;
    private int _rank;

    private TextureRect _portrait = null!;
    private Panel _ring = null!;
    private Label _name = null!;

    private Gauge _health = null!;
    private Gauge _panic = null!;

    private VBoxContainer _stats = null!;
    private HBoxContainer _slots = null!;
    private Label _load = null!;
    private VBoxContainer _decision = null!;

    public void Configure(ItemIcons? icons = null)
    {
        _icons = icons;

        Layer = 14;
        Build();
    }

    // --- Construction ------------------------------------------------------------------------------

    private void Build()
    {
        var anchor = new MarginContainer();
        anchor.SetAnchorsPreset(Control.LayoutPreset.RightWide);
        anchor.GrowHorizontal = Control.GrowDirection.Begin;

        anchor.AddThemeConstantOverride("margin_right", 12);
        anchor.AddThemeConstantOverride("margin_top", 12);

        // La grille du bas occupe le bas de l'ecran : le panneau s'arrete au-dessus d'elle plutot que
        // de passer dessous, ou la moitie de l'inventaire serait cachee.
        anchor.AddThemeConstantOverride("margin_bottom", 165);

        AddChild(anchor);

        var frame = new PanelContainer
        {
            CustomMinimumSize = new Vector2(PanelWidth, 0),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };

        if (UiSkin.FrameLarge() is { } skin) frame.AddThemeStyleboxOverride("panel", skin);
        anchor.AddChild(frame);

        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };

        frame.AddChild(scroll);

        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 5);
        scroll.AddChild(column);

        BuildIdentity(column);

        _health = Gauge.Add(column, "SANTE", HealthColor);
        _panic = Gauge.Add(column, "PANIQUE", PanicColor);

        // Ce qui change pendant la mission d'abord, ce qui ne change jamais ensuite. Le panneau tient
        // dans 446 unites de haut et le reste se deroule : autant que ce soit la fiche signaletique
        // qui passe sous la ligne de flottaison, pas l'inventaire ni la decision en cours.
        BuildInventory(column);

        _decision = Section(column, "DECISION");
        _stats = Section(column, "CARACTERISTIQUES");
    }

    private void BuildIdentity(Container parent)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        parent.AddChild(row);

        _ring = new Panel { CustomMinimumSize = new Vector2(PortraitSize + 6, PortraitSize + 6) };

        _portrait = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };

        _portrait.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _portrait.OffsetLeft = 3;
        _portrait.OffsetTop = 3;
        _portrait.OffsetRight = -3;
        _portrait.OffsetBottom = -3;

        _ring.AddChild(_portrait);
        row.AddChild(_ring);

        var titles = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation", 1);
        row.AddChild(titles);

        _name = Text(titles, 20, Ink);

        RingColor = new Color(0.31f, 0.78f, 1f);
    }

    private void BuildInventory(Container parent)
    {
        var header = new HBoxContainer();
        parent.AddChild(header);

        var title = Text(header, 12, Heading);
        title.Text = "INVENTAIRE";
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        _load = Text(header, 11, Faint);

        _slots = new HBoxContainer();
        _slots.AddThemeConstantOverride("separation", 4);
        parent.AddChild(_slots);
    }

    /// <summary>Un titre de section et la colonne vide qui le suit, videe puis remplie a chaque passe.</summary>
    private static VBoxContainer Section(Container parent, string title)
    {
        var label = Text(parent, 12, Heading);
        label.Text = title;

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 1);
        parent.AddChild(column);

        return column;
    }

    /// <summary>La couleur d'anneau du survivant affiche — la meme que sous ses pieds sur la carte.</summary>
    public Color RingColor
    {
        set
        {
            var box = new StyleBoxFlat { BgColor = value };
            box.SetCornerRadiusAll(4);
            _ring.AddThemeStyleboxOverride("panel", box);
        }
    }

    // --- Contenu -----------------------------------------------------------------------------------

    public void Refresh(SurvivorAgent? agent, int rank, double now)
    {
        if (agent is null)
        {
            Visible = false;
            return;
        }

        Visible = true;
        _rank = rank;

        var survivor = agent.Survivor;
        var profile = survivor.Profile;

        _portrait.Texture = UiSkin.Portrait(profile.Character ?? UiSkin.CharacterAt(_rank));

        _name.Text = survivor.Name.ToUpperInvariant();
        _health.Write(survivor.Health, survivor.MaxHealth);
        _panic.Write(survivor.Panic * 100f, 100f, survivor.PanicEnabled ? null : "neutralisee (debug)");

        RefreshStats(survivor);
        RefreshInventory(survivor);
        RefreshDecision(agent, now);
    }

    /// <summary>
    /// Les caracteristiques que la simulation lit reellement. Elles viennent de la section
    /// <c>survivor</c> de <c>config/game_config.json</c> et sont les memes pour tout le monde : le
    /// panneau les montre parce qu'elles expliquent ce qu'on voit a l'ecran, pas pour distinguer
    /// celui-ci de celui-la.
    /// </summary>
    private void RefreshStats(Survivor survivor)
    {
        Clear(_stats);

        var settings = survivor.Settings;

        Line("marche", $"{settings.WalkSpeed:0.#} cases/s");
        Line("course", $"{settings.RunSpeed:0.#} cases/s");
        Line("vision", $"{survivor.VisionRange} cases, {survivor.FieldOfViewDegrees:0}°");
        Line("fouille", $"{settings.SearchRadius:0.#} cases");
        Line("soin rendu", $"{settings.HealAmount:0} PV");

        void Line(string name, string value) => KeyValue(_stats, name, value);
    }


    /// <summary>
    /// Les six emplacements du livrable 11. Un emplacement montre la <b>tuile</b> de l'objet, a
    /// l'echelle native : il n'existe pas de serie d'icones separee, et repeindre l'atlas repeint donc
    /// l'inventaire.
    /// </summary>
    private void RefreshInventory(Survivor survivor)
    {
        Clear(_slots);

        var inventory = survivor.Inventory;
        var stacks = inventory.Stacks;

        for (int index = 0; index < inventory.Settings.Slots; index++)
        {
            var slot = new PanelContainer { CustomMinimumSize = new Vector2(SlotSize + 8, SlotSize + 8) };

            bool filled = index < stacks.Count;
            bool equipped = filled && inventory.IsEquipped(stacks[index].ItemId);

            var box = new StyleBoxFlat
            {
                BgColor = new Color(0.28f, 0.24f, 0.19f, 0.5f),
                BorderColor = equipped ? new Color(0.95f, 0.78f, 0.35f) : new Color(0.45f, 0.38f, 0.3f),
            };

            box.SetCornerRadiusAll(3);
            box.SetBorderWidthAll(equipped ? 2 : 1);
            slot.AddThemeStyleboxOverride("panel", box);

            if (filled)
            {
                var stack = stacks[index];

                if (_icons?.Slot(stack.ItemId, SlotSize) is { } icon) slot.AddChild(icon);
                else
                {
                    var fallback = new Label { Text = stack.ItemId[..Math.Min(3, stack.ItemId.Length)] };
                    fallback.AddThemeFontSizeOverride("font_size", 10);
                    slot.AddChild(fallback);
                }

                slot.TooltipText = inventory.IsBroken(stack.ItemId) ? $"{stack} (cassee)" : stack.ToString();
            }

            _slots.AddChild(slot);
        }

        string overweight = inventory.IsOverweight ? " — surcharge" : string.Empty;
        _load.Text = $"{inventory.TotalWeight:0.#}/{inventory.Settings.MaxWeight:0.#} kg{overweight}";
        _load.AddThemeColorOverride("font_color", inventory.IsOverweight ? new Color(0.8f, 0.35f, 0.3f) : Faint);
    }

    /// <summary>
    /// Ce que le survivant a decide et pourquoi. Les informations exigees par le livrable 8 sont
    /// toutes la — objectif, action, plan, derniere cause de replanification —, elles cessent
    /// simplement d'etre reservees au mode debug.
    /// </summary>
    private void RefreshDecision(SurvivorAgent agent, double now)
    {
        Clear(_decision);

        if (!agent.Survivor.Alive)
        {
            KeyValue(_decision, "etat", "hors de combat");
            return;
        }

        string goal = agent.Utility.CurrentGoal is { } current
            ? $"{current} ({agent.Utility.CurrentScore:0.00}), depuis {now - agent.Utility.LastChangeAt:0.0} s"
            : "aucun — rien ne depasse le seuil";

        KeyValue(_decision, "objectif", goal);

        string action = agent.Plan?.Current is { } step
            ? $"{step.Describe()} ({agent.Executor.ElapsedInStep:0.0}/{step.Duration:0.#} s)"
            : "aucune";

        KeyValue(_decision, "action", action);

        if (agent.Plan is { } plan)
        {
            KeyValue(
                _decision,
                "plan",
                $"{plan.Goal}{(plan.IsFallback ? " (repli)" : string.Empty)} — "
              + $"{plan.CurrentActionIndex}/{plan.StepCount}, cout {plan.TotalCost:0.##}, bruit {plan.TotalNoise:0.##}");

            foreach (string line in plan.DescribeSteps())
            {
                var entry = Text(_decision, 10, Faint);
                entry.Text = $"   {line}";
                entry.ClipText = true;
            }
        }
        else KeyValue(_decision, "plan", $"aucun — {agent.Brain.LastReason}");

        string replan = agent.Brain.LastReplan is { } record
            ? $"{record.Trigger.Reason} (t = {record.Timestamp}, {agent.Brain.ReplanCount} au total)"
            : $"aucune ({agent.Brain.ReplanCount} au total)";

        KeyValue(_decision, "replanification", replan);
    }

    // --- Mise en forme -----------------------------------------------------------------------------

    private static Label Text(Container parent, int size, Color color)
    {
        var label = new Label();
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        parent.AddChild(label);
        return label;
    }

    /// <summary>Une ligne « nom  valeur », le nom a gauche en pale, la valeur a droite en encre.</summary>
    private static void KeyValue(Container parent, string name, string value)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        parent.AddChild(row);

        var key = Text(row, 11, Faint);
        key.Text = name;
        key.CustomMinimumSize = new Vector2(82, 0);
        key.VerticalAlignment = VerticalAlignment.Top;

        var text = Text(row, 11, Ink);
        text.Text = value;
        text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
    }

    private static StyleBoxFlat Fill(Color color)
    {
        var box = new StyleBoxFlat { BgColor = color };
        box.SetCornerRadiusAll(2);
        return box;
    }

    private static void Clear(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }

    /// <summary>
    /// Une jauge nommee : le mot a gauche, le chiffre exact a droite, la barre en dessous. Les deux se
    /// lisent — la barre pour l'etat d'un coup d'oeil, le chiffre pour la decision.
    /// </summary>
    private sealed class Gauge
    {
        private Label _caption = null!;
        private ProgressBar _bar = null!;

        public static Gauge Add(Container parent, string title, Color color)
        {
            var gauge = new Gauge();

            var row = new HBoxContainer();
            parent.AddChild(row);

            var name = Text(row, 11, Heading);
            name.Text = title;
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

            gauge._caption = Text(row, 11, Ink);

            gauge._bar = new ProgressBar
            {
                ShowPercentage = false,
                CustomMinimumSize = new Vector2(0, 10),
            };

            gauge._bar.AddThemeStyleboxOverride("background", Fill(TrackColor));
            gauge._bar.AddThemeStyleboxOverride("fill", Fill(color));
            parent.AddChild(gauge._bar);

            return gauge;
        }

        public void Write(float value, float maximum, string? note = null)
        {
            _caption.Text = note ?? $"{value:0}/{maximum:0}";

            _bar.MaxValue = Math.Max(0.0001d, maximum);
            _bar.Value = Math.Clamp(value, 0f, maximum);
        }
    }
}

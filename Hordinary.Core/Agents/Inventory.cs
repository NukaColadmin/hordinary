using Hordinary.World;

namespace Hordinary.Agents;

/// <summary>
/// Contraintes du sac a dos, section <c>inventory</c> de <c>config/game_config.json</c>
/// (specification, livrable 11).
///
/// Le MVP portait un sac sans fond : on ramassait indefiniment, et rien de ce qu'on prenait ne
/// coutait quelque chose. Ces quatre limites transforment le ramassage en arbitrage — combien
/// d'emplacements, combien de kilos, et ce qui reste au sol quand on tombe.
/// </summary>
public sealed class InventorySettings
{
    /// <summary>Emplacements. Un objet empilable en occupe un seul jusqu'a <c>max_stack</c>.</summary>
    public int Slots { get; init; } = 6;

    /// <summary>Poids transportable avant penalite, en regard des <c>weight</c> du catalogue.</summary>
    public float MaxWeight { get; init; } = 20f;

    /// <summary>Armes equipables simultanement. Une seule : on ne degaine pas deux fusils.</summary>
    public int WeaponSlots { get; init; } = 1;

    /// <summary>
    /// Multiplicateur de vitesse applique au-dela de <see cref="MaxWeight"/>. Un survivant surcharge
    /// ralentit : c'est ce qui empeche de tout emporter et de rentrer quand meme.
    /// </summary>
    public float OverweightSpeedPenalty { get; init; } = 0.5f;

    /// <summary>
    /// A <c>true</c>, un survivant mort laisse son inventaire au sol. C'est ce qui rend une mort
    /// couteuse autrement que par le nombre — et ce qui donne une raison d'aller rechercher un corps.
    /// </summary>
    public bool DropOnDeath { get; init; } = true;

    public static InventorySettings Default { get; } = new();
}

/// <summary>Une pile d'objets dans un emplacement : un identifiant du catalogue et une quantite.</summary>
public sealed record InventoryStack(string ItemId, int Count)
{
    public override string ToString() => Count > 1 ? $"{ItemId} x{Count}" : ItemId;
}

/// <summary>
/// Le sac d'un survivant : des piles d'identifiants du catalogue, sous contrainte d'emplacements et
/// de poids, plus l'arme actuellement en main.
///
/// Il ne connait aucun objet par son nom. « Est-ce que je porte des medicaments » se demande avec un
/// identifiant venu de <c>config/items.json</c>, et le sac repond sans savoir ce qu'il transporte.
/// </summary>
public sealed class Inventory(ItemCatalogue catalogue, InventorySettings? settings = null)
{
    private readonly ItemCatalogue _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
    private readonly List<InventoryStack> _stacks = [];

    /// <summary>Durabilite restante des armes portees, par identifiant. Absente = arme intacte.</summary>
    private readonly Dictionary<string, int> _durability = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Munitions restantes des armes a feu portees, par identifiant.</summary>
    private readonly Dictionary<string, int> _ammo = new(StringComparer.OrdinalIgnoreCase);

    public InventorySettings Settings { get; } = settings ?? InventorySettings.Default;

    public IReadOnlyList<InventoryStack> Stacks => _stacks;

    /// <summary>L'arme en main, ou <c>null</c> a mains nues. Une seule a la fois.</summary>
    public string? EquippedWeapon { get; private set; }

    /// <summary>Les outils equipes — lampe, radio, caisse a outils. Ils n'occupent pas l'emplacement d'arme.</summary>
    private readonly HashSet<string> _equippedTools = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> EquippedTools => _equippedTools;

    /// <summary>Emis quand l'equipement change : le domaine de planification est a respecialiser.</summary>
    public event Action? EquipmentChanged;

    /// <summary>Emis quand le contenu change : la charge, donc la vitesse, est a recalculer.</summary>
    public event Action? Changed;

    public int UsedSlots => _stacks.Count;

    public float TotalWeight => _stacks.Sum(stack => _catalogue.WeightOf(stack.ItemId) * stack.Count);

    public bool IsFull => UsedSlots >= Settings.Slots;

    /// <summary>Au-dela du poids transportable : le survivant avance moins vite.</summary>
    public bool IsOverweight => TotalWeight > Settings.MaxWeight;

    /// <summary>Multiplicateur de vitesse du a la charge. 1 tant qu'on reste sous la limite.</summary>
    public float SpeedMultiplier => IsOverweight ? Settings.OverweightSpeedPenalty : 1f;

    public int CountOf(string itemId)
        => _stacks.Where(stack => stack.ItemId.Equals(itemId, StringComparison.OrdinalIgnoreCase))
            .Sum(stack => stack.Count);

    public bool Carries(string? itemId) => itemId is not null && CountOf(itemId) > 0;

    public IEnumerable<string> ItemIds => _stacks.Select(stack => stack.ItemId);

    /// <summary>
    /// L'identifiant du premier objet porte qui sait soigner, ou <c>null</c>. Le sac ne connait
    /// aucun objet par son nom : il demande au catalogue lequel declare un effet de soin. C'est ce
    /// qui permet a une action « soigner » d'exister sans que le domaine nomme la trousse, et de
    /// rester vraie quand la mission part chercher un jerrican.
    /// </summary>
    public string? HealingItemId => _stacks
        .Select(stack => stack.ItemId)
        .FirstOrDefault(itemId => _catalogue.Find(itemId) is { } definition
            && definition.Effects.Any(effect => effect.Verb == EffectVerb.Heal));

    /// <summary>Vrai quand le sac contient de quoi soigner, quel que soit l'objet.</summary>
    public bool HasHealingItem => HealingItemId is not null;

    /// <summary>
    /// Le premier objet porte qui fait du bruit la ou il tombe — un petard —, ou <c>null</c>. Meme
    /// regle que pour le soin : le catalogue dit lequel declare <c>emit_noise</c>, et le domaine
    /// parle d'un <c>noisemaker</c> sans jamais nommer le petard.
    /// </summary>
    public string? NoiseMakerId => _stacks
        .Select(stack => stack.ItemId)
        .FirstOrDefault(itemId => _catalogue.Find(itemId) is { } definition
            && definition.Effects.Any(effect => effect.Verb == EffectVerb.EmitNoise));

    /// <summary>
    /// Tous les objets, un identifiant par exemplaire — ce que la mort laisse tomber au sol, et ce
    /// qu'un debrief enumere.
    /// </summary>
    public IEnumerable<string> Enumerate()
        => _stacks.SelectMany(stack => Enumerable.Repeat(stack.ItemId, stack.Count));

    /// <summary>
    /// Ce sac prendrait-il un exemplaire de plus ? C'est la regle du ramassage posee une fois, et
    /// non deux : <see cref="Add"/> commence par la consulter, et un ecran qui propose de donner un
    /// objet a un survivant la consulte aussi plutot que de recompter les emplacements pour son
    /// compte.
    /// </summary>
    public bool Accepts(string itemId)
    {
        if (_catalogue.Find(itemId) is not { } definition) return false;

        if (definition.Stackable
            && _stacks.Any(stack => stack.ItemId.Equals(definition.Id, StringComparison.OrdinalIgnoreCase)
                                 && stack.Count < definition.MaxStack))
        {
            return true;
        }

        return !IsFull;
    }

    /// <summary>
    /// Ramasser. Echoue quand l'objet est inconnu du catalogue, ou quand il ne reste ni emplacement
    /// ni pile ouverte — un inventaire plein refuse un ramassage de plus, et c'est l'echec d'action
    /// qui declenche une replanification.
    ///
    /// Le poids, lui, n'interdit rien : il ralentit. Un survivant peut choisir de rentrer charge.
    /// </summary>
    public bool Add(string itemId)
    {
        if (_catalogue.Find(itemId) is not { } definition) return false;
        if (!Accepts(definition.Id)) return false;

        if (definition.Stackable)
        {
            for (int i = 0; i < _stacks.Count; i++)
            {
                if (!_stacks[i].ItemId.Equals(itemId, StringComparison.OrdinalIgnoreCase)) continue;
                if (_stacks[i].Count >= definition.MaxStack) continue;

                _stacks[i] = _stacks[i] with { Count = _stacks[i].Count + 1 };
                Changed?.Invoke();
                return true;
            }
        }

        if (IsFull) return false;

        _stacks.Add(new InventoryStack(definition.Id, 1));
        Changed?.Invoke();

        if (definition.IsWeapon)
        {
            if (definition.Durability > 0) _durability[definition.Id] = definition.Durability;
            if (definition.Ammo is { } rounds) _ammo[definition.Id] = rounds;
        }

        return true;
    }

    /// <summary>Retirer un exemplaire. Un objet retire de l'inventaire cesse d'etre equipe.</summary>
    public bool Remove(string itemId)
    {
        for (int i = 0; i < _stacks.Count; i++)
        {
            if (!_stacks[i].ItemId.Equals(itemId, StringComparison.OrdinalIgnoreCase)) continue;

            if (_stacks[i].Count > 1)
            {
                _stacks[i] = _stacks[i] with { Count = _stacks[i].Count - 1 };
                Changed?.Invoke();
                return true;
            }

            string removed = _stacks[i].ItemId;
            _stacks.RemoveAt(i);
            Unequip(removed);
            Changed?.Invoke();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Passer un objet a quelqu'un d'autre, de la main a la main. Il garde son usure : une batte
    /// fendue pretee reste fendue, alors que <see cref="Add"/> seul la rendrait neuve — et un pret
    /// deviendrait un moyen de reparer. Echoue, sans rien changer d'un cote ni de l'autre, si le sac
    /// qui recoit n'a plus de place.
    /// </summary>
    public bool HandTo(Inventory receiver, string itemId)
    {
        ArgumentNullException.ThrowIfNull(receiver);

        if (ReferenceEquals(receiver, this) || !Carries(itemId)) return false;
        if (!receiver.Add(itemId)) return false;

        if (_durability.TryGetValue(itemId, out int wear)) receiver._durability[itemId] = wear;
        if (_ammo.TryGetValue(itemId, out int rounds)) receiver._ammo[itemId] = rounds;

        Remove(itemId);
        return true;
    }

    /// <summary>Vide le sac et renvoie ce qu'il contenait : ce qu'un corps laisse derriere lui.</summary>
    public IReadOnlyList<string> DropAll()
    {
        var dropped = Enumerate().ToList();

        _stacks.Clear();
        _durability.Clear();
        _ammo.Clear();

        bool had = EquippedWeapon is not null || _equippedTools.Count > 0;
        EquippedWeapon = null;
        _equippedTools.Clear();
        if (had) EquipmentChanged?.Invoke();
        if (dropped.Count > 0) Changed?.Invoke();

        return dropped;
    }

    // --- Equipement -------------------------------------------------------------------------------

    /// <summary>
    /// Prendre une arme en main. Elle remplace celle qui y etait : <c>weapon_slots</c> vaut un, et la
    /// precedente retourne dans le sac sans se perdre.
    /// </summary>
    public bool EquipWeapon(string itemId)
    {
        if (_catalogue.Find(itemId) is not { IsWeapon: true } weapon) return false;
        if (!Carries(weapon.Id)) return false;
        if (Settings.WeaponSlots < 1) return false;
        if (string.Equals(EquippedWeapon, weapon.Id, StringComparison.OrdinalIgnoreCase)) return true;

        EquippedWeapon = weapon.Id;
        EquipmentChanged?.Invoke();
        return true;
    }

    /// <summary>Equiper un outil : lampe, radio, caisse a outils. Ils s'accumulent, contrairement aux armes.</summary>
    public bool EquipTool(string itemId)
    {
        if (_catalogue.Find(itemId) is not { Category: ItemCategory.Tool } tool) return false;
        if (!Carries(tool.Id)) return false;
        if (!_equippedTools.Add(tool.Id)) return true;

        EquipmentChanged?.Invoke();
        return true;
    }

    public bool Unequip(string itemId)
    {
        bool changed = false;

        if (string.Equals(EquippedWeapon, itemId, StringComparison.OrdinalIgnoreCase))
        {
            EquippedWeapon = null;
            changed = true;
        }

        changed |= _equippedTools.Remove(itemId);

        if (changed) EquipmentChanged?.Invoke();
        return changed;
    }

    /// <summary>
    /// Une arme portee en plus de celle qu'on a en main, et qui frappe encore : ce qu'on peut preter
    /// sans se desarmer. <c>null</c> s'il n'y en a pas. Une arme cassee ne compte pas — la preter ne
    /// rendrait personne arme.
    /// </summary>
    public string? SpareWeapon => _stacks
        .Select(stack => stack.ItemId)
        .Where(id => _catalogue.Find(id) is { IsWeapon: true })
        .Where(id => !string.Equals(id, EquippedWeapon, StringComparison.OrdinalIgnoreCase))
        .Where(CanAttackWith)
        .OrderBy(id => id, StringComparer.Ordinal)
        .FirstOrDefault();

    /// <summary>
    /// Ce que l'equipement fait d'une caracteristique du porteur : le produit des multiplicateurs que
    /// les objets equipes declarent pour elle (<see cref="ItemDefinition.StatMultiplier"/>). Un objet au
    /// fond du sac ne compte pas — une radio eteinte ne porte pas la voix.
    /// </summary>
    public float StatMultiplier(string stat)
        => EquippedItems.Aggregate(1f, (factor, item) => factor * item.StatMultiplier(stat));

    /// <summary>Vrai si l'arme en main frappe encore : ce que <c>armed</c> dit au planificateur.</summary>
    public bool IsArmed => EquippedWeapon is { } weapon && CanAttackWith(weapon);

    public bool IsEquipped(string itemId)
        => string.Equals(EquippedWeapon, itemId, StringComparison.OrdinalIgnoreCase)
        || _equippedTools.Contains(itemId);

    /// <summary>Tout ce qui est equipe : l'arme en main et les outils. C'est ce qui pondere les actions.</summary>
    public IEnumerable<ItemDefinition> EquippedItems
    {
        get
        {
            if (_catalogue.Find(EquippedWeapon) is { } weapon) yield return weapon;

            foreach (string tool in _equippedTools)
            {
                if (_catalogue.Find(tool) is { } definition) yield return definition;
            }
        }
    }

    // --- Usure ------------------------------------------------------------------------------------

    /// <summary>Durabilite restante d'une arme portee. <c>null</c> si elle est indestructible ou absente.</summary>
    public int? DurabilityOf(string itemId)
        => _durability.TryGetValue(itemId, out int left) ? left : null;

    /// <summary>Munitions restantes d'une arme a feu portee. <c>null</c> si ce n'en est pas une.</summary>
    public int? AmmoOf(string itemId)
        => _ammo.TryGetValue(itemId, out int left) ? left : null;

    /// <summary>
    /// Une arme cassee reste dans l'inventaire — c'est une information a ramener au camp, pas un
    /// objet qui disparait sans prevenir — mais elle ne frappe plus.
    /// </summary>
    public bool IsBroken(string itemId) => DurabilityOf(itemId) == 0;

    public bool CanAttackWith(string? itemId)
        => itemId is not null && Carries(itemId) && !IsBroken(itemId);

    /// <summary>
    /// Portee de l'arme, en cellules. Une arme a feu sans munitions retombe a un : on ne tire pas
    /// a six cases avec un barillet vide, on cogne.
    /// </summary>
    public int RangeOf(string itemId)
    {
        if (_catalogue.Find(itemId) is not { } weapon) return 1;
        if (weapon.IsFirearm && (AmmoOf(weapon.Id) ?? 0) <= 0) return 1;

        return Math.Max(1, weapon.Range);
    }

    /// <summary>Duree d'un coup porte avec l'arme donnee, telle que le catalogue la declare.</summary>
    public float AttackDurationOf(string itemId)
        => _catalogue.Find(itemId)?.AttackDuration ?? 1f;

    /// <summary>Recharger une arme a feu, au depart d'une mission ou sur le stock du camp.</summary>
    public void Reload(string itemId, int rounds)
    {
        if (_catalogue.Find(itemId) is not { IsFirearm: true } weapon) return;

        int current = _ammo.TryGetValue(weapon.Id, out int left) ? left : 0;
        _ammo[weapon.Id] = Math.Min(current + rounds, weapon.Ammo!.Value);
    }

    /// <summary>
    /// Ce qu'un coup porte reellement avec l'arme donnee, et le bruit qu'il fait. Une arme a feu sans
    /// munitions retombe sur ses valeurs de massue : elle reste utilisable, simplement mauvaise.
    /// C'est ce qui rend <c>ammo_box</c> interessante sans la rendre indispensable.
    /// </summary>
    public (float Damage, float Noise) StrikeWith(string itemId)
    {
        if (_catalogue.Find(itemId) is not { } weapon) return (0f, 0f);
        if (IsBroken(weapon.Id)) return (0f, 0f);

        if (weapon.IsFirearm && (AmmoOf(weapon.Id) ?? 0) <= 0)
        {
            return (weapon.UnloadedDamage, weapon.UnloadedNoise);
        }

        return (weapon.Damage, weapon.NoiseLevel);
    }

    /// <summary>
    /// Enregistre un coup porte : une munition en moins pour une arme a feu chargee, un point de
    /// durabilite en moins sinon. Une arme a feu vide s'use donc comme une massue, ce qui est ce
    /// qu'elle est devenue.
    /// </summary>
    public void RegisterStrike(string itemId)
    {
        if (_catalogue.Find(itemId) is not { } weapon) return;

        if (weapon.IsFirearm && _ammo.TryGetValue(weapon.Id, out int rounds) && rounds > 0)
        {
            _ammo[weapon.Id] = rounds - 1;
            return;
        }

        if (_durability.TryGetValue(weapon.Id, out int left) && left > 0)
        {
            _durability[weapon.Id] = left - 1;
        }
    }

    public override string ToString()
        => _stacks.Count == 0
            ? "sac vide"
            : $"{string.Join(", ", _stacks)} ({TotalWeight:0.#} kg{(IsOverweight ? ", surcharge" : string.Empty)})";
}

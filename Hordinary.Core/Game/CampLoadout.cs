using Hordinary.Agents;
using Hordinary.World;

namespace Hordinary.Game;

/// <summary>
/// Le paquetage : ce que le joueur confie a chacun avant le depart, pris dans l'entrepot du camp.
///
/// C'est une decision de joueur au sens du livrable 15 — pas un reflexe. Un survivant ramasse ce
/// qu'il trouve sur place parce qu'il en a besoin la tout de suite ; decider la veille lequel des
/// trois emporte l'unique fusil est une <b>strategie</b>, et elle revient au joueur. La regle de
/// coupe est la meme que partout : « est-ce que le survivant ferait ca sans qu'on le lui dise ? »
///
/// Rien n'est debite tant que le van n'est pas parti. Composer un paquetage, le defaire, changer
/// d'avis ne coute rien — exactement comme consulter une offre de mission : c'est
/// <see cref="HandOver"/> qui touche l'entrepot, une seule fois, au depart.
///
/// Les limites du sac ne sont pas redites ici : chaque paquetage <b>est</b> un
/// <see cref="Inventory"/>, construit avec les memes reglages que celui du survivant. Un ecran qui
/// recompterait les emplacements ferait exister une seconde verite, qui aurait fini par mentir le
/// jour ou <c>inventory.slots</c> aurait change.
/// </summary>
public sealed class CampLoadout
{
    private readonly CampStock _camp;
    private readonly ItemCatalogue _catalogue;
    private readonly InventorySettings _limits;

    /// <summary>Un sac previsionnel par survivant, cree a la premiere ligne qu'on lui donne.</summary>
    private readonly Dictionary<string, Inventory> _packs = new(StringComparer.OrdinalIgnoreCase);

    public CampLoadout(CampStock camp, ItemCatalogue catalogue, InventorySettings? limits = null)
    {
        _camp = camp ?? throw new ArgumentNullException(nameof(camp));
        _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
        _limits = limits ?? InventorySettings.Default;
    }

    /// <summary>Ce que ce survivant-la emporte, par pile. Vide s'il n'a rien recu.</summary>
    public IReadOnlyList<InventoryStack> Of(string survivorId)
        => _packs.TryGetValue(survivorId, out var pack) ? pack.Stacks : [];

    /// <summary>Le poids du paquetage, au bareme du catalogue.</summary>
    public float WeightOf(string survivorId)
        => _packs.TryGetValue(survivorId, out var pack) ? pack.TotalWeight : 0f;

    /// <summary>Emplacements occupes, sur <see cref="InventorySettings.Slots"/>.</summary>
    public int SlotsOf(string survivorId)
        => _packs.TryGetValue(survivorId, out var pack) ? pack.UsedSlots : 0;

    /// <summary>Vrai si le paquetage depasse deja le poids transportable : il partira ralenti.</summary>
    public bool Overweight(string survivorId)
        => _packs.TryGetValue(survivorId, out var pack) && pack.IsOverweight;

    /// <summary>Combien d'exemplaires de cet objet sont deja promis, toutes fiches confondues.</summary>
    public int Assigned(string itemId)
        => _packs.Values.Sum(pack => pack.CountOf(itemId));

    /// <summary>Ce que l'entrepot offre encore : ce qu'il contient, moins ce qui est deja promis.</summary>
    public int Available(string itemId) => Math.Max(0, _camp.CountOf(itemId) - Assigned(itemId));

    /// <summary>
    /// L'etagere telle que l'ecran de camp la montre : un objet, et le nombre d'exemplaires encore
    /// libres. L'ordre est celui du catalogue et non celui des arrivages, pour qu'une etagere ne se
    /// reorganise pas sous la souris entre deux missions.
    /// </summary>
    public IReadOnlyList<InventoryStack> Shelf()
        => [.. _catalogue.Items
            .Select(item => new InventoryStack(item.Id, Available(item.Id)))
            .Where(stack => stack.Count > 0)];

    /// <summary>
    /// Peut-on encore confier cet objet a ce survivant ? C'est ce que l'ecran demande pour griser un
    /// bouton, et c'est exactement ce que <see cref="Give"/> repondra — la place dans le sac est
    /// tenue par <see cref="Inventory.Accepts"/>, jamais recomptee ici.
    /// </summary>
    public bool CanGive(string survivorId, string itemId)
    {
        if (string.IsNullOrWhiteSpace(survivorId)) return false;
        if (_catalogue.Find(itemId) is null) return false;
        if (Available(itemId) <= 0) return false;

        return _packs.TryGetValue(survivorId, out var pack) ? pack.Accepts(itemId) : _limits.Slots > 0;
    }

    /// <summary>
    /// Confie un exemplaire a un survivant. Echoue quand l'entrepot n'en a plus de libre, quand
    /// l'objet est inconnu du catalogue, ou quand le sac est plein — les trois refus que l'ecran
    /// montre en grisant le bouton.
    /// </summary>
    public bool Give(string survivorId, string itemId)
    {
        if (string.IsNullOrWhiteSpace(survivorId)) return false;
        if (_catalogue.Find(itemId) is null) return false;
        if (Available(itemId) <= 0) return false;

        if (!_packs.TryGetValue(survivorId, out var pack))
        {
            pack = new Inventory(_catalogue, _limits);
            _packs[survivorId] = pack;
        }

        return pack.Add(itemId);
    }

    /// <summary>Repose un exemplaire sur l'etagere.</summary>
    public bool TakeBack(string survivorId, string itemId)
        => _packs.TryGetValue(survivorId, out var pack) && pack.Remove(itemId);

    /// <summary>
    /// Defait entierement un paquetage — ce que fait le desenrolement d'un survivant. Sans cela, un
    /// survivant retire de l'equipe garderait l'unique trousse du camp en otage : elle resterait
    /// promise a quelqu'un qui ne part pas, et introuvable pour ceux qui partent.
    /// </summary>
    public void Clear(string survivorId) => _packs.Remove(survivorId);

    /// <summary>Ne garde que les paquetages de ceux qui partent reellement.</summary>
    public void KeepOnly(IEnumerable<string> survivorIds)
    {
        ArgumentNullException.ThrowIfNull(survivorIds);

        var keep = new HashSet<string>(survivorIds, StringComparer.OrdinalIgnoreCase);

        foreach (string id in _packs.Keys.Where(id => !keep.Contains(id)).ToList()) _packs.Remove(id);
    }

    /// <summary>
    /// Le depart : l'entrepot est debite, le sac est rempli, et ce qui se tient en main est mis en
    /// main. Une arme confiee est <b>equipee</b> — le joueur a decide qui porte le fusil, la lui
    /// laisser au fond du sac reviendrait a ne pas ecouter la decision.
    ///
    /// L'entrepot n'est debite que de ce qui rentre reellement dans le sac : les limites sont deja
    /// tenues par <see cref="Give"/>, et cette ligne n'est la que pour qu'un objet ne puisse jamais
    /// disparaitre entre l'etagere et l'epaule.
    /// </summary>
    /// <returns>Ce qui a ete remis, un identifiant par exemplaire.</returns>
    public IReadOnlyList<string> HandOver(Survivor survivor)
    {
        ArgumentNullException.ThrowIfNull(survivor);

        if (!_packs.TryGetValue(survivor.Id, out var pack)) return [];

        var handed = new List<string>();

        foreach (string itemId in pack.Enumerate())
        {
            if (!_camp.Withdraw(itemId)) continue;

            if (!survivor.AddToInventory(itemId))
            {
                // Le sac du survivant a refuse ce que le paquetage acceptait : l'exemplaire retourne
                // a l'entrepot plutot que de s'evaporer entre les deux.
                _camp.Store(itemId);
                continue;
            }

            handed.Add(itemId);

            if (_catalogue.Find(itemId) is not { } item) continue;

            if (item.IsWeapon) survivor.Inventory.EquipWeapon(item.Id);
            else if (item.Category == ItemCategory.Tool) survivor.Inventory.EquipTool(item.Id);
        }

        return handed;
    }

    /// <summary>
    /// Le retour de ce qui a ete <b>prete</b>, et de rien d'autre. C'est ce qui distingue un pret
    /// d'une recompense : une mission perdue ne rapporte rien, mais le fusil que le camp a confie
    /// revient avec celui qui l'a rapporte. Ce que le groupe a ramasse en chemin, lui, se depose
    /// (<see cref="CampStock.Deposit"/>) et depend du verdict.
    /// </summary>
    /// <returns>Ce qui est reellement rentre a l'entrepot.</returns>
    public IReadOnlyList<string> Recover(Survivor survivor)
    {
        ArgumentNullException.ThrowIfNull(survivor);

        if (!_packs.TryGetValue(survivor.Id, out var pack)) return [];

        var recovered = new List<string>();

        foreach (string itemId in pack.Enumerate())
        {
            if (!survivor.Carries(itemId)) continue;
            if (_camp.Store(itemId) == 0) continue;

            survivor.RemoveFromInventory(itemId);
            recovered.Add(itemId);
        }

        return recovered;
    }

    /// <summary>Vrai si personne n'emporte quoi que ce soit : l'ecran n'a alors rien a annoncer.</summary>
    public bool IsEmpty => _packs.Values.All(pack => pack.UsedSlots == 0);

    public override string ToString()
        => IsEmpty
            ? "aucun paquetage"
            : string.Join(
                " | ",
                _packs
                    .Where(pack => pack.Value.UsedSlots > 0)
                    .Select(pack => $"{pack.Key} : {string.Join(", ", pack.Value.Stacks)}"));
}

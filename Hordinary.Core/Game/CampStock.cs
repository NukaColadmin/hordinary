using System.Text.Json;
using Hordinary.Agents;
using Hordinary.Config;

namespace Hordinary.Game;

/// <summary>
/// Ce que le camp possede et ce qu'il consomme, charge depuis <c>config/camp.json</c>
/// (specification, livrable 11).
///
/// La faim est une ressource <b>collective</b> : il n'y a pas de faim individuelle a suivre case par
/// case, ce qui garderait la simulation lisible pour rien. Le camp mange, et c'est le groupe entier
/// qui en paie le prix quand il n'y a plus rien.
/// </summary>
public sealed record CampSettings
{
    public float Food { get; init; } = 12f;
    public float Water { get; init; } = 10f;
    public float Fuel { get; init; } = 40f;
    public float Ammo { get; init; } = 18f;

    public float FoodPerSurvivorPerDay { get; init; } = 1f;
    public float WaterPerSurvivorPerDay { get; init; } = 1.5f;

    /// <summary>
    /// Plafond retire a la sante maximale d'un survivant que le camp n'a pas ravitaille — faim ou
    /// soif, meme penalite : les deux disent la meme chose, un corps qui manque de reserves.
    /// </summary>
    public float StarvationMaxHealthPenalty { get; init; } = 15f;

    /// <summary>
    /// Sous cette part de la capacite de depart, une reserve est <b>vide</b> ; au-dela de
    /// <see cref="FullThreshold"/>, elle est <b>pleine</b> ; entre les deux, elle est basse.
    ///
    /// Les trois etats sont ce qui fait exister la faim autrement que par un chiffre : le joueur voit
    /// l'etagere se vider avant de lire le compteur. Ils sont lus ici et nulle part ailleurs, pour que
    /// l'ecran ne redecide pas ce que la configuration a deja decide.
    /// </summary>
    public float EmptyThreshold { get; init; } = 0.1f;

    public float FullThreshold { get; init; } = 0.6f;

    /// <summary>
    /// Emplacements de l'entrepot du camp — celui qui recoit les recompenses de mission (livrable
    /// 16), pas les quatre reserves ci-dessus, qui se comptent et ne s'empilent pas.
    ///
    /// Il est fini pour que « le stock est plein » soit une situation reelle et non une clause de
    /// style : c'est elle qui fait exister la file d'attente des recompenses, et la promesse qu'une
    /// recompense annoncee ne disparait jamais en silence.
    /// </summary>
    public int StorageSlots { get; init; } = 24;

    /// <summary>
    /// Ce que l'entrepot contient avant la premiere sortie, par identifiant d'objet.
    ///
    /// Sans cette liste l'entrepot n'existe qu'apres une victoire : le camp ouvre les mains vides, et
    /// le paquetage — ce que le joueur confie a chacun avant le depart — n'a rien a distribuer le
    /// premier jour. C'est une donnee comme le reste : le materiel de depart se regle dans
    /// <c>config/camp.json</c>, et un camp qui ouvre demuni s'ecrit avec une liste vide.
    ///
    /// Ce qui deborde de <see cref="StorageSlots"/> est ignore au chargement : un entrepot ne peut
    /// pas commencer plus plein qu'il n'est grand.
    /// </summary>
    public List<ItemRequirement> Storage { get; init; } = [];

    public static CampSettings Default { get; } = new();

    public static CampSettings FromJson(string json)
        => JsonSerializer.Deserialize<CampConfig>(json, GameConfig.JsonOptions)?.Camp ?? Default;
}

/// <summary>Les trois etats d'une reserve, tels que l'ecran de preparation les montre.</summary>
public enum StockLevel
{
    Empty = 0,
    Low,
    Full,
}

/// <summary>Enveloppe de <c>config/camp.json</c>, dont la seule section est <c>camp</c>.</summary>
public sealed record CampConfig
{
    public CampSettings Camp { get; init; } = new();
}

/// <summary>Ce qu'une journee a coute au camp, et ce qu'elle a coute a chacun.</summary>
/// <param name="Day">Numero de la journee resolue, a partir de 1.</param>
/// <param name="Mouths">Survivants vivants a nourrir.</param>
/// <param name="FoodEaten">Nourriture reellement consommee — au plus ce qu'il y avait.</param>
/// <param name="WaterDrunk">Eau reellement consommee.</param>
/// <param name="Hungry">Vrai si le camp n'a pas pu nourrir tout le monde.</param>
/// <param name="Thirsty">Vrai si le camp n'a pas pu desalterer tout le monde.</param>
/// <param name="StarvingDays">Journees consecutives de disette, celle-ci comprise.</param>
public sealed record CampDay(
    int Day,
    int Mouths,
    float FoodEaten,
    float WaterDrunk,
    bool Hungry,
    bool Thirsty,
    int StarvingDays)
{
    public bool Fed => !Hungry && !Thirsty;

    public override string ToString()
    {
        if (Fed) return $"Jour {Day} — {Mouths} bouches, {FoodEaten:0.#} nourriture, {WaterDrunk:0.#} eau.";

        string manque = (Hungry, Thirsty) switch
        {
            (true, true) => "la faim et la soif",
            (true, false) => "la faim",
            _ => "la soif",
        };

        return $"Jour {Day} — {Mouths} bouches. Le camp subit {manque} "
             + $"({StarvingDays} jour(s) de disette).";
    }
}

/// <summary>
/// Le stock du camp entre deux missions. C'est ce qui transforme « une mission, une caisse de
/// medicaments » en boucle de survie : ce que le groupe rapporte se consomme, et ce qu'il consomme
/// decide de la mission suivante.
///
/// Il ne connait ni carte ni <c>WorldState</c> : une journee se resout hors simulation, sur des
/// nombres et une liste de survivants.
/// </summary>
public sealed class CampStock(CampSettings? settings = null)
{
    public CampSettings Settings { get; } = settings ?? CampSettings.Default;

    public float Food { get; private set; } = (settings ?? CampSettings.Default).Food;
    public float Water { get; private set; } = (settings ?? CampSettings.Default).Water;
    public float Fuel { get; private set; } = (settings ?? CampSettings.Default).Fuel;
    public float Ammo { get; private set; } = (settings ?? CampSettings.Default).Ammo;

    /// <summary>Journees resolues depuis le debut de la partie.</summary>
    public int Day { get; private set; }

    /// <summary>Journees consecutives ou le camp n'a pas pu nourrir ou desalterer tout le monde.</summary>
    public int StarvingDays { get; private set; }

    /// <summary>Ce que chaque journee a coute, pour le debrief et l'ecran de preparation.</summary>
    private readonly List<CampDay> _history = [];

    public IReadOnlyList<CampDay> History => _history;

    // --- L'entrepot et les recompenses (livrable 16) ----------------------------------------------

    /// <summary>Ce que le camp entrepose, par identifiant d'objet. Une entree par exemplaire.</summary>
    private readonly List<string> _stored = [.. InitialStorage(settings ?? CampSettings.Default)];

    /// <summary>Ce qui a ete gagne mais que l'entrepot n'a pas pu prendre.</summary>
    private readonly List<MissionReward> _pending = [];

    public IReadOnlyList<string> Stored => _stored;

    /// <summary>
    /// La file des recompenses a recuperer. Une recompense qui ne rentre pas y attend, visible dans
    /// l'ecran de camp : elle n'est <b>jamais</b> supprimee. Le joueur a choisi sa mission sur ce
    /// qu'elle promettait, et un entrepot plein est un probleme de rangement, pas une annulation.
    /// </summary>
    public IReadOnlyList<MissionReward> Pending => _pending;

    public bool StorageFull => _stored.Count >= Settings.StorageSlots;

    public int FreeSlots => Math.Max(0, Settings.StorageSlots - _stored.Count);

    /// <summary>Combien d'exemplaires de cet objet le camp possede.</summary>
    public int CountOf(string itemId)
        => _stored.Count(id => id.Equals(itemId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Range une recompense. Ce qui rentre est range, ce qui deborde passe en file d'attente — et le
    /// total des deux est toujours exactement ce que l'offre annoncait.
    /// </summary>
    public void Store(MissionReward reward)
    {
        ArgumentNullException.ThrowIfNull(reward);

        int taken = Store(reward.Item, reward.Quantity);

        int left = reward.Quantity - taken;
        if (left > 0) _pending.Add(reward with { Quantity = left });
    }

    /// <summary>
    /// Range des exemplaires d'un objet, autant que l'entrepot en accepte.
    ///
    /// Cette forme-la ne met rien en file d'attente, et c'est voulu : la file est la promesse d'une
    /// <b>recompense</b> annoncee avant le depart, pas un second entrepot. Ce qui rentre du sac d'un
    /// survivant rentre, ou ne rentre pas — au retour ce n'est pas une promesse, c'est un probleme de
    /// rangement, et l'appelant le dit au journal.
    /// </summary>
    /// <returns>Le nombre d'exemplaires reellement ranges.</returns>
    public int Store(string itemId, int quantity = 1)
    {
        int taken = Math.Min(Math.Max(0, quantity), FreeSlots);

        for (int i = 0; i < taken; i++) _stored.Add(itemId);

        return taken;
    }

    /// <summary>Le contenu de depart de l'entrepot, borne par le nombre d'emplacements.</summary>
    private static IEnumerable<string> InitialStorage(CampSettings settings)
        => settings.Storage
            .SelectMany(line => Enumerable.Repeat(line.Item, Math.Max(0, line.Quantity)))
            .Take(Math.Max(0, settings.StorageSlots));

    /// <summary>
    /// Vide la file dans l'entrepot, autant que la place le permet. C'est le geste de l'ecran de
    /// camp : on a fait de la place, on va chercher ce qui attendait.
    /// </summary>
    /// <returns>Ce qui a reellement ete range.</returns>
    public IReadOnlyList<MissionReward> ClaimPending()
    {
        var claimed = new List<MissionReward>();

        for (int index = 0; index < _pending.Count && FreeSlots > 0; index++)
        {
            var reward = _pending[index];
            int taken = Math.Min(reward.Quantity, FreeSlots);

            for (int i = 0; i < taken; i++) _stored.Add(reward.Item);
            claimed.Add(reward with { Quantity = taken });

            if (taken == reward.Quantity)
            {
                _pending.RemoveAt(index--);
                continue;
            }

            _pending[index] = reward with { Quantity = reward.Quantity - taken };
        }

        return claimed;
    }

    /// <summary>Retire un exemplaire entrepose — l'equiper avant de partir, par exemple.</summary>
    public bool Withdraw(string itemId)
    {
        int index = _stored.FindIndex(id => id.Equals(itemId, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return false;

        _stored.RemoveAt(index);
        return true;
    }

    // --- Les quatre reserves ----------------------------------------------------------------------

    public void Add(string resource, float amount)
    {
        switch (resource.ToLowerInvariant())
        {
            case "food": Food = Math.Max(0f, Food + amount); break;
            case "water": Water = Math.Max(0f, Water + amount); break;
            case "fuel": Fuel = Math.Max(0f, Fuel + amount); break;
            case "ammo": Ammo = Math.Max(0f, Ammo + amount); break;
        }
    }

    public float Of(string resource) => resource.ToLowerInvariant() switch
    {
        "food" => Food,
        "water" => Water,
        "fuel" => Fuel,
        "ammo" => Ammo,
        _ => 0f,
    };

    /// <summary>La capacite de depart d'une reserve, celle que <c>config/camp.json</c> declare.</summary>
    public float CapacityOf(string resource) => resource.ToLowerInvariant() switch
    {
        "food" => Settings.Food,
        "water" => Settings.Water,
        "fuel" => Settings.Fuel,
        "ammo" => Settings.Ammo,
        _ => 0f,
    };

    /// <summary>
    /// L'etat d'une reserve, en trois paliers. C'est ce que l'ecran de preparation affiche : trois
    /// etats se lisent d'un coup d'oeil, un nombre demande d'etre compare a quelque chose.
    /// </summary>
    public StockLevel LevelOf(string resource)
    {
        float capacity = CapacityOf(resource);
        if (capacity <= 0f) return Of(resource) > 0f ? StockLevel.Full : StockLevel.Empty;

        float ratio = Of(resource) / capacity;

        if (ratio < Settings.EmptyThreshold) return StockLevel.Empty;
        return ratio >= Settings.FullThreshold ? StockLevel.Full : StockLevel.Low;
    }

    /// <summary>Les quatre reserves, dans l'ordre ou elles se lisent.</summary>
    public static IReadOnlyList<string> Resources { get; } = ["food", "water", "fuel", "ammo"];

    /// <summary>
    /// La reserve qu'un effet alimente, ou <c>null</c> s'il ne rentre rien au camp. C'est le seul
    /// endroit qui traduise un verbe d'effet en nom de reserve, et il est public parce que la question
    /// se pose deux fois : quand un survivant vide son sac, et quand l'ecran cherche quelle vignette
    /// mettre en face du compteur de nourriture.
    /// </summary>
    public static string? ResourceOf(World.ItemEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        return effect.Verb switch
        {
            World.EffectVerb.Feed when effect.Target is "camp" => "food",
            World.EffectVerb.Drink when effect.Target is "camp" => "water",
            World.EffectVerb.Refuel when effect.Target is "camp" => "fuel",
            World.EffectVerb.Modify when effect.Target is "camp_ammo" => "ammo",
            _ => null,
        };
    }

    /// <summary>
    /// L'objet qui remplit une reserve — la conserve pour la nourriture, le jerrican pour l'essence.
    /// Aucun code ne nomme ces objets : on cherche dans le catalogue celui dont un effet alimente la
    /// reserve, et changer d'objet nourricier est une modification de <c>config/items.json</c>.
    /// </summary>
    public static World.ItemDefinition? StockerOf(string resource, World.ItemCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        return catalogue.Items.FirstOrDefault(
            item => item.Effects.Any(effect => string.Equals(ResourceOf(effect), resource, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Rentre au camp ce qu'un survivant rapporte. Le catalogue decide de ce que chaque objet vaut :
    /// une conserve fait une ration, un jerrican vingt d'essence.
    ///
    /// Ce qui n'alimente aucune reserve — une arme, une trousse, une lampe — va a l'entrepot. Il y
    /// allait nulle part avant que le joueur puisse confier du materiel au depart : le survivant est
    /// recree a chaque mission, et un fusil pret par le camp disparaissait avec l'objet qui le
    /// portait. Preter n'est pas donner.
    /// </summary>
    /// <returns>Ce que l'entrepot n'a pas pu prendre, faute de place. Vide le plus souvent.</returns>
    public IReadOnlyList<string> Deposit(Survivor survivor, World.ItemCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(survivor);
        ArgumentNullException.ThrowIfNull(catalogue);

        var refused = new List<string>();

        foreach (string itemId in survivor.Inventory.Enumerate().ToList())
        {
            if (catalogue.Find(itemId) is not { } item) continue;

            bool banked = false;

            foreach (var effect in item.Effects)
            {
                if (ResourceOf(effect) is not { } resource) continue;

                Add(resource, effect.Amount);
                banked = true;
            }

            if (banked)
            {
                survivor.RemoveFromInventory(itemId);
                continue;
            }

            if (Store(itemId) == 0)
            {
                refused.Add(itemId);
                continue;
            }

            survivor.RemoveFromInventory(itemId);
        }

        return refused;
    }

    /// <summary>
    /// Resout une journee — une mission, ou une expedition. Le camp consomme, et ce qui manque
    /// plafonne la sante de ceux qui restent.
    /// </summary>
    public CampDay ResolveDay(IReadOnlyList<Survivor> survivors)
    {
        ArgumentNullException.ThrowIfNull(survivors);

        var mouths = survivors.Where(survivor => survivor.Alive).ToList();
        Day++;

        float wantedFood = Settings.FoodPerSurvivorPerDay * mouths.Count;
        float wantedWater = Settings.WaterPerSurvivorPerDay * mouths.Count;

        float eaten = Math.Min(Food, wantedFood);
        float drunk = Math.Min(Water, wantedWater);

        Food -= eaten;
        Water -= drunk;

        bool hungry = eaten < wantedFood;
        bool thirsty = drunk < wantedWater;

        StarvingDays = hungry || thirsty ? StarvingDays + 1 : 0;

        foreach (var survivor in mouths)
        {
            if (hungry || thirsty) survivor.CapMaxHealth(Settings.StarvationMaxHealthPenalty);
        }

        var day = new CampDay(Day, mouths.Count, eaten, drunk, hungry, thirsty, StarvingDays);
        _history.Add(day);
        return day;
    }

    /// <summary>
    /// Combien de journees pleines la reserve tient encore a <paramref name="mouths"/> bouches, a la
    /// ration de <see cref="ResolveDay"/> ; <c>null</c> pour une reserve qui ne se mange ni ne se
    /// boit. Les paliers de <see cref="LevelOf"/> disent si une reserve est basse, pas quand elle
    /// sera vide — et c'est la seconde question qui decide de la sortie du lendemain.
    /// </summary>
    public int? DaysOf(string resource, int mouths)
    {
        float ration = resource.ToLowerInvariant() switch
        {
            "food" => Settings.FoodPerSurvivorPerDay,
            "water" => Settings.WaterPerSurvivorPerDay,
            _ => 0f,
        };

        float need = ration * mouths;
        if (need <= 0f) return null;

        return (int)Math.Floor(Of(resource) / need);
    }

    public override string ToString()
        => $"nourriture {Food:0.#}, eau {Water:0.#}, essence {Fuel:0.#}, munitions {Ammo:0}";
}

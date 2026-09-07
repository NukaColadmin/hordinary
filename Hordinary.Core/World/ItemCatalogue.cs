using System.Text.Json;
using System.Text.Json.Serialization;
using Hordinary.AI.Planning;
using Hordinary.Agents;
using Hordinary.Config;

namespace Hordinary.World;

/// <summary>
/// Categorie d'un objet. C'est le seul vocabulaire d'objet que le code connaisse : il n'existe
/// volontairement ni <c>Medikit</c>, ni <c>Shotgun</c>, ni <c>Crowbar</c> en C#. Une conserve et un
/// revolver sont deux entrees de <c>config/items.json</c> et <c>config/weapons.json</c>, distinguees
/// par cette categorie et par leurs effets — pas par leur classe.
/// </summary>
public enum ItemCategory
{
    Consumable = 0,
    Weapon,
    Tool,
    Heavy,
}

/// <summary>
/// Les sept verbes d'effet primitifs, et rien de plus (specification, livrable 11). Ils sont a
/// l'objet ce que <see cref="AI.Planning.ActionVerb"/> est a l'action : le socle sur lequel le
/// catalogue se decrit sans que le moteur ait a connaitre un seul identifiant.
/// </summary>
public enum EffectVerb
{
    /// <summary>Rendre des points de vie.</summary>
    Heal = 0,

    /// <summary>Nourrir : une ration au stock du camp.</summary>
    Feed,

    /// <summary>Desalterer.</summary>
    Drink,

    /// <summary>Remplir le stock d'essence du camp.</summary>
    Refuel,

    /// <summary>Multiplier ou decaler une caracteristique existante, tant que l'objet est equipe.</summary>
    Modify,

    /// <summary>Consommer l'objet : il quitte l'inventaire.</summary>
    Consume,

    /// <summary>Faire du bruit a l'endroit vise.</summary>
    EmitNoise,
}

/// <summary>
/// Lit un <see cref="EffectVerb"/> ecrit en snake_case. Le convertisseur d'enumeration de
/// <c>System.Text.Json</c> compare au nom du membre : <c>emit_noise</c> ne trouverait donc pas
/// <c>EmitNoise</c>. Plutot que d'ecrire <c>emitNoise</c> dans le fichier — les autres verbes du
/// projet, ceux du planificateur, sont en un seul mot et ne posaient pas la question — on retire les
/// soulignements avant de comparer, et <c>config/items.json</c> reste ecrit comme il se lit.
/// </summary>
public sealed class EffectVerbConverter : JsonConverter<EffectVerb>
{
    public override EffectVerb Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        string raw = reader.GetString() ?? string.Empty;
        string compact = raw.Replace("_", string.Empty);

        if (Enum.TryParse<EffectVerb>(compact, ignoreCase: true, out var verb)) return verb;

        throw new JsonException(
            $"Verbe d'effet inconnu : « {raw} ». Verbes connus : "
          + $"{string.Join(", ", Enum.GetNames<EffectVerb>().Select(ToSnake).Order())}.");
    }

    public override void Write(Utf8JsonWriter writer, EffectVerb value, JsonSerializerOptions options)
        => writer.WriteStringValue(ToSnake(value.ToString()));

    private static string ToSnake(string name)
        => string.Concat(name.Select((c, i) => char.IsUpper(c) && i > 0 ? $"_{char.ToLowerInvariant(c)}" : $"{char.ToLowerInvariant(c)}"));
}

/// <summary>Ce qu'un <see cref="Modify"/> fait de la valeur qu'il touche.</summary>
public enum ModifyMode
{
    /// <summary>Multiplie — le cas courant : <c>vision_range x1.4</c>.</summary>
    Multiply = 0,

    /// <summary>Decale : ajoute la quantite a la valeur existante.</summary>
    Offset,
}

/// <summary>
/// Un effet primitif d'objet. La combinaison verbe + cible remplace ce qui serait sinon une methode
/// par objet : « la lampe torche allonge la vue » s'ecrit
/// <c>{ "verb": "modify", "target": "vision_range", "amount": 1.4 }</c>.
/// </summary>
public sealed record ItemEffect
{
    [JsonConverter(typeof(EffectVerbConverter))]
    public EffectVerb Verb { get; init; }

    /// <summary>
    /// Sur quoi porte l'effet : <c>self</c>, <c>ally</c>, <c>camp</c>, ou le nom d'une
    /// caracteristique pour un <see cref="EffectVerb.Modify"/>.
    ///
    /// Une cible qui contient un point designe une action du domaine de planification et le champ
    /// qu'elle touche — <c>ForceDoor.cost</c>, <c>ForceDoor.noise</c>. C'est ce qui permet a un
    /// pied-de-biche de rendre l'effraction moins chere par le meme mecanisme qui fait de Bob un
    /// bricoleur (voir <see cref="ActionModifier"/>), sans qu'aucun code ne teste ce qu'on porte.
    /// </summary>
    public string Target { get; init; } = "self";

    public float Amount { get; init; }

    public ModifyMode Mode { get; init; } = ModifyMode.Multiply;

    /// <summary>Duree en secondes. Absente, l'effet est permanent tant que l'objet est equipe.</summary>
    public float? Duration { get; init; }

    /// <summary>Les trois seuls champs qu'une action porte, et donc qu'un objet puisse ponderer.</summary>
    private static readonly string[] ActionFields = ["cost", "noise", "duration"];

    /// <summary>Nom de l'action visee, pour une cible de la forme <c>Action.champ</c>.</summary>
    public string? ActionName => Split()?.Action;

    /// <summary>Champ vise sur l'action : <c>cost</c>, <c>noise</c> ou <c>duration</c>.</summary>
    public string? ActionField => Split()?.Field;

    /// <summary>
    /// Une cible n'est une action que si son champ en est reellement un. Sans cette verification,
    /// <c>knowledge.communication_range</c> passerait pour une ponderation de l'action
    /// « knowledge » et cesserait d'etre lue comme la caracteristique qu'elle est.
    /// </summary>
    private (string Action, string Field)? Split()
    {
        if (Verb != EffectVerb.Modify) return null;

        int dot = Target.IndexOf('.');
        if (dot <= 0 || dot == Target.Length - 1) return null;

        string field = Target[(dot + 1)..];
        if (!ActionFields.Contains(field, StringComparer.OrdinalIgnoreCase)) return null;

        return (Target[..dot], field);
    }

    public override string ToString()
        => $"{Verb} {Target} {(Mode == ModifyMode.Multiply ? "x" : "+")}{Amount:0.##}";
}

/// <summary>
/// Une entree du catalogue : un objet, ou une arme, qui est un objet de categorie
/// <see cref="ItemCategory.Weapon"/> avec quelques champs de plus.
///
/// Aucune de ces entrees n'existe en C#. Ajouter une conserve, retirer une arme ou doubler les
/// degats d'une batte est une modification de <c>config/items.json</c> ou <c>config/weapons.json</c>,
/// jamais une recompilation.
/// </summary>
public sealed record ItemDefinition
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public ItemCategory Category { get; init; } = ItemCategory.Consumable;

    /// <summary>Nom de la tuile qui le represente au sol, tel que declare dans <c>tile_legend.json</c>.</summary>
    public string Sprite { get; init; } = string.Empty;

    public float Weight { get; init; } = 1f;

    public bool Stackable { get; init; }

    public int MaxStack { get; init; } = 1;

    public List<ItemEffect> Effects { get; init; } = [];

    /// <summary>Bruit de l'utilisation, au bareme de la section 18.1.</summary>
    public float NoiseLevel { get; init; }

    // --- Armes ----------------------------------------------------------------------------------
    // Une arme est un objet comme un autre : ces champs sont simplement absents des dix objets.

    public float Damage { get; init; }

    /// <summary>Portee en cellules. 1 = corps a corps.</summary>
    public int Range { get; init; } = 1;

    public float AttackDuration { get; init; } = 1f;

    /// <summary>Nombre de coups avant que l'arme ne soit hors d'usage. 0 = indestructible.</summary>
    public int Durability { get; init; }

    /// <summary>Munitions par chargeur. Absent, c'est une arme de corps a corps.</summary>
    public int? Ammo { get; init; }

    /// <summary>
    /// Degats d'une arme a feu sans munitions, utilisee comme une massue. C'est ce qui rend
    /// <c>ammo_box</c> interessante sans la rendre indispensable : un revolver vide reste une arme,
    /// simplement une mauvaise.
    /// </summary>
    public float UnloadedDamage { get; init; } = 10f;

    /// <summary>Bruit d'une arme a feu sans munitions : celui d'un coup porte, pas d'une detonation.</summary>
    public float UnloadedNoise { get; init; } = 3f;

    public bool IsWeapon => Category == ItemCategory.Weapon;

    /// <summary>Une arme a feu : elle consomme des munitions et retombe sur ses valeurs de poing sans.</summary>
    public bool IsFirearm => IsWeapon && Ammo is > 0;

    /// <summary>
    /// Les modificateurs d'action que l'objet applique quand il est equipe, dans le vocabulaire des
    /// competences de survivant. Un pied-de-biche equipe rend <c>ForceDoor</c> moins cher exactement
    /// comme une competence de <c>config/survivors.json</c> le ferait.
    /// </summary>
    public IReadOnlyDictionary<string, ActionModifier> ActionModifiers => _modifiers ??= BuildModifiers();

    private Dictionary<string, ActionModifier>? _modifiers;

    private Dictionary<string, ActionModifier> BuildModifiers()
    {
        var byAction = new Dictionary<string, (float Cost, float Noise, float Duration)>(StringComparer.OrdinalIgnoreCase);

        foreach (var effect in Effects)
        {
            if (effect.ActionName is not { } action || effect.ActionField is not { } field) continue;

            var current = byAction.TryGetValue(action, out var existing) ? existing : (1f, 1f, 1f);

            byAction[action] = field.ToLowerInvariant() switch
            {
                "cost" => (current.Item1 * effect.Amount, current.Item2, current.Item3),
                "noise" => (current.Item1, current.Item2 * effect.Amount, current.Item3),
                "duration" => (current.Item1, current.Item2, current.Item3 * effect.Amount),
                _ => current,
            };
        }

        return byAction.ToDictionary(
            pair => pair.Key,
            pair => new ActionModifier { Cost = pair.Value.Cost, Noise = pair.Value.Noise, Duration = pair.Value.Duration },
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Multiplicateur applique a une caracteristique du survivant par l'objet equipe — <c>vision_range</c>,
    /// <c>knowledge.communication_range</c>. Renvoie 1 quand l'objet ne la touche pas.
    /// </summary>
    public float StatMultiplier(string stat)
    {
        float factor = 1f;

        foreach (var effect in Effects)
        {
            if (effect.Verb != EffectVerb.Modify) continue;
            if (effect.ActionName is not null) continue;
            if (!effect.Target.Equals(stat, StringComparison.OrdinalIgnoreCase)) continue;
            if (effect.Mode != ModifyMode.Multiply) continue;

            factor *= effect.Amount;
        }

        return factor;
    }

    /// <summary>Premiere quantite portee par un verbe donne, ou <c>null</c> si l'objet ne le porte pas.</summary>
    public float? AmountOf(EffectVerb verb)
        => Effects.FirstOrDefault(effect => effect.Verb == verb)?.Amount;

    public override string ToString() => $"{Name} [{Id}] ({Category})";
}

/// <summary>
/// Les objets et les armes livres, charges depuis <c>config/items.json</c> et
/// <c>config/weapons.json</c>. Les deux fichiers ont exactement le meme format : c'est le lecteur
/// humain qu'ils separent, pas le code.
///
/// Comme <see cref="Game.MissionCatalogue"/>, le catalogue se valide au chargement : un identifiant
/// vide, un doublon ou une arme sans degats doivent se voir tout de suite.
/// </summary>
public sealed class ItemCatalogue
{
    public List<ItemDefinition> Items { get; init; } = [];

    [JsonIgnore]
    private Dictionary<string, ItemDefinition>? _byId;

    public static ItemCatalogue Empty { get; } = new();

    public ItemDefinition? Find(string? id)
    {
        if (id is null) return null;

        _byId ??= Items.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        return _byId.TryGetValue(id, out var found) ? found : null;
    }

    public bool Contains(string? id) => Find(id) is not null;

    /// <summary>Poids d'un objet. Un identifiant inconnu ne pese rien plutot que de faire echouer un calcul.</summary>
    public float WeightOf(string? id) => Find(id)?.Weight ?? 0f;

    public IEnumerable<ItemDefinition> OfCategory(ItemCategory category)
        => Items.Where(item => item.Category == category);

    public static ItemCatalogue FromJson(string json)
    {
        var catalogue = JsonSerializer.Deserialize<ItemCatalogue>(json, GameConfig.JsonOptions) ?? Empty;
        catalogue.Validate();
        return catalogue;
    }

    /// <summary>
    /// Les deux fichiers reunis. Objets et armes partagent un espace d'identifiants unique : un
    /// inventaire ne connait qu'un <c>id</c>, jamais le fichier dont il vient.
    /// </summary>
    public static ItemCatalogue Merge(params ItemCatalogue[] catalogues)
    {
        var merged = new ItemCatalogue { Items = [.. catalogues.SelectMany(catalogue => catalogue.Items)] };
        merged.Validate();
        return merged;
    }

    public void Validate()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in Items)
        {
            if (string.IsNullOrWhiteSpace(item.Id))
                throw new InvalidOperationException("Un objet sans identifiant a ete declare.");

            if (!seen.Add(item.Id))
                throw new InvalidOperationException($"L'objet « {item.Id} » est declare deux fois.");

            if (string.IsNullOrWhiteSpace(item.Name))
                throw new InvalidOperationException($"L'objet « {item.Id} » n'a pas de nom.");

            if (item.Weight < 0f)
                throw new InvalidOperationException($"L'objet « {item.Id} » a un poids negatif.");

            if (item.MaxStack < 1)
                throw new InvalidOperationException($"L'objet « {item.Id} » a une pile de taille nulle.");

            if (!item.Stackable && item.MaxStack != 1)
            {
                throw new InvalidOperationException(
                    $"L'objet « {item.Id} » n'est pas empilable mais declare une pile de {item.MaxStack}.");
            }

            if (item.IsWeapon && item.Damage <= 0f)
                throw new InvalidOperationException($"L'arme « {item.Id} » n'inflige aucun degat.");

            if (item.IsWeapon && item.Range < 1)
                throw new InvalidOperationException($"L'arme « {item.Id} » a une portee nulle.");

            if (!item.IsWeapon && item.Ammo is not null)
                throw new InvalidOperationException($"L'objet « {item.Id} » n'est pas une arme mais declare des munitions.");
        }
    }
}

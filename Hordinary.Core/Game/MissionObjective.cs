using System.Text.Json.Serialization;
using Hordinary.World;

namespace Hordinary.Game;

/// <summary>
/// Ce qu'une mission demande de faire (livrable 16). Trois formes, et pas une de plus.
///
/// C'est la seule enumeration que ce livrable ajoute au coeur, et elle porte une decision : un type
/// d'objectif n'est pas une donnee de configuration parce que chacun demande une <b>mesure</b>
/// differente — compter des morts dans un rectangle, compter des objets dans des sacs, compter des
/// rations rentrees au camp. Trois compteurs, trois branches, ecrites une fois dans
/// <see cref="MissionObjectiveTracker"/>. Tout le reste — quelle zone, quels objets, combien, quelle
/// recompense — reste de la donnee.
/// </summary>
public enum MissionObjectiveKind
{
    /// <summary>Tuer un nombre de zombies dans la zone cible.</summary>
    KillZombies = 0,

    /// <summary>Rapporter des objets nommes, ramasses dans la zone cible.</summary>
    CollectObjects,

    /// <summary>Rapporter des ressources vitales : nourriture, eau, essence.</summary>
    CollectPriorityResources,
}

/// <summary>Une ligne d'exigence : cet objet-la, en cette quantite.</summary>
public sealed record ItemRequirement(string Item, int Quantity)
{
    public override string ToString() => Quantity > 1 ? $"{Item} x{Quantity}" : Item;
}

/// <summary>Une ligne d'exigence de ressource : <c>food</c>, <c>water</c> ou <c>fuel</c>.</summary>
public sealed record ResourceRequirement(string Resource, int Quantity)
{
    public override string ToString() => Quantity > 1 ? $"{Resource} x{Quantity}" : Resource;
}

/// <summary>
/// Ce qu'il faut atteindre pour remplir une mission generee. Les trois champs sont exclusifs : le
/// <see cref="MissionObjectiveKind"/> de la mission dit lequel est lu, et les deux autres restent
/// vides.
///
/// Ils ne sont pas fondus en un seul champ polymorphe parce que le fichier doit rester lisible : un
/// joueur qui ouvre une offre exportee doit voir <c>"zombies": 8</c> ou
/// <c>"items": [...]</c>, pas une union etiquetee.
/// </summary>
public sealed record MissionRequirements
{
    /// <summary>Nombre de zombies a abattre dans la zone cible.</summary>
    public int Zombies { get; init; }

    public List<ItemRequirement> Items { get; init; } = [];

    public List<ResourceRequirement> Resources { get; init; } = [];

    /// <summary>
    /// Les trois ressources vitales, dans l'ordre ou elles se lisent. Elles sont ici et non dans
    /// <see cref="CampStock.Resources"/> parce que les munitions, quatrieme reserve du camp, ne sont
    /// pas une ressource <b>prioritaire</b> : on ne monte pas une expedition pour elles.
    /// </summary>
    public static IReadOnlyList<string> PriorityResources { get; } = ["food", "water", "fuel"];

    public static MissionRequirements None { get; } = new();

    /// <summary>
    /// Ce qu'il faut poser au sol pour que la mission soit realisable, par identifiant d'objet.
    ///
    /// Une ressource n'a pas d'identifiant a elle : c'est le catalogue qui dit quel objet remplit
    /// quelle reserve (<see cref="CampStock.StockerOf"/>), et c'est pourquoi cette methode demande
    /// un catalogue au lieu de nommer une conserve. Une mission d'elimination ne pose rien.
    /// </summary>
    public IReadOnlyList<ItemRequirement> Payload(MissionObjectiveKind kind, ItemCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        return kind switch
        {
            MissionObjectiveKind.CollectObjects => Items,

            MissionObjectiveKind.CollectPriorityResources =>
            [
                .. Resources
                    .Select(line => (Stocker: CampStock.StockerOf(line.Resource, catalogue), line.Quantity))
                    .Where(pair => pair.Stocker is not null)
                    .Select(pair => new ItemRequirement(pair.Stocker!.Id, pair.Quantity)),
            ],

            _ => [],
        };
    }

    public override string ToString() => (Zombies, Items.Count, Resources.Count) switch
    {
        ( > 0, _, _) => $"{Zombies} zombies",
        (_, > 0, _) => string.Join(", ", Items),
        (_, _, > 0) => string.Join(", ", Resources),
        _ => "aucune exigence",
    };
}

/// <summary>
/// Les trois categories de recompense (livrable 16). Comme pour les objectifs, la categorie est du
/// code et le contenu est de la donnee : quelle arme, quel objet rare, en quelle quantite se
/// declarent dans <c>config/game_config.json</c>.
/// </summary>
public enum RewardCategory
{
    Weapon = 0,
    Medikit,
    Rare,
}

/// <summary>
/// Une recompense annoncee avant le depart et attribuee apres la victoire.
///
/// Elle nomme un objet et une quantite, jamais « une recompense aleatoire » : le joueur choisit son
/// expedition sur ce qu'elle rapporte, et une promesse floue ne se compare pas a une autre.
/// </summary>
public sealed record MissionReward(RewardCategory Category, string Item, int Quantity)
{
    public override string ToString() => Quantity > 1 ? $"{Item} x{Quantity}" : Item;
}

/// <summary>
/// Les trois etiquettes de difficulte. Elles ne sont pas qu'un mot : le temps imparti, l'equipe
/// conseillee, le cout d'entree en essence et la table de recompenses se lisent toutes par elles.
/// C'est ce qui empeche l'etiquette et le contenu de diverger, defaut que la carte livree avait
/// avant ce livrable et que <c>MissionCatalogueTests</c> devait surveiller a la main.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<MissionDifficulty>))]
public enum MissionDifficulty
{
    Easy = 0,
    Medium,
    Hard,
}

/// <summary>Comment une difficulte se dit au joueur. L'interface n'en decide pas seule.</summary>
public static class MissionDifficultyNames
{
    public static string Of(MissionDifficulty difficulty) => difficulty switch
    {
        MissionDifficulty.Easy => "Facile",
        MissionDifficulty.Medium => "Moyenne",
        _ => "Difficile",
    };

    /// <summary>L'etiquette lue a l'envers, pour une mission ecrite a la main qui l'ecrit en clair.</summary>
    public static MissionDifficulty? Parse(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;

        foreach (var difficulty in Enum.GetValues<MissionDifficulty>())
        {
            if (Of(difficulty).Equals(label, StringComparison.OrdinalIgnoreCase)) return difficulty;
            if (difficulty.ToString().Equals(label, StringComparison.OrdinalIgnoreCase)) return difficulty;
        }

        return null;
    }
}

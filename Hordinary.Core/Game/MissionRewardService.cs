using Hordinary.World;

namespace Hordinary.Game;

/// <summary>
/// Le grand livre d'une mission : ce qu'elle coute a l'aller, ce qu'elle rapporte au retour.
///
/// Les deux tiennent dans la meme classe parce qu'ils sont la meme garantie, prise deux fois : un
/// mouvement de stock ne doit se produire <b>qu'une fois</b>. L'essence est debitee au lancement et
/// pas a chaque clic sur l'offre ; les recompenses sont versees au verdict de victoire et pas a
/// chaque passe qui le relit — or <see cref="MissionTracker.Evaluate"/> est appele a chaque frame,
/// et un verdict deja rendu y repasse indefiniment. Deux drapeaux, un seul endroit ou les tenir.
///
/// Le service est cree <b>par mission</b>, et non une fois pour la partie : c'est ce qui rend
/// « une seule fois » verifiable sans avoir a se souvenir de ce qui a ete joue les jours precedents.
/// Rejouer la meme mission un autre jour la repaie, ce qui est le comportement voulu.
/// </summary>
public sealed class MissionRewardService
{
    /// <summary>La reserve du camp qui paie l'entree. Nommee ici, et nulle part ailleurs.</summary>
    public const string FuelResource = "fuel";

    private readonly CampStock _camp;
    private readonly ItemCatalogue _catalogue;

    public MissionRewardService(CampStock camp, MissionDefinition mission, ItemCatalogue catalogue)
    {
        _camp = camp ?? throw new ArgumentNullException(nameof(camp));
        _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
        Mission = mission ?? throw new ArgumentNullException(nameof(mission));
    }

    public MissionDefinition Mission { get; }

    /// <summary>Vrai une fois le cout d'entree debite. Ne redescend jamais.</summary>
    public bool Paid { get; private set; }

    /// <summary>Vrai une fois les recompenses versees. Ne redescend jamais.</summary>
    public bool Granted { get; private set; }

    /// <summary>Ce qui a ete reellement verse, pour l'ecran de resultat.</summary>
    public IReadOnlyList<MissionReward> Awarded { get; private set; } = [];

    /// <summary>
    /// Le camp a-t-il de quoi partir ? La question se pose sur une offre qu'on n'a pas acceptee,
    /// donc sans service : c'est pourquoi elle existe aussi en version statique.
    /// </summary>
    public static bool CanAfford(CampStock camp, MissionDefinition mission)
    {
        ArgumentNullException.ThrowIfNull(camp);
        ArgumentNullException.ThrowIfNull(mission);

        return camp.Of(FuelResource) >= mission.EntryFuelCost;
    }

    public bool CanLaunch => CanAfford(_camp, Mission);

    /// <summary>
    /// Debite le cout d'entree. Refuse si le camp n'a pas de quoi, et ne debite jamais deux fois.
    ///
    /// Consulter une offre, en ouvrir le detail ou en changer ne passe pas par ici : le prix se paie
    /// au depart, et une offre qu'on repose n'a rien coute.
    /// </summary>
    /// <returns>Vrai si le groupe peut partir.</returns>
    public bool PayEntry()
    {
        if (Paid) return true;
        if (!CanLaunch) return false;

        if (Mission.EntryFuelCost > 0) _camp.Add(FuelResource, -Mission.EntryFuelCost);

        Paid = true;
        return true;
    }

    /// <summary>
    /// Verse les recompenses, une seule fois, et seulement sur une victoire. Une defaite, un abandon
    /// ou la mort du groupe ne versent rien — et l'essence de l'aller n'est pas rendue : le van a
    /// roule.
    ///
    /// Une recompense inconnue du catalogue n'est pas versee en silence : elle serait invisible dans
    /// le stock, et le joueur croirait avoir recu ce que l'offre annoncait. Le chargement la refuse
    /// deja (<see cref="MissionGenerationSettings.Validate"/>) ; ce second filtre couvre une mission
    /// ecrite a la main qui promettrait n'importe quoi.
    /// </summary>
    public IReadOnlyList<MissionReward> Grant(MissionVerdict verdict)
    {
        if (Granted || verdict.Outcome != MissionOutcome.Victory) return Awarded;

        Granted = true;

        var awarded = new List<MissionReward>();

        foreach (var reward in Mission.Rewards)
        {
            if (reward.Quantity <= 0 || _catalogue.Find(reward.Item) is null) continue;

            _camp.Store(reward);
            awarded.Add(reward);
        }

        Awarded = awarded;
        return Awarded;
    }
}

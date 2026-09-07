namespace Hordinary.World.MapGeneration;

/// <summary>Ce qu'un quartier rejete avait contre lui, pour le journal et pour les tests.</summary>
public readonly record struct RejectedDistrict(int Seed, ValidationReport Report)
{
    public override string ToString() => $"graine {Seed} rejetee — {Report}";
}

/// <summary>
/// Le generateur de quartiers (livrable 13) : six passes qui posent, une qui relit, et une graine.
///
/// Il vit dans <c>Hordinary.Core</c> et ne depend d'aucun type Godot : un quartier se genere, se
/// valide et se rejoue sans lancer le moteur, ce qui est la condition pour que les tests de ce
/// livrable existent. Ce qu'il produit, ce sont <b>exactement</b> les donnees que <c>MapLoader</c>
/// charge aujourd'hui — une <see cref="GridMap"/>, des <see cref="MapZone"/>, des portes, des
/// fenetres, des points d'apparition. Rien en aval ne sait si la carte a ete dessinee ou generee.
/// </summary>
public sealed class DistrictGenerator(MapGenConfig config)
{
    public MapGenConfig Config { get; } = config ?? throw new ArgumentNullException(nameof(config));

    /// <summary>Les quartiers refuses lors de la derniere generation, du premier au dernier.</summary>
    public IReadOnlyList<RejectedDistrict> Rejected => _rejected;

    private readonly List<RejectedDistrict> _rejected = [];

    /// <summary>
    /// Genere un quartier jouable, ou echoue en disant pourquoi.
    ///
    /// Une carte rejetee est regeneree a <c>seed + 1</c>, au maximum
    /// <see cref="MapGenConfig.MaxGenerationAttempts"/> fois. Ce n'est pas une correction : le
    /// quartier rejete est jete entier. Retoucher une carte pour la faire passer reviendrait a
    /// produire des quartiers qui satisfont le contrat sans le meriter, et a ne jamais savoir
    /// lesquels.
    /// </summary>
    public GeneratedDistrict Generate(DistrictRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var profile = Config.Find(request.Profile)
            ?? throw new InvalidOperationException(
                $"Le profil de carte « {request.Profile ?? "(aucun)"} » n'existe pas dans config/mapgen.json.");

        _rejected.Clear();

        for (int attempt = 0; attempt < Config.MaxGenerationAttempts; attempt++)
        {
            int seed = request.Seed + attempt;
            var draft = Build(profile, request, seed);
            var report = ValidationPass.Apply(draft);

            if (report.Accepted) return draft.Freeze(attempt + 1);

            _rejected.Add(new RejectedDistrict(seed, report));
        }

        throw new InvalidOperationException(
            $"Aucun quartier « {profile.Id} » jouable en {Config.MaxGenerationAttempts} essais "
          + $"depuis la graine {request.Seed}. Derniers refus : "
          + string.Join(" | ", _rejected.TakeLast(3)));
    }

    /// <summary>
    /// Les six passes, dans l'ordre, chacune consommant la sortie de la precedente. Publique parce
    /// qu'un test qui veut regarder un quartier rejete a besoin du brouillon, pas du resultat.
    /// </summary>
    public DistrictDraft Build(MapProfile profile, DistrictRequest request, int seed)
    {
        var draft = new DistrictDraft(Config, profile, request, seed);

        LayoutPass.Apply(draft);
        RoadPass.Apply(draft);
        BuildingPass.Apply(draft);
        ZonePass.Apply(draft);
        PopulatePass.Apply(draft);
        BorderPass.Apply(draft);

        return draft;
    }
}

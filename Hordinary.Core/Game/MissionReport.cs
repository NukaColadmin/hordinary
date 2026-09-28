using System.Globalization;
using Hordinary.Agents;

namespace Hordinary.Game;

/// <summary>Ou se trouvait un survivant au moment du verdict.</summary>
public enum SurvivorFate
{
    /// <summary>Au van, ou a moins de <see cref="MissionSettings.HomeRadius"/> cases.</summary>
    Home = 0,

    /// <summary>Debout, mais hors du van : une defaite qui tombe pendant que le groupe est dehors.</summary>
    Outside,

    /// <summary>Hors de combat.</summary>
    Down,
}

/// <summary>Un survivant au moment du verdict : ou il est, et dans quel etat.</summary>
public sealed record SurvivorOutcome(string Name, SurvivorFate Fate, float Health, float MaxHealth, string? Brought = null)
{
    public override string ToString()
    {
        if (Fate == SurvivorFate.Down) return Name;

        string state = string.Create(CultureInfo.InvariantCulture, $"{Name} {Health:0}/{MaxHealth:0} PV");
        return Brought is null ? state : $"{state} (rapporte {Brought})";
    }
}

/// <summary>
/// Le bilan d'une sortie, tel que l'ecran de camp l'affiche au retour.
///
/// Il est dresse en trois temps, parce que ce qu'il raconte n'existe pas au meme moment. Au
/// <b>verdict</b> d'abord (<see cref="Of"/>) : qui est ou, et dans quel etat — c'est l'instant que le
/// verdict juge, et non celui, quelques secondes plus tard, ou le camp reprend la main. Au
/// <b>reglement</b> ensuite, que <c>GameRoot</c> remplit parce que c'est lui qui vide les sacs : ce
/// qui est reellement rentre au camp, et ce que l'entrepot a refuse. A la <b>journee</b> enfin
/// (<see cref="Closing"/>) : ce que le camp a mange, et combien de temps il tient encore.
///
/// Ce qui est calcule — le pret perdu, ce qui a ete trouve, l'autonomie — l'est ici et non dans la
/// couche moteur : le bilan se teste sur une mission deroulee par <c>MissionRun</c>, et une
/// soustraction faite dans <c>GameRoot</c> serait la seule du jeu a n'avoir aucun test.
///
/// Le pret se compte <b>au groupe</b> et non au survivant. Une arme ramassee sur un camarade tombe
/// et rapportee au van est rendue, pas perdue : c'est ce qui rentre a l'entrepot qui fait foi, pas
/// qui l'avait au depart.
/// </summary>
public sealed record MissionReport
{
    public required string Mission { get; init; }

    public required MissionVerdict Verdict { get; init; }

    /// <summary>Temps imparti, en secondes ; zero ou moins pour une mission sans limite.</summary>
    public double TimeLimit { get; init; }

    /// <summary>Vrai si le van a du rappeler le groupe.</summary>
    public bool Recalled { get; init; }

    /// <summary>Le groupe, dans l'ordre d'enrolement.</summary>
    public IReadOnlyList<SurvivorOutcome> Survivors { get; init; } = [];

    /// <summary>Consignes tactiques actives pendant la sortie (livrable 15).</summary>
    public int Constraints { get; init; }

    /// <summary>Plans passes au portillon 4, tous survivants confondus.</summary>
    public int PlansVerified { get; init; }

    /// <summary>Plans que le portillon 4 a trouves non conformes : un defaut du compilateur.</summary>
    public int PlansViolated { get; init; }

    // --- Le reglement --------------------------------------------------------------------------

    /// <summary>Ce que le camp avait confie au depart, un identifiant par exemplaire.</summary>
    public IReadOnlyList<string> Loaned { get; init; } = [];

    /// <summary>Ce qui est reellement rentre au camp — entrepot ou reserves —, un par exemplaire.</summary>
    public IReadOnlyList<string> Returned { get; init; } = [];

    /// <summary>Ce que l'entrepot plein a refuse, et qui repart avec le sac de celui qui le portait.</summary>
    public IReadOnlyList<string> Left { get; init; } = [];

    /// <summary>Les recompenses versees. Vide hors victoire.</summary>
    public IReadOnlyList<MissionReward> Awarded { get; init; } = [];

    // --- La journee ----------------------------------------------------------------------------

    public CampDay? Day { get; init; }

    /// <summary>Journees de nourriture et d'eau qui restent apres la journee, a la meme ration.</summary>
    public int? FoodDays { get; init; }

    /// <inheritdoc cref="FoodDays"/>
    public int? WaterDays { get; init; }

    // --- Ce qui s'en deduit --------------------------------------------------------------------

    /// <summary>
    /// Le pret qui ne revient pas : confie au depart, ni rentre au camp ni retenu faute de place. Un
    /// bandage consomme en route y figure, et c'est juste — il ne sera plus sur l'etagere demain.
    /// </summary>
    public IReadOnlyList<string> Lost => Without(Without(Loaned, Returned), Left);

    /// <summary>Ce que la sortie a rapporte en plus de ce qu'elle avait emporte.</summary>
    public IReadOnlyList<string> Brought => Without(Returned, Loaned);

    /// <summary>
    /// Le bilan au moment du verdict, avant que les sacs ne se vident. Le reglement et la journee
    /// sont a completer ensuite.
    /// </summary>
    public static MissionReport Of(MissionTracker tracker, SurvivorBand band, CampLoadout? loadout = null)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(band);

        // Personne ne « rapporte » huit morts : une elimination n'a pas de porteur a nommer.
        bool carries = tracker.Objectives is not { Kind: MissionObjectiveKind.KillZombies };
        var courier = tracker.Verdict.Outcome == MissionOutcome.Victory && carries ? tracker.Courier : null;

        var survivors = band.Survivors
            .Select(survivor => new SurvivorOutcome(
                survivor.Name,
                !survivor.Alive ? SurvivorFate.Down
                    : tracker.IsHome(survivor) ? SurvivorFate.Home
                    : SurvivorFate.Outside,
                survivor.Health,
                survivor.MaxHealth,
                ReferenceEquals(survivor, courier) ? tracker.Mission.Item : null))
            .ToList();

        var loaned = loadout is null
            ? []
            : band.Survivors
                .SelectMany(survivor => loadout.Of(survivor.Id))
                .SelectMany(stack => Enumerable.Repeat(stack.ItemId, stack.Count))
                .ToList();

        return new MissionReport
        {
            Mission = tracker.Mission.Name,
            Verdict = tracker.Verdict,
            TimeLimit = tracker.TimeLimit,
            Recalled = tracker.Recalled,
            Survivors = survivors,
            Constraints = band.Instructions?.Active.Count ?? 0,
            PlansVerified = band.Agents.Sum(agent => agent.PlansVerified),
            PlansViolated = band.Agents.Sum(agent => agent.PlansViolated),
            Loaned = loaned,
        };
    }

    /// <summary>La journee resolue : ce qu'elle a coute, et ce qu'il reste pour les suivantes.</summary>
    public MissionReport Closing(CampDay day, CampStock camp)
    {
        ArgumentNullException.ThrowIfNull(day);
        ArgumentNullException.ThrowIfNull(camp);

        return this with
        {
            Day = day,
            FoodDays = camp.DaysOf("food", day.Mouths),
            WaterDays = camp.DaysOf("water", day.Mouths),
        };
    }

    /// <summary>
    /// Difference de deux multiensembles d'identifiants : chaque exemplaire retire n'en retire
    /// qu'un. Publique parce que <c>GameRoot</c> en a besoin pour dire ce qui est rentre — ce que le
    /// survivant portait, moins ce que l'entrepot a refuse.
    /// </summary>
    public static IReadOnlyList<string> Without(IEnumerable<string> items, IEnumerable<string> removed)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(removed);

        var owed = removed
            .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var kept = new List<string>();

        foreach (string id in items)
        {
            if (owed.TryGetValue(id, out int count) && count > 0)
            {
                owed[id] = count - 1;
                continue;
            }

            kept.Add(id);
        }

        return kept;
    }

    /// <summary>
    /// Les lignes affichees, dans l'ordre ou le joueur se pose les questions : comment ca s'est fini,
    /// qui est rentre, ce que ca a rapporte et coute, puis ce que le camp peut encore tenir. Une
    /// ligne sans objet — aucune consigne, aucun pret perdu — n'est pas ecrite.
    /// </summary>
    public IReadOnlyList<string> Lines()
    {
        var lines = new List<string> { $"SORTIE PRECEDENTE — {Mission} : {Verdict}" };

        string duration = TimeLimit > 0d ? $"Duree : {Clock(Verdict.At)} sur {Clock(TimeLimit)}" : $"Duree : {Clock(Verdict.At)}";
        lines.Add(Recalled ? $"{duration}, rappel du van." : $"{duration}.");

        var groups = new List<string>();
        AddGroup(groups, "Au van", SurvivorFate.Home);
        AddGroup(groups, "Dehors", SurvivorFate.Outside);
        AddGroup(groups, "Hors de combat", SurvivorFate.Down);
        if (groups.Count > 0) lines.Add(string.Join(" · ", groups));

        if (Brought.Count > 0) lines.Add($"Rapporte au camp : {Stacked(Brought)}");
        if (Awarded.Count > 0) lines.Add($"Recompenses versees : {string.Join(", ", Awarded)}");
        if (Lost.Count > 0) lines.Add($"Pret non rendu : {Stacked(Lost)}");
        if (Left.Count > 0) lines.Add($"Laisse faute de place a l'entrepot : {Stacked(Left)}");

        if (Constraints > 0) lines.Add(ConstraintLine());

        if (Day is { } day) lines.Add(day.ToString());

        if (FoodDays is { } food && WaterDays is { } water)
        {
            lines.Add($"Reserves : {Days(food)} de nourriture, {Days(water)} d'eau.");
        }

        return lines;
    }

    public override string ToString() => string.Join("\n", Lines());

    private void AddGroup(List<string> groups, string label, SurvivorFate fate)
    {
        var members = Survivors.Where(survivor => survivor.Fate == fate).ToList();
        if (members.Count > 0) groups.Add($"{label} : {string.Join(", ", members)}");
    }

    /// <summary>
    /// Ce que les consignes ont donne. Une violation est un defaut du compilateur et non du
    /// survivant : la ligne le dit, pour que le joueur ne la lise pas comme une desobeissance.
    /// </summary>
    private string ConstraintLine()
    {
        string head = $"Consignes : {Constraints} active(s), {PlansVerified} plan(s) verifie(s)";

        return PlansViolated == 0
            ? $"{head}, aucun ecart."
            : $"{head}, {PlansViolated} non conforme(s) — defaut du compilateur, detail au journal.";
    }

    private static string Stacked(IEnumerable<string> items)
        => string.Join(
            ", ",
            items
                .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
                .Select(group => new InventoryStack(group.Key, group.Count())));

    private static string Days(int days) => days <= 1 ? $"{days} jour" : $"{days} jours";

    /// <summary>Minutes et secondes ; une mission sans limite peut passer l'heure sans changer de format.</summary>
    private static string Clock(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0d, seconds));
        return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalMinutes}:{span.Seconds:00}");
    }
}

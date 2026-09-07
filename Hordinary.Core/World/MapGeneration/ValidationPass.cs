namespace Hordinary.World.MapGeneration;

/// <summary>Une clause du contrat de jouabilite qui n'est pas tenue, et pourquoi.</summary>
public readonly record struct ValidationFailure(string Clause, string Detail)
{
    public override string ToString() => $"{Clause} : {Detail}";
}

/// <summary>Ce que la relecture a trouve. Un rapport vide est un quartier accepte.</summary>
public sealed record ValidationReport(IReadOnlyList<ValidationFailure> Failures)
{
    public bool Accepted => Failures.Count == 0;

    public override string ToString()
        => Accepted ? "quartier accepte" : string.Join(" ; ", Failures);
}

/// <summary>
/// Passe 7 — la relecture. Le generateur relit sa propre carte et rejette celle qui ne satisfait pas
/// le contrat de jouabilite ; <see cref="DistrictGenerator"/> en regenere alors une a <c>seed + 1</c>.
///
/// Elle est separee des six passes qui posent, et cette separation est le seul point d'architecture
/// du livrable qui merite d'etre defendu : on ne teste pas un validateur qui est aussi son propre
/// producteur. Une passe qui corrigerait ce qu'elle vient de constater rendrait le contrat toujours
/// satisfait — et donc invisible le jour ou une passe le viole vraiment.
/// </summary>
public static class ValidationPass
{
    public static ValidationReport Apply(DistrictDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var failures = new List<ValidationFailure>();

        // Le chemin que le groupe peut reellement emprunter : chaque porte fermee y est ouvrable,
        // forcable, ou contournable par une fenetre. C'est la lecture que demande la premiere clause,
        // et elle sert ensuite aux deux suivantes.
        var reachable = Reach(draft);

        Connected(draft, reachable, failures);
        Distance(draft, failures);
        Accesses(draft, failures);
        RoomsReachable(draft, reachable, failures);
        Van(draft, failures);
        Walkable(draft, failures);
        Closed(draft, failures);
        NothingInTheBelt(draft, failures);
        SpawnDistance(draft, failures);

        return new ValidationReport(failures);
    }

    // --- Les neuf clauses -------------------------------------------------------------------------

    /// <summary>Le point de depose et l'objectif sont connectes, portes verrouillees comprises.</summary>
    private static void Connected(DistrictDraft draft, HashSet<GridPosition> reachable, List<ValidationFailure> failures)
    {
        if (draft.Objective?.Zone is not { } zone)
        {
            failures.Add(new ValidationFailure("objectif", "aucun batiment n'a ete designe pour le porter"));
            return;
        }

        if (zone.Positions().Any(reachable.Contains)) return;

        failures.Add(new ValidationFailure(
            "connectivite",
            $"« {zone.Name} » n'est joignable depuis {draft.Drop} par aucun chemin, meme en forcant"));
    }

    /// <summary>La distance depose -> objectif : ni collee au van, ni au bord oppose.</summary>
    private static void Distance(DistrictDraft draft, List<ValidationFailure> failures)
    {
        if (draft.Objective is not { } building) return;

        int distance = draft.Drop.ChebyshevDistanceTo(building.Bounds.Center);
        var range = draft.Profile.ObjectiveDistance;

        if (distance >= range.Min && distance <= range.Max) return;

        failures.Add(new ValidationFailure(
            "distance", $"objectif a {distance} cases, hors de l'intervalle {range}"));
    }

    /// <summary>
    /// Le batiment de l'objectif a au moins deux acces distincts : sinon un echec de <c>ForceDoor</c>
    /// clot la mission au lieu de la compliquer.
    /// </summary>
    private static void Accesses(DistrictDraft draft, List<ValidationFailure> failures)
    {
        if (draft.Objective is not { } building) return;
        if (building.Accesses >= 2) return;

        failures.Add(new ValidationFailure(
            "acces", $"le batiment de l'objectif n'a que {building.Accesses} acces"));
    }

    /// <summary>Aucune piece n'est inaccessible depuis l'exterieur de son batiment.</summary>
    private static void RoomsReachable(
        DistrictDraft draft, HashSet<GridPosition> reachable, List<ValidationFailure> failures)
    {
        foreach (var building in draft.Buildings)
        {
            foreach (var room in building.Rooms)
            {
                var floor = room.Interior().Where(cell => draft.Map.GetCell(cell).IsKnownWalkable()).ToList();

                // Une piece entierement meublee n'a plus de sol : ce n'est pas une piece murée, c'est
                // un placard plein. Elle ne rend pas le quartier injouable.
                if (floor.Count == 0 || floor.Any(reachable.Contains)) continue;

                failures.Add(new ValidationFailure(
                    "pieces",
                    $"la piece {room} du batiment {building.Bounds} est muree "
                  + $"({building.Doors.Count} porte(s), {building.Windows.Count} fenetre(s), "
                  + $"{building.Rooms.Count} piece(s))"));
                return;
            }
        }
    }

    /// <summary>Le van dispose de son empreinte de 2 x 3 cases libres, sur une voie touchant un bord.</summary>
    private static void Van(DistrictDraft draft, List<ValidationFailure> failures)
    {
        var van = draft.Van;

        if (van.Area != 6)
        {
            failures.Add(new ValidationFailure("van", $"l'empreinte {van} ne fait pas 2 x 3 cases"));
            return;
        }

        if (van.Positions().Any(cell => !draft.Map.IsWalkable(cell)))
        {
            failures.Add(new ValidationFailure("van", $"l'empreinte {van} n'est pas entierement libre"));
            return;
        }

        bool onEdge = van.Min.X <= draft.Playable.Min.X || van.Max.X >= draft.Playable.Max.X
                   || van.Min.Y <= draft.Playable.Min.Y || van.Max.Y >= draft.Playable.Max.Y;

        if (!onEdge) failures.Add(new ValidationFailure("van", $"l'empreinte {van} ne touche aucun bord"));
    }

    /// <summary>La proportion de cellules praticables du quartier jouable : ni labyrinthe, ni terrain vague.</summary>
    private static void Walkable(DistrictDraft draft, List<ValidationFailure> failures)
    {
        int total = draft.Playable.Area;
        int walkable = draft.Playable.Positions().Count(draft.Map.IsWalkable);

        float ratio = total == 0 ? 0f : (float)walkable / total;
        var range = draft.Profile.WalkableRatio;

        if (ratio >= range.Min && ratio <= range.Max) return;

        failures.Add(new ValidationFailure(
            "praticabilite", $"{ratio:P0} de cases praticables, hors de l'intervalle {range}"));
    }

    /// <summary>
    /// Aucune voie n'atteint un bord sans terminaison de ceinture.
    ///
    /// C'est la clause que le brouillard simplifie le plus. Avec six familles de tuiles raccordables,
    /// elle demandait de verifier que chaque rue butant sur le bord portait bien son portail soude ou
    /// son bouchon de voitures. Avec une ceinture qui remplace tout ce qui est au-dela, elle se dit
    /// une fois pour toutes : rien de praticable ne sort du quartier jouable.
    /// </summary>
    private static void Closed(DistrictDraft draft, List<ValidationFailure> failures)
    {
        foreach (var cell in draft.Map.AllPositions())
        {
            if (draft.IsPlayable(cell) || !draft.Map.IsWalkable(cell)) continue;

            failures.Add(new ValidationFailure("ceinture", $"la case praticable {cell} est hors du quartier"));
            return;
        }
    }

    /// <summary>Aucun zombie et aucun objet ne sont poses dans la ceinture.</summary>
    private static void NothingInTheBelt(DistrictDraft draft, List<ValidationFailure> failures)
    {
        var strays = draft.ZombieSpawns
            .Concat(draft.Items.Select(item => item.Position))
            // Toute l'empreinte, et pas seulement l'ancre : une epave posee au ras du quartier
            // jouable deborde sur la ceinture par ses trois autres cases de long.
            .Concat(draft.Heavy.SelectMany(heavy => heavy.Cells))
            .Where(cell => !draft.IsPlayable(cell))
            .ToList();

        if (strays.Count == 0) return;

        failures.Add(new ValidationFailure(
            "ceinture", $"{strays.Count} entite(s) posee(s) dans la ceinture, dont {strays[0]}"));
    }

    /// <summary>Aucun zombie n'apparait a moins de <c>zombies.minimum_spawn_distance</c> de la depose.</summary>
    private static void SpawnDistance(DistrictDraft draft, List<ValidationFailure> failures)
    {
        var close = draft.ZombieSpawns
            .Where(cell => cell.ChebyshevDistanceTo(draft.Drop) < draft.MinimumSpawnDistance)
            .ToList();

        if (close.Count == 0) return;

        failures.Add(new ValidationFailure(
            "horde",
            $"{close.Count} zombie(s) a moins de {draft.MinimumSpawnDistance} cases de la depose"));
    }

    // --- Ce que le groupe peut atteindre ----------------------------------------------------------

    /// <summary>
    /// Les cases joignables depuis le point de depose <b>au prix d'un effort</b> : une porte fermee
    /// s'ouvre, une porte verrouillee se force, une fenetre se brise. Seul le blindage arrete — c'est
    /// la seule chose qu'une carte puisse retirer du repertoire d'actions (livrable 11).
    ///
    /// Ce n'est donc pas la meme accessibilite que celle de la navigation, qui refuse une porte
    /// verrouillee. C'est celle du contrat : « il existe au moins un chemin dont chaque porte fermee
    /// est ouvrable, forcable, ou contournable par une fenetre ».
    /// </summary>
    public static HashSet<GridPosition> Reach(DistrictDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var seen = new HashSet<GridPosition> { draft.Drop };
        var pending = new Queue<GridPosition>();
        pending.Enqueue(draft.Drop);

        while (pending.Count > 0)
        {
            var cell = pending.Dequeue();

            foreach (var offset in GridPosition.OrthogonalOffsets)
            {
                var neighbour = cell + offset;
                if (!seen.Contains(neighbour) && Forcible(draft.Map, neighbour) && seen.Add(neighbour))
                {
                    pending.Enqueue(neighbour);
                }
            }
        }

        return seen;
    }

    private static bool Forcible(GridMap map, GridPosition cell)
    {
        if (!map.Contains(cell)) return false;

        return map.GetCell(cell) switch
        {
            CellType.Wall or CellType.Obstacle or CellType.Border or CellType.Unknown => false,
            CellType.DoorLocked or CellType.Window => !map.IsUnbreachable(cell),
            _ => true,
        };
    }
}

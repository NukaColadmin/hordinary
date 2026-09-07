namespace Hordinary.World.MapGeneration;

/// <summary>
/// Passe 4 — les lieux. Elle transforme des rectangles anonymes en <see cref="MapZone"/> au format de
/// <c>config/map_locations.json</c> : le point de depose, l'objectif de la mission, les batiments et
/// les voies.
///
/// C'est la seule passe qui lit la mission, et c'est voulu : une mission dit ou elle veut son
/// objectif — un type de zone, un profil, ou un nom exact — et le generateur doit lui donner un
/// endroit qui reponde a cette description. Le reste du pipeline ignore qu'une mission existe.
///
/// Aucun nom de lieu n'est ecrit ici. Les gabarits viennent de <c>config/mapgen.json</c>, et le
/// qualificatif qui les complete est le point cardinal reellement occupe : « Batiment nord » est au
/// nord, sinon le nom mentirait au joueur qui s'oriente avec.
/// </summary>
public static class ZonePass
{
    public static void Apply(DistrictDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var rng = new DistrictRandom(draft.Seed, "zones");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Drop(draft, names);
        Objective(draft, rng, names);
        Buildings(draft, names);
        Roads(draft, names);
    }

    // --- Le point de depose -----------------------------------------------------------------------

    /// <summary>
    /// La ou le van s'arrete est a la fois le depart et le retour, comme le camp de la carte
    /// dessinee. Les deux entrees restent distinctes parce que le domaine de planification connait la
    /// sortie sous le nom « extraction » et le briefing sous le nom « start ».
    /// </summary>
    private static void Drop(DistrictDraft draft, HashSet<string> names)
    {
        var naming = draft.Config.Naming;

        draft.Map.AddZone(draft.Van.ToZone(Unique(naming.Drop, names), ZoneKind.Start));

        var extraction = draft.Van.ToZone(Unique(naming.Extraction, names), ZoneKind.Extraction);
        draft.Map.AddZone(extraction);

        Stamp(draft, extraction, CellType.Extraction);
    }

    // --- L'objectif -------------------------------------------------------------------------------

    /// <summary>
    /// Le batiment qui portera ce que la mission va chercher. Deux exigences, dans cet ordre : au
    /// moins deux acces distincts, parce qu'un echec de <c>ForceDoor</c> ne doit pas clore la mission
    /// au lieu de la compliquer ; puis une distance de depose dans l'intervalle du profil, ni collee
    /// au van ni au bord oppose.
    ///
    /// Quand aucun batiment ne satisfait les deux, la passe pose quand meme le moins mauvais et laisse
    /// <see cref="ValidationPass"/> rejeter le quartier. Un producteur qui se corrigerait lui-meme
    /// rendrait le validateur decoratif, et c'est precisement ce que la separation des deux evite.
    /// </summary>
    private static void Objective(DistrictDraft draft, DistrictRandom rng, HashSet<string> names)
    {
        if (draft.Buildings.Count == 0) return;

        var mission = draft.Mission;
        var range = draft.Profile.ObjectiveDistance;

        var reachable = draft.Buildings.Where(b => b.Accesses >= 2).ToList();
        if (reachable.Count == 0) reachable = draft.Buildings;

        var wanted = reachable
            .Where(b => DistanceToDrop(draft, b) >= range.Min && DistanceToDrop(draft, b) <= range.Max)
            .ToList();

        // A defaut d'un batiment dans l'intervalle, le plus eloigne : c'est celui qui s'en approche
        // le plus par le haut, et un objectif trop loin se corrige par une graine, pas par un ilot.
        var candidates = wanted.Count > 0
            ? wanted
            : [reachable.OrderByDescending(b => DistanceToDrop(draft, b)).ThenBy(Ordinal).First()];

        var chosen = candidates.Count == 1
            ? candidates[0]
            : rng.Choose([.. candidates.OrderBy(Ordinal)], "objective");

        string name = mission.ObjectiveZone is { Length: > 0 } exact
            ? Unique(exact, names)
            : Unique(Describe(draft, draft.Config.Naming.Building, chosen.Bounds), names);

        var zone = chosen.Bounds.ToZone(name, mission.Objective, mission.ObjectiveProfile);

        draft.Map.AddZone(zone);
        chosen.Zone = zone;
        draft.Objective = chosen;

        // Meme regle que pour une carte dessinee : une zone d'objectif marque son sol, murs et portes
        // exceptes. C'est ce qui fait que le planificateur reconnait le lieu sans lire son nom.
        if (mission.Objective == ZoneKind.Pharmacy) Stamp(draft, zone, CellType.Pharmacy);
    }

    // --- Le reste ---------------------------------------------------------------------------------

    private static void Buildings(DistrictDraft draft, HashSet<string> names)
    {
        foreach (var building in draft.Buildings)
        {
            if (building.Zone is not null) continue;

            var zone = building.Bounds.ToZone(
                Unique(Describe(draft, draft.Config.Naming.Building, building.Bounds), names),
                ZoneKind.Building);

            draft.Map.AddZone(zone);
            building.Zone = zone;
        }
    }

    private static void Roads(DistrictDraft draft, HashSet<string> names)
    {
        var naming = draft.Config.Naming;

        foreach (var road in draft.Roads)
        {
            var (pattern, kind) = road.Kind switch
            {
                RoadKind.Main => (naming.MainRoad, ZoneKind.Street),
                RoadKind.Alley => (naming.Alley, ZoneKind.Alley),
                _ => (naming.Street, ZoneKind.Street),
            };

            draft.Map.AddZone(road.Bounds.ToZone(Unique(Describe(draft, pattern, road.Bounds), names), kind));
        }
    }

    // --- Utilitaires ------------------------------------------------------------------------------

    /// <summary>Distance de la depose au centre du batiment, en cases de Chebyshev.</summary>
    private static int DistanceToDrop(DistrictDraft draft, Building building)
        => draft.Drop.ChebyshevDistanceTo(building.Bounds.Center);

    /// <summary>Un ordre stable, pour que deux batiments a egalite ne dependent pas d'un tri instable.</summary>
    private static (int, int) Ordinal(Building building) => (building.Bounds.Min.Y, building.Bounds.Min.X);

    /// <summary>
    /// Remplit le gabarit avec le point cardinal reellement occupe. Un gabarit sans <c>{0}</c> — la
    /// rue principale, le point de depose — est rendu tel quel : il n'y en a qu'un par quartier.
    /// </summary>
    private static string Describe(DistrictDraft draft, string pattern, Rect bounds)
    {
        if (!pattern.Contains("{0}", StringComparison.Ordinal)) return pattern;

        return string.Format(pattern, draft.Config.Naming.Qualifier(Cardinal(draft, bounds)));
    }

    /// <summary>
    /// L'indice du point cardinal dans <c>naming.cardinals</c> : 0 nord, 1 est, 2 sud, 3 ouest, 4
    /// centre. L'ordre de la liste porte donc du sens, et c'est ecrit dans le fichier.
    /// </summary>
    private static int Cardinal(DistrictDraft draft, Rect bounds)
    {
        var centre = draft.Playable.Center;
        int dx = bounds.Center.X - centre.X;
        int dy = bounds.Center.Y - centre.Y;
        int inner = Math.Max(2, draft.Profile.Size / 6);

        if (Math.Abs(dx) < inner && Math.Abs(dy) < inner) return 4;
        if (Math.Abs(dx) > Math.Abs(dy)) return dx > 0 ? 1 : 3;

        return dy > 0 ? 2 : 0;
    }

    /// <summary>
    /// Deux lieux ne peuvent pas porter le meme nom : une <see cref="GridMap"/> les indexe par la, et
    /// le second effacerait le premier sans rien dire.
    /// </summary>
    private static string Unique(string wanted, HashSet<string> taken)
    {
        if (taken.Add(wanted)) return wanted;

        for (int suffix = 2; ; suffix++)
        {
            string candidate = $"{wanted} {suffix}";
            if (taken.Add(candidate)) return candidate;
        }
    }

    /// <summary>Marque le sol d'une zone. Murs, portes et fenetres gardent leur type.</summary>
    private static void Stamp(DistrictDraft draft, MapZone zone, CellType stamp)
    {
        foreach (var cell in zone.Positions())
        {
            if (draft.Map.GetCell(cell) == CellType.Floor) draft.Map.SetCell(cell, stamp);
        }
    }
}

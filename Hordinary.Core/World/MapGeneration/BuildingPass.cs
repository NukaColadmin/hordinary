namespace Hordinary.World.MapGeneration;

/// <summary>
/// Passe 3 — les batiments. Un par ilot : empreinte, murs, pieces par decoupage binaire recursif,
/// portes interieures, fenetres en facade, et une a trois portes d'entree.
///
/// Le decoupage binaire donne des pieces de tailles variees reliees en arbre, et un arbre a une
/// propriete qui vaut tout un algorithme de verification : chaque piece est joignable depuis
/// n'importe quelle autre, et donc depuis l'exterieur des qu'une porte d'entree existe. La clause
/// « aucune piece n'est inaccessible » du contrat de jouabilite est ainsi tenue par construction —
/// la passe 7 la verifie quand meme, parce qu'un producteur ne se valide pas lui-meme.
/// </summary>
public static class BuildingPass
{
    /// <summary>Trottoir minimal entre un batiment et le bord de son ilot.</summary>
    private const int Sidewalk = 2;

    /// <summary>Une piece a au moins une case d'interieur : en dessous, ce n'est plus une piece.</summary>
    private const int MinimumRoomSpan = 3;

    /// <summary>Un pan de facade tous les trois murs. C'est lui qui peut porter une fenetre.</summary>
    private const int FacadeStride = 3;

    public static void Apply(DistrictDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var rng = new DistrictRandom(draft.Seed, "buildings");

        foreach (var block in draft.Blocks)
        {
            foreach (var bounds in Plots(draft, rng, block))
            {
                var building = new Building(bounds);
                draft.Buildings.Add(building);

                Shell(draft, bounds);
                Rooms(draft, rng, building);
                Openings(draft, rng, building);
            }
        }
    }

    // --- Les parcelles ----------------------------------------------------------------------------

    /// <summary>
    /// Le decoupage d'un ilot en parcelles. Un ilot porte <b>plusieurs</b> batiments, pas un seul :
    /// un ilot de quarante cases de cote avec une maison au milieu n'est pas un quartier, c'est un
    /// terrain vague avec un pavillon dessus — et c'est exactement ce que la clause de praticabilite
    /// du contrat rejette.
    ///
    /// Les parcelles sont posees en colonnes de largeur variable, et chacune n'est batie qu'avec la
    /// probabilite que donne <c>buildings.density</c>. Ce qui reste vide est une cour, un terrain en
    /// friche, un parking : de l'espace ouvert au milieu du bati, ce qui vaut mieux qu'un bati
    /// regulier ou qu'un vide regulier.
    /// </summary>
    private static List<Rect> Plots(DistrictDraft draft, DistrictRandom rng, Rect block)
    {
        var plots = new List<Rect>();
        var available = block.Shrink(Sidewalk);

        var minimum = draft.Profile.MinimumFootprint ?? (6, 8);
        var maximum = draft.Profile.MaximumFootprint ?? (18, 22);

        if (available.Width < minimum.Width || available.Height < minimum.Height) return plots;

        float density = Math.Clamp(rng.Range(draft.Profile.Buildings.Density, "density"), 0.05f, 1f);

        for (int x = available.Min.X; available.Max.X - x + 1 >= minimum.Width;)
        {
            int width = rng.Between(
                minimum.Width, Math.Min(maximum.Width, available.Max.X - x + 1), "plot.width");

            for (int y = available.Min.Y; available.Max.Y - y + 1 >= minimum.Height;)
            {
                int height = rng.Between(
                    minimum.Height, Math.Min(maximum.Height, available.Max.Y - y + 1), "plot.height");

                if (rng.Chance(density, "plot.built"))
                {
                    plots.Add(Rect.FromBounds(x, y, x + width - 1, y + height - 1));
                }

                y += height + Sidewalk;
            }

            x += width + Sidewalk;
        }

        return plots;
    }

    // --- La coquille ------------------------------------------------------------------------------

    /// <summary>Murs sur le pourtour, sol a l'interieur, et le marqueur « interieur » qui va avec.</summary>
    private static void Shell(DistrictDraft draft, Rect bounds)
    {
        foreach (var cell in bounds.Positions())
        {
            bool edge = cell.X == bounds.Min.X || cell.X == bounds.Max.X
                     || cell.Y == bounds.Min.Y || cell.Y == bounds.Max.Y;

            draft.Map.SetCell(cell, edge ? CellType.Wall : CellType.Floor);
            draft.Map.SetIndoor(cell, true);
        }
    }

    // --- Les pieces -------------------------------------------------------------------------------

    /// <summary>
    /// Decoupage binaire recursif jusqu'au nombre de pieces demande. On fend toujours la plus grande
    /// piece restante : fendre au hasard produirait un placard a cote d'un gymnase.
    ///
    /// Les rectangles se partagent leur mur mitoyen — c'est ce qui evite les murs doubles, qui
    /// mangent l'interieur d'un batiment de dix cases en deux decoupages.
    /// </summary>
    private static void Rooms(DistrictDraft draft, DistrictRandom rng, Building building)
    {
        var rooms = new List<Rect> { building.Bounds };
        int wanted = Math.Max(1, rng.Pick(draft.Profile.Buildings.Rooms, "rooms"));

        while (rooms.Count < wanted)
        {
            int index = Largest(rooms);
            if (Split(draft, rng, rooms[index]) is not { } halves) break;

            rooms[index] = halves.First;
            rooms.Add(halves.Second);
        }

        building.Rooms.AddRange(rooms);
    }

    private static int Largest(List<Rect> rooms)
    {
        int best = 0;
        for (int i = 1; i < rooms.Count; i++)
        {
            if (rooms[i].Area > rooms[best].Area) best = i;
        }

        return best;
    }

    /// <summary>
    /// Fend une piece par un refend perce d'une porte. <c>null</c> quand il n'y a pas la place : c'est
    /// ce qui arrete la recursion sans avoir a compter les cases par avance.
    /// </summary>
    private static (Rect First, Rect Second)? Split(DistrictDraft draft, DistrictRandom rng, Rect room)
    {
        bool vertical = room.Width >= room.Height;
        int span = vertical ? room.Width : room.Height;

        if (span < 2 * MinimumRoomSpan - 1) return null;

        int origin = vertical ? room.Min.X : room.Min.Y;

        // Toutes les positions de refend qui ne condamnent aucune porte deja percee. Un refend
        // s'arrete une case avant les murs qu'il rejoint, mais cela ne suffit pas : s'il vient buter
        // contre une porte de la cloison d'en face, il bouche la case par laquelle on la franchissait.
        // C'est le defaut qui muraient une piece sur trois au centre-ville, et il ne se voyait qu'a
        // la relecture — la porte etait bien la, elle ne menait simplement nulle part.
        var cuts = new List<int>();

        for (int candidate = origin + MinimumRoomSpan - 1; candidate <= origin + span - MinimumRoomSpan; candidate++)
        {
            if (Clear(draft, Refend(room, candidate, vertical))) cuts.Add(candidate);
        }

        if (cuts.Count == 0) return null;

        int cut = cuts[rng.Between(0, cuts.Count - 1, "room.cut")];
        var wallCells = Refend(room, cut, vertical).Positions().ToList();

        // La porte est choisie avant que le refend soit peint, et seulement la ou le passage existe
        // des deux cotes. Deux refends qui se croisent forment un T : percer le sommet de ce T donne
        // une porte qui ouvre sur l'autre cloison, et la piece derriere est muree sans que rien ne le
        // dise. C'est le seul defaut de ce decoupage qui produise une carte injouable.
        var openings = wallCells.Where(cell => Crosses(draft, cell, vertical)).ToList();
        if (openings.Count == 0) return null;

        foreach (var cell in wallCells) draft.Map.SetCell(cell, CellType.Wall);

        // Une porte interieure se verrouille moitie moins souvent qu'une porte d'entree : la reserve
        // du fond doit rester l'exception, sinon chaque piece demande une effraction et le batiment
        // se joue au pied-de-biche du premier couloir.
        var door = openings[rng.Between(0, openings.Count - 1, "room.door")];
        bool locked = rng.Chance(draft.Profile.Doors.LockedChance * 0.5f, "room.lock");

        draft.Map.SetCell(door, locked ? CellType.DoorLocked : CellType.DoorClosed);

        return vertical
            ? (Rect.FromBounds(room.Min.X, room.Min.Y, cut, room.Max.Y),
               Rect.FromBounds(cut, room.Min.Y, room.Max.X, room.Max.Y))
            : (Rect.FromBounds(room.Min.X, room.Min.Y, room.Max.X, cut),
               Rect.FromBounds(room.Min.X, cut, room.Max.X, room.Max.Y));
    }

    /// <summary>
    /// Le refend qui fendrait cette piece a cette position. Il s'arrete une case avant les deux murs
    /// qu'il rejoint : ceux-la existent deja, et les repeindre effacerait la porte qu'un decoupage
    /// precedent y avait percee.
    /// </summary>
    private static Rect Refend(Rect room, int cut, bool vertical)
        => vertical
            ? Rect.FromBounds(cut, room.Min.Y + 1, cut, room.Max.Y - 1)
            : Rect.FromBounds(room.Min.X + 1, cut, room.Max.X - 1, cut);

    /// <summary>
    /// Vrai si ce refend ne vient condamner aucune porte. Une porte a besoin d'une case libre de
    /// chaque cote ; un mur pose contre elle la laisse ouverte sur une cloison, ce qui est pire
    /// qu'une porte murée : elle compte comme un passage et n'en est pas un.
    /// </summary>
    private static bool Clear(DistrictDraft draft, Rect wall)
    {
        foreach (var cell in wall.Positions())
        {
            foreach (var offset in GridPosition.OrthogonalOffsets)
            {
                if (draft.Map.GetCell(cell + offset).IsDoor()) return false;
            }
        }

        return true;
    }

    /// <summary>Vrai si une porte percee ici donnerait sur du sol des deux cotes du refend.</summary>
    private static bool Crosses(DistrictDraft draft, GridPosition cell, bool vertical)
    {
        var step = vertical ? new GridPosition(1, 0) : new GridPosition(0, 1);

        return draft.Map.GetCell(cell + step).IsKnownWalkable()
            && draft.Map.GetCell(cell - step).IsKnownWalkable();
    }

    // --- Ce par quoi on entre ---------------------------------------------------------------------

    /// <summary>
    /// Les portes d'entree, puis les fenetres. Les unes et les autres sont des acces, et c'est ce qui
    /// rend une facade lisible de l'exterieur : un batiment dont la seule porte est verrouillee reste
    /// jouable s'il a une fenetre, et le contrat de jouabilite compte les deux ensemble.
    /// </summary>
    private static void Openings(DistrictDraft draft, DistrictRandom rng, Building building)
    {
        var candidates = building.Bounds.Perimeter().ToList();
        if (candidates.Count == 0) return;

        rng.Shuffle(candidates, "facade");

        int wanted = Math.Max(1, rng.Pick(draft.Profile.Doors.PerBuilding, "doors"));
        var taken = new HashSet<GridPosition>();

        foreach (var cell in candidates)
        {
            if (building.Doors.Count >= wanted) break;

            // Deux portes voisines n'en font qu'une : on ecarte tout le mur autour de celle-ci.
            if (taken.Any(other => other.ChebyshevDistanceTo(cell) <= 2)) continue;
            if (!OpensOnSomething(draft, building.Bounds, cell)) continue;

            bool locked = rng.Chance(draft.Profile.Doors.LockedChance, "lock");
            draft.Map.SetCell(cell, locked ? CellType.DoorLocked : CellType.DoorClosed);

            building.Doors.Add(cell);
            taken.Add(cell);
        }

        // Un pan de facade tous les trois murs : sans cet espacement, une probabilite par case ferait
        // une verriere plutot qu'un batiment.
        for (int i = 0; i < candidates.Count; i += FacadeStride)
        {
            var cell = candidates[i];
            if (taken.Contains(cell) || draft.Map.GetCell(cell) != CellType.Wall) continue;
            if (!OpensOnSomething(draft, building.Bounds, cell)) continue;
            if (!rng.Chance(draft.Profile.Doors.WindowChance, "window")) continue;

            draft.Map.SetCell(cell, CellType.Window);
            building.Windows.Add(cell);
            taken.Add(cell);
        }
    }

    /// <summary>
    /// Vrai si le mur donne sur quelque chose des deux cotes : une piece dedans, la rue dehors.
    ///
    /// Un refend qui rejoint le mur d'enceinte forme un T, et percer le sommet de ce T donne une
    /// porte qui ouvre sur un mur. Ce n'est pas une porte condamnee — c'est pire : elle compte comme
    /// un acces, le contrat de jouabilite la croit sur parole, et le survivant qui la force se
    /// retrouve devant une cloison.
    /// </summary>
    private static bool OpensOnSomething(DistrictDraft draft, Rect bounds, GridPosition cell)
    {
        var inward = new GridPosition(
            cell.X == bounds.Min.X ? 1 : cell.X == bounds.Max.X ? -1 : 0,
            cell.Y == bounds.Min.Y ? 1 : cell.Y == bounds.Max.Y ? -1 : 0);

        var inside = cell + inward;
        var outside = cell - inward;

        return draft.Map.GetCell(inside).IsKnownWalkable() && draft.Map.IsWalkable(outside);
    }
}

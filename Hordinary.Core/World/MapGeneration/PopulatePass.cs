namespace Hordinary.World.MapGeneration;

/// <summary>
/// Passe 5 — le peuplement : les meubles poussables du livrable 11, les objets au sol, et la horde.
///
/// L'ordre compte et il est le seul possible. Les meubles ferment des cases, les objets se posent sur
/// celles qui restent, et les zombies apparaissent sur ce qui est encore praticable une fois tout
/// pose. Peupler d'abord et meubler ensuite murerait un zombie dans un rayonnage.
///
/// Les caisses de la mission ne sont pas posees ici. L'assemblage le fait deja, de la meme facon,
/// pour la carte dessinee (<c>GameRoot.PlaceSupplies</c>) : une regle qui existe et qui marche ne
/// gagne rien a exister deux fois, et la seconde ecriture serait celle qui divergerait.
/// </summary>
public static class PopulatePass
{
    public static void Apply(DistrictDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var rng = new DistrictRandom(draft.Seed, "populate");

        Furniture(draft, rng);
        GroundItems(draft, rng);
        Horde(draft, rng);
    }

    // --- Les meubles ------------------------------------------------------------------------------

    /// <summary>
    /// Un meuble poussable occupe une cellule <see cref="CellType.Obstacle"/> : la navigation et les
    /// lignes de vue n'ont donc rien de nouveau a apprendre. C'est aussi ce qui le rend dangereux a
    /// poser — un obstacle de trop en travers d'une ruelle coupe le quartier en deux.
    ///
    /// D'ou la regle : on ne meuble qu'une case qui garde au moins trois voisins praticables, et
    /// jamais le pas d'une porte. Elle ne remplace pas la verification de connectivite de la passe 7,
    /// elle lui evite de rejeter sept quartiers sur huit.
    /// </summary>
    private static void Furniture(DistrictDraft draft, DistrictRandom rng)
    {
        var kinds = draft.Profile.Props.HeavyKinds;
        if (kinds.Count == 0) return;

        int index = 0;

        foreach (var block in draft.Blocks)
        {
            int wanted = rng.Pick(draft.Profile.Props.HeavyPerBlock, "heavy.count");
            if (wanted <= 0) continue;

            var candidates = block.Positions().Where(cell => CanFurnish(draft, cell)).ToList();
            if (candidates.Count == 0) continue;

            rng.Shuffle(candidates, "heavy.order");

            foreach (var cell in candidates.Take(wanted))
            {
                // Reevalue apres chaque pose : deux meubles voisins poses de suite formeraient le mur
                // que la regle cherche justement a eviter.
                if (!CanFurnish(draft, cell)) continue;

                bool indoor = draft.Map.IsIndoor(cell);
                var suitable = kinds.Where(kind => kind.Outdoor != indoor).ToList();
                if (suitable.Count == 0) continue;

                var chosen = rng.Choose(suitable, "heavy.kind");

                // Une epave tient sur quatre cases : elle ne se pose que la ou les quatre sont
                // libres, et dans le sens qui rentre. Un tirage qui echoue ne cherche pas ailleurs —
                // la ruelle etait trop etroite, et c'est une raison suffisante de ne rien y mettre.
                if (Orient(draft, cell, chosen) is not { } footprint) continue;

                foreach (var covered in footprint.Cells) draft.Map.SetCell(covered, CellType.Obstacle);

                draft.Heavy.Add(new HeavyObject($"heavy-{++index}", chosen.Kind, cell, chosen.Mass)
                {
                    Width = footprint.Width,
                    Height = footprint.Height,
                });
            }
        }
    }

    /// <summary>L'empreinte reellement posee : ancre, largeur, hauteur, et les cases couvertes.</summary>
    private readonly record struct Footprint(GridPosition Anchor, int Width, int Height)
    {
        public IEnumerable<GridPosition> Cells
        {
            get
            {
                for (int dy = 0; dy < Height; dy++)
                    for (int dx = 0; dx < Width; dx++)
                        yield return new GridPosition(Anchor.X + dx, Anchor.Y + dy);
            }
        }
    }

    /// <summary>
    /// Dans quel sens poser le meuble, ou <c>null</c> s'il ne rentre dans aucun.
    ///
    /// Les deux orientations sont essayees, la longueur en travers d'abord — une voiture se gare le
    /// long d'une rue plus souvent qu'en travers, et la premiere qui tient gagne. Chaque case de
    /// l'empreinte doit satisfaire la meme regle de contournement que la case d'ancrage : un meuble
    /// de huit cases est huit fois plus capable de couper un quartier en deux qu'un rayonnage.
    /// </summary>
    private static Footprint? Orient(DistrictDraft draft, GridPosition anchor, HeavyKind kind)
    {
        int length = Math.Max(1, kind.Length);
        int breadth = Math.Max(1, kind.Breadth);

        if (length == 1 && breadth == 1) return new Footprint(anchor, 1, 1);

        foreach (var (width, height) in new[] { (length, breadth), (breadth, length) })
        {
            var footprint = new Footprint(anchor, width, height);
            if (footprint.Cells.All(cell => CanFurnish(draft, cell))) return footprint;
        }

        return null;
    }

    /// <summary>Fenetre du contournement local, en cases de rayon. Deux : de quoi longer un meuble.</summary>
    private const int DetourRadius = 2;

    /// <summary>
    /// Vrai si l'on peut fermer cette case sans couper le quartier.
    ///
    /// La regle n'est pas un compte de voisins mais un <b>contournement</b> : une fois le meuble pose,
    /// les cases praticables qui l'entouraient doivent rester reliees entre elles sans passer par lui,
    /// et sans s'eloigner de plus de deux cases. Un compte de voisins ne suffisait pas — dans un
    /// passage de deux cases de large, deux meubles poses en diagonale gardent chacun trois voisins
    /// libres et coupent pourtant le passage, parce que la navigation refuse la coupe de coin
    /// (<c>NavigationGrid.GetNeighbours</c>). C'est exactement le defaut qui isolait un ilot entier.
    ///
    /// Le contournement est volontairement local, donc prudent : il refuse des emplacements qui
    /// n'auraient rien coupe. C'est le bon sens du compromis — un meuble en moins ne se voit pas, un
    /// quartier coupe en deux se voit tout de suite.
    /// </summary>
    private static bool CanFurnish(DistrictDraft draft, GridPosition cell)
    {
        if (draft.Map.GetCell(cell) != CellType.Floor) return false;
        if (draft.Van.Contains(cell)) return false;

        var around = new List<GridPosition>();

        foreach (var offset in GridPosition.OrthogonalOffsets)
        {
            var neighbour = cell + offset;

            // Jamais sur le pas d'une porte : c'est le seul endroit d'un batiment ou l'on est certain
            // qu'il n'existe aucun contournement.
            if (draft.Map.GetCell(neighbour).IsDoor()) return false;
            if (draft.Map.IsWalkable(neighbour)) around.Add(neighbour);
        }

        if (around.Count < 3) return false;

        var window = Rect.FromBounds(
            cell.X - DetourRadius, cell.Y - DetourRadius,
            cell.X + DetourRadius, cell.Y + DetourRadius);

        // La case candidate est marquee vue des le depart : le parcours ne peut donc pas l'emprunter,
        // ce qui revient a la traiter comme deja meublee.
        var seen = new HashSet<GridPosition> { cell, around[0] };
        var pending = new Queue<GridPosition>();
        pending.Enqueue(around[0]);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();

            foreach (var offset in GridPosition.OrthogonalOffsets)
            {
                var next = current + offset;
                if (!window.Contains(next) || !draft.Map.IsWalkable(next) || !seen.Add(next)) continue;

                pending.Enqueue(next);
            }
        }

        return around.All(seen.Contains);
    }

    // --- Les objets au sol ------------------------------------------------------------------------

    /// <summary>
    /// Ce qu'on trouve en fouillant un batiment. Les identifiants viennent du profil de carte : le
    /// generateur ne nomme aucun objet, il tire dans une liste ecrite dans <c>config/mapgen.json</c>.
    /// </summary>
    private static void GroundItems(DistrictDraft draft, DistrictRandom rng)
    {
        var catalogue = draft.Profile.Props.GroundItems;
        if (catalogue.Count == 0) return;

        var used = new HashSet<GridPosition>();

        foreach (var building in draft.Buildings)
        {
            int wanted = rng.Pick(draft.Profile.Props.GroundItemsPerBuilding, "items.count");
            if (wanted <= 0) continue;

            var cells = building.Bounds.Interior()
                .Where(cell => draft.Map.GetCell(cell).IsKnownWalkable() && !used.Contains(cell))
                .ToList();

            if (cells.Count == 0) continue;

            rng.Shuffle(cells, "items.order");

            foreach (var cell in cells.Take(wanted))
            {
                used.Add(cell);
                draft.Items.Add(new DistrictItem(rng.Choose(catalogue, "items.kind"), cell));
            }
        }
    }

    // --- La horde ---------------------------------------------------------------------------------

    /// <summary>
    /// La horde, a distance du point de depose. La regle de distance n'est pas reecrite ici : c'est
    /// <c>zombies.minimum_spawn_distance</c> de <c>config/game_config.json</c>, transmis avec la
    /// demande de generation.
    ///
    /// Aucun zombie ne peut apparaitre dans la ceinture, et pas par une verification : la ceinture
    /// n'est pas praticable, et le tirage ne connait que des cases praticables du quartier jouable.
    /// </summary>
    private static void Horde(DistrictDraft draft, DistrictRandom rng)
    {
        if (draft.ZombieCount <= 0) return;

        var candidates = draft.Playable.Positions()
            .Where(draft.Map.IsWalkable)
            .Where(cell => !draft.Van.Contains(cell))
            .Where(cell => cell.ChebyshevDistanceTo(draft.Drop) >= draft.MinimumSpawnDistance)
            .ToList();

        if (candidates.Count == 0) return;

        rng.Shuffle(candidates, "zombie.order");
        draft.ZombieSpawns.AddRange(candidates.Take(draft.ZombieCount));
    }
}

namespace Hordinary.World.MapGeneration;

/// <summary>
/// Passe 1 — la trame. Le point de depose du van est pose <b>en premier</b>, sur un bord ; tout le
/// reste est construit en fonction de lui.
///
/// Cette passe decide <b>ou</b> les choses vont : le bord d'arrivee, la traversante qui le rejoint,
/// et le decoupage du quartier en ilots par la voirie. Elle ne peint rien — <see cref="RoadPass"/>
/// s'en charge. La separation vaut son prix a l'usage : on peut regarder une trame sans regarder une
/// carte, et une trame se lit en cinq rectangles.
/// </summary>
public static class LayoutPass
{
    /// <summary>Marge minimale entre deux batiments voisins : le trottoir, et de quoi se faufiler.</summary>
    private const int BlockMargin = 2;

    public static void Apply(DistrictDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var rng = new DistrictRandom(draft.Seed, "layout");
        var profile = draft.Profile;
        var playable = draft.Playable;

        int minimumBlock = MinimumBlock(profile);
        int mainWidth = Math.Max(2, rng.Pick(profile.Roads.MainWidth, "main.width"));

        draft.DropSide = (Side)rng.Between(0, 3, "side");
        bool vertical = draft.DropSide is Side.North or Side.South;

        // La traversante est posee sur l'axe qui rejoint le bord d'arrivee au bord oppose. Sa
        // position est bornee pour qu'il reste un ilot de chaque cote : une rue principale collee au
        // bord donnerait un quartier a sens unique, et le van n'aurait rien devant lui.
        int low = (vertical ? playable.Min.X : playable.Min.Y) + minimumBlock;
        int high = (vertical ? playable.Max.X : playable.Max.Y) - minimumBlock - mainWidth + 1;

        if (high < low) (low, high) = (Middle(playable, vertical), Middle(playable, vertical));

        int start = rng.Between(low, high, "main.position");

        var main = vertical
            ? Rect.FromBounds(start, playable.Min.Y, start + mainWidth - 1, playable.Max.Y)
            : Rect.FromBounds(playable.Min.X, start, playable.Max.X, start + mainWidth - 1);

        draft.Roads.Add(new RoadStrip(main, RoadKind.Main, vertical));

        PlaceVan(draft, main, vertical);

        // De part et d'autre de la traversante : deux regions a decouper. Les ilots vont a la plus
        // grande en proportion de sa surface, sinon un quartier coupe en deux tiers / un tiers
        // recevrait autant de batiments des deux cotes.
        var regions = vertical
            ? new[]
            {
                Rect.FromBounds(playable.Min.X, playable.Min.Y, main.Min.X - 1, playable.Max.Y),
                Rect.FromBounds(main.Max.X + 1, playable.Min.Y, playable.Max.X, playable.Max.Y),
            }
            : new[]
            {
                Rect.FromBounds(playable.Min.X, playable.Min.Y, playable.Max.X, main.Min.Y - 1),
                Rect.FromBounds(playable.Min.X, main.Max.Y + 1, playable.Max.X, playable.Max.Y),
            };

        int wanted = Math.Max(2, rng.Pick(profile.Blocks, "count"));
        int total = regions.Where(region => !region.IsEmpty).Sum(region => region.Area);

        for (int i = 0; i < regions.Length; i++)
        {
            if (regions[i].IsEmpty) continue;

            // Le dernier prend ce qui reste : sans cela, deux arrondis vers le bas perdraient un ilot.
            int share = i == regions.Length - 1
                ? Math.Max(1, wanted - draft.Blocks.Count)
                : Math.Max(1, wanted * regions[i].Area / Math.Max(1, total));

            Subdivide(draft, rng, regions[i], share, minimumBlock);
        }
    }

    /// <summary>
    /// Le van ferme la route d'entree : il est gare a l'extremite de la traversante, contre la
    /// ceinture, et son empreinte de 2 x 3 cases (livrable 12) est en travers de la chaussee.
    /// </summary>
    private static void PlaceVan(DistrictDraft draft, Rect main, bool vertical)
    {
        var playable = draft.Playable;

        // 2 cases en travers de la voie, 3 dans sa longueur : le van barre la rue sans la remplir.
        var van = (vertical, draft.DropSide) switch
        {
            (true, Side.North) => Rect.FromBounds(main.Min.X, playable.Min.Y, main.Min.X + 1, playable.Min.Y + 2),
            (true, _) => Rect.FromBounds(main.Min.X, playable.Max.Y - 2, main.Min.X + 1, playable.Max.Y),
            (false, Side.West) => Rect.FromBounds(playable.Min.X, main.Min.Y, playable.Min.X + 2, main.Min.Y + 1),
            _ => Rect.FromBounds(playable.Max.X - 2, main.Min.Y, playable.Max.X, main.Min.Y + 1),
        };

        draft.Van = van;
        draft.Drop = van.Center;
    }

    /// <summary>
    /// Decoupage binaire recursif. On fend toujours le cote le plus long : sans cela, une region
    /// deux fois plus haute que large produirait des ilots en lanieres, et un batiment en laniere
    /// n'est qu'un couloir avec un toit.
    /// </summary>
    private static void Subdivide(DistrictDraft draft, DistrictRandom rng, Rect region, int leaves, int minimumBlock)
    {
        int streetWidth = Math.Max(1, rng.Pick(draft.Profile.Roads.StreetWidth, "street.width"));

        if (leaves <= 1)
        {
            draft.Blocks.Add(region);
            return;
        }

        bool vertical = region.Width >= region.Height;
        int span = vertical ? region.Width : region.Height;

        // Il faut de quoi loger deux ilots et la rue qui les separe, sinon on s'arrete ici.
        if (span < 2 * minimumBlock + streetWidth)
        {
            draft.Blocks.Add(region);
            return;
        }

        int origin = vertical ? region.Min.X : region.Min.Y;
        int cut = rng.Between(
            origin + minimumBlock,
            origin + span - minimumBlock - streetWidth,
            "cut");

        var street = vertical
            ? Rect.FromBounds(cut, region.Min.Y, cut + streetWidth - 1, region.Max.Y)
            : Rect.FromBounds(region.Min.X, cut, region.Max.X, cut + streetWidth - 1);

        draft.Roads.Add(new RoadStrip(street, RoadKind.Street, vertical));

        var first = vertical
            ? Rect.FromBounds(region.Min.X, region.Min.Y, street.Min.X - 1, region.Max.Y)
            : Rect.FromBounds(region.Min.X, region.Min.Y, region.Max.X, street.Min.Y - 1);

        var second = vertical
            ? Rect.FromBounds(street.Max.X + 1, region.Min.Y, region.Max.X, region.Max.Y)
            : Rect.FromBounds(region.Min.X, street.Max.Y + 1, region.Max.X, region.Max.Y);

        int half = leaves / 2;
        Subdivide(draft, rng, first, half, minimumBlock);
        Subdivide(draft, rng, second, leaves - half, minimumBlock);
    }

    /// <summary>Le plus petit ilot qui accepte encore un batiment et son trottoir.</summary>
    private static int MinimumBlock(MapProfile profile)
    {
        var footprint = profile.MinimumFootprint ?? (6, 8);
        return Math.Max(footprint.Width, footprint.Height) + 2 * BlockMargin;
    }

    private static int Middle(Rect rect, bool vertical) => vertical ? rect.Center.X : rect.Center.Y;
}

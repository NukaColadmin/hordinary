namespace Hordinary.World.MapGeneration;

/// <summary>
/// Passe 2 — la voirie. Elle peint ce que la passe 1 a decide, fend quelques ilots d'une ruelle, et
/// garantit la connectivite globale <b>avant</b> que quoi que ce soit d'autre soit pose.
///
/// La garantie tient a un choix et pas a un algorithme : le quartier jouable est de la rue partout,
/// et c'est la passe 3 qui y pose des murs, jamais l'inverse. Un batiment n'est donc pas un obstacle
/// qu'il faudrait contourner par un chemin qu'on aurait calcule — c'est un trou creuse dans un
/// espace deja entierement connexe, et un trou entoure d'un trottoir ne coupe rien.
/// </summary>
public static class RoadPass
{
    /// <summary>Largeur d'une ruelle. Une case : c'est ce qui en fait une ruelle et pas une rue.</summary>
    private const int AlleyWidth = 1;

    public static void Apply(DistrictDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var rng = new DistrictRandom(draft.Seed, "roads");

        foreach (var cell in draft.Playable.Positions())
        {
            draft.Map.SetCell(cell, CellType.Floor);
            draft.Map.SetIndoor(cell, false);
        }

        CarveAlleys(draft, rng);
    }

    /// <summary>
    /// Une ruelle fend un ilot en deux. Ce n'est pas un decor : elle donne un second acces a l'arriere
    /// des batiments, une ligne de vue d'une seule case de large, et un raccourci que la horde emprunte
    /// aussi bien que le groupe.
    /// </summary>
    private static void CarveAlleys(DistrictDraft draft, DistrictRandom rng)
    {
        var footprint = draft.Profile.MinimumFootprint ?? (6, 8);
        int minimumHalf = Math.Min(footprint.Width, footprint.Height) + 2;

        var blocks = draft.Blocks.ToList();
        draft.Blocks.Clear();

        foreach (var block in blocks)
        {
            bool vertical = block.Width >= block.Height;
            int span = vertical ? block.Width : block.Height;

            if (span < 2 * minimumHalf + AlleyWidth
                || !rng.Chance(draft.Profile.Roads.AlleyChance, "alley"))
            {
                draft.Blocks.Add(block);
                continue;
            }

            int origin = vertical ? block.Min.X : block.Min.Y;
            int cut = rng.Between(origin + minimumHalf, origin + span - minimumHalf - AlleyWidth, "alley.cut");

            var alley = vertical
                ? Rect.FromBounds(cut, block.Min.Y, cut + AlleyWidth - 1, block.Max.Y)
                : Rect.FromBounds(block.Min.X, cut, block.Max.X, cut + AlleyWidth - 1);

            draft.Roads.Add(new RoadStrip(alley, RoadKind.Alley, vertical));

            draft.Blocks.Add(vertical
                ? Rect.FromBounds(block.Min.X, block.Min.Y, alley.Min.X - 1, block.Max.Y)
                : Rect.FromBounds(block.Min.X, block.Min.Y, block.Max.X, alley.Min.Y - 1));

            draft.Blocks.Add(vertical
                ? Rect.FromBounds(alley.Max.X + 1, block.Min.Y, block.Max.X, block.Max.Y)
                : Rect.FromBounds(block.Min.X, alley.Max.Y + 1, block.Max.X, block.Max.Y));
        }
    }
}

namespace Hordinary.World.MapGeneration;

/// <summary>
/// Passe 6 — la ceinture. C'est la seule vraie question de conception du livrable : une carte finie
/// ne doit ni ressembler a une carte finie, ni laisser croire qu'on peut continuer.
///
/// La reponse tient en une couche, et c'est le brouillard. La specification en decrivait quatre —
/// ceinture dessinee, bande de decor, fondu vers le noir, et six familles de tuiles raccordables avec
/// leurs terminaisons de rue — parce qu'aucune ne suffisait seule : un grillage explique pourquoi on
/// ne passe pas, mais il faut du decor derriere lui pour que la ville continue, et un fondu pour que
/// le decor s'arrete sans ligne nette. Le brouillard fait les trois d'un coup. Il explique
/// (on ne marche pas dedans), il <b>remplace</b> ce qu'il y a derriere au lieu de le cacher — il n'y a
/// donc rien a dessiner derriere — et il est deja un fondu.
///
/// Ce qui reste de la specification, et qui comptait vraiment : la ceinture est <b>de la carte</b>,
/// pas un mur invisible ; elle est infranchissable ; elle est connue des survivants des le depart ; et
/// elle peut s'ouvrir d'une breche par ou la horde se renforce.
/// </summary>
public static class BorderPass
{
    public static void Apply(DistrictDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var rng = new DistrictRandom(draft.Seed, "belt");

        // La grille nait entierement en ceinture et les passes 2 a 3 creusent le quartier dedans :
        // repeindre le pourtour ici serait redire ce qui est deja vrai. Cette passe le verifie plutot
        // que de le refaire — une passe anterieure qui deborderait doit se voir, pas se faire couvrir.
        foreach (var cell in draft.Map.AllPositions())
        {
            if (draft.IsPlayable(cell)) continue;

            if (draft.Map.GetCell(cell) != CellType.Border)
            {
                throw new InvalidOperationException(
                    $"Une passe a ecrit en {cell}, hors du quartier jouable {draft.Playable}.");
            }
        }

        Breach(draft, rng);
    }

    /// <summary>
    /// La breche : un endroit ou le brouillard s'eclaircit. Elle n'est <b>pas</b> franchissable — la
    /// case reste de la ceinture, et la navigation ne la voit pas autrement qu'une autre. C'est par la
    /// que les renforts de horde entrent en cours de mission, et le bord cesse alors d'etre une limite
    /// pour devenir une menace : la meilleure facon de faire oublier que c'en est une.
    ///
    /// Elle est posee sur la voie la plus longue qui atteint un bord, et jamais du cote du van : un
    /// renfort qui arriverait par la sortie ne serait pas une menace, ce serait une punition.
    /// </summary>
    private static void Breach(DistrictDraft draft, DistrictRandom rng)
    {
        if (!draft.Border.AllowsBreach) return;
        if (!rng.Chance(draft.Config.Border.BreachChance, "breach")) return;

        var elsewhere = draft.Roads
            .Where(road => road.Kind != RoadKind.Alley)
            .Where(road => !Touches(draft, road, draft.DropSide))
            .SelectMany(road => Mouths(draft, road))
            .ToList();

        if (elsewhere.Count == 0) return;

        var mouth = rng.Choose(elsewhere, "breach.mouth");
        int width = Math.Max(1, rng.Pick(draft.Config.Border.BreachWidth, "breach.width"));

        // La breche traverse toute l'epaisseur du brouillard : une eclaircie qui s'arreterait a mi
        // chemin ne se lirait pas comme un passage, seulement comme une tache.
        foreach (var cell in mouth.Bounds.Positions())
        {
            for (int step = 0; step < width; step++)
            {
                var deep = cell + new GridPosition(mouth.Outward.X * step, mouth.Outward.Y * step);
                if (draft.Map.GetCell(deep) == CellType.Border) draft.Breach.Add(deep);
            }
        }
    }

    /// <summary>Vrai si la voie debouche sur ce bord.</summary>
    private static bool Touches(DistrictDraft draft, RoadStrip road, Side side) => side switch
    {
        Side.North => road.Bounds.Min.Y <= draft.Playable.Min.Y,
        Side.South => road.Bounds.Max.Y >= draft.Playable.Max.Y,
        Side.West => road.Bounds.Min.X <= draft.Playable.Min.X,
        _ => road.Bounds.Max.X >= draft.Playable.Max.X,
    };

    /// <summary>
    /// Les debouches d'une voie : la premiere rangee de ceinture qu'elle touche, et la direction dans
    /// laquelle il faut s'enfoncer pour la traverser.
    /// </summary>
    private static IEnumerable<(Rect Bounds, GridPosition Outward)> Mouths(DistrictDraft draft, RoadStrip road)
    {
        var playable = draft.Playable;
        var bounds = road.Bounds;

        if (road.Vertical)
        {
            if (bounds.Min.Y <= playable.Min.Y)
            {
                yield return (Rect.FromBounds(bounds.Min.X, playable.Min.Y - 1, bounds.Max.X, playable.Min.Y - 1),
                              new GridPosition(0, -1));
            }

            if (bounds.Max.Y >= playable.Max.Y)
            {
                yield return (Rect.FromBounds(bounds.Min.X, playable.Max.Y + 1, bounds.Max.X, playable.Max.Y + 1),
                              new GridPosition(0, 1));
            }

            yield break;
        }

        if (bounds.Min.X <= playable.Min.X)
        {
            yield return (Rect.FromBounds(playable.Min.X - 1, bounds.Min.Y, playable.Min.X - 1, bounds.Max.Y),
                          new GridPosition(-1, 0));
        }

        if (bounds.Max.X >= playable.Max.X)
        {
            yield return (Rect.FromBounds(playable.Max.X + 1, bounds.Min.Y, playable.Max.X + 1, bounds.Max.Y),
                          new GridPosition(1, 0));
        }
    }
}

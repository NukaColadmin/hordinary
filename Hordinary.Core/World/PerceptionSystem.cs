using System.Numerics;
using Hordinary.Agents;

namespace Hordinary.World;

/// <summary>Resultat d'une passe de perception, utile au debug et a l'affichage.</summary>
public sealed class PerceptionResult(
    IReadOnlyList<GridPosition> visibleCells,
    int discoveredCells,
    IReadOnlyList<string> seenEntities,
    IReadOnlyList<string> seenItems)
{
    public IReadOnlyList<GridPosition> VisibleCells { get; } = visibleCells;

    /// <summary>Cellules observees pour la premiere fois lors de cette passe.</summary>
    public int DiscoveredCells { get; } = discoveredCells;

    public IReadOnlyList<string> SeenEntities { get; } = seenEntities;
    public IReadOnlyList<string> SeenItems { get; } = seenItems;
}

/// <summary>
/// Bruit tel qu'un agent l'a entendu : l'evenement reel, la position qu'il lui prete, et l'intensite
/// qui lui est parvenue. L'ecart entre <see cref="Noise"/> et <see cref="BelievedPosition"/> est
/// exactement ce que l'auditeur ignore.
/// </summary>
public readonly record struct HeardNoise(
    NoiseEvent Noise, GridPosition BelievedPosition, float PerceivedIntensity);

/// <summary>
/// Seul pont entre la verite du monde et la carte mentale des agents (specification section 9).
///
/// Il lit <see cref="WorldState"/> et n'ecrit que dans l'<see cref="AgentKnowledge"/> de l'agent
/// observateur. Aucun autre systeme n'a besoin de <see cref="WorldState"/> : c'est ce qui rend
/// impossible l'usage d'une information jamais observee.
/// </summary>
public sealed class PerceptionSystem(PerceptionSettings? settings = null)
{
    /// <summary>
    /// Intensite qui sature l'echelle du bareme de la section 18.1 : la distraction, le bruit le
    /// plus fort qu'une action puisse produire. Elle ne sert qu'a ramener un bruit entendu entre 0
    /// et 1 pour la carte de chaleur.
    /// </summary>
    private const float MaximumScaleIntensity = 10f;

    public PerceptionSettings Settings { get; } = settings ?? PerceptionSettings.Default;

    /// <summary>
    /// Cellules visibles depuis <paramref name="observer"/> : dans la portee, dans le cone de
    /// vision, et non masquees par un mur, une porte fermee ou un obstacle. La cellule de
    /// l'observateur est toujours visible.
    /// </summary>
    public List<GridPosition> ComputeVisibleCells(
        GridMap grid,
        GridPosition observer,
        Vector2 facing,
        int range,
        float fieldOfViewDegrees)
    {
        var visible = new List<GridPosition>();
        if (!grid.Contains(observer)) return visible;

        visible.Add(observer);

        // Au-dela de 360 degres le cone couvre tout : inutile de normaliser une orientation.
        bool panoramic = fieldOfViewDegrees >= 360f;
        var axis = facing.LengthSquared() > 0f ? Vector2.Normalize(facing) : new Vector2(0f, 1f);
        float minimumDot = MathF.Cos(float.DegreesToRadians(Math.Clamp(fieldOfViewDegrees, 0f, 360f) * 0.5f));

        for (int dy = -range; dy <= range; dy++)
        {
            for (int dx = -range; dx <= range; dx++)
            {
                if (dx == 0 && dy == 0) continue;

                if (dx * dx + dy * dy > range * range) continue;

                var cell = new GridPosition(observer.X + dx, observer.Y + dy);
                if (!grid.Contains(cell)) continue;

                if (!panoramic)
                {
                    var direction = Vector2.Normalize(new Vector2(dx, dy));
                    if (Vector2.Dot(direction, axis) < minimumDot) continue;
                }

                if (LineOfSight.IsVisible(grid, observer, cell)) visible.Add(cell);
            }
        }

        return visible;
    }

    /// <summary>
    /// Fait observer le monde au survivant et met a jour sa carte mentale : type de chaque cellule
    /// visible, entites et objets presents, le tout horodate (specification section 9.2).
    /// </summary>
    public PerceptionResult Observe(IPerceivingAgent agent, WorldState world, double? time = null)
    {
        double now = time ?? world.Time;

        var visible = ComputeVisibleCells(
            world.Grid, agent.Position, agent.Facing, agent.VisionRange, agent.FieldOfViewDegrees);

        var knowledge = agent.Knowledge;
        int discovered = 0;

        foreach (var cell in visible)
        {
            if (!knowledge.HasObserved(cell)) discovered++;
            knowledge.ObserveCell(cell, world.Grid.GetCell(cell), world.Grid.IsIndoor(cell), now);
        }

        var visibleSet = new HashSet<GridPosition>(visible);
        var seenEntities = new List<string>();
        var seenItems = new List<string>();

        foreach (var entity in world.Entities.Values)
        {
            if (entity.Id == agent.Id) continue;
            if (!visibleSet.Contains(entity.Position)) continue;

            knowledge.RememberEntity(entity.Id, entity.Kind, entity.Position, now);
            seenEntities.Add(entity.Id);

            // Apercevoir un zombie ne marque pas une case mais un voisinage : on evite la rue, pas
            // le pave. Le souvenir s'effacera tout seul (voir DangerMemory).
            if (entity.Kind == EntityKind.Zombie) knowledge.Danger.Mark(entity.Position, now);
        }

        foreach (var item in world.Items.Values)
        {
            if (!visibleSet.Contains(item.Position)) continue;

            knowledge.RememberItem(item.Id, item.ItemId, item.Position, now);
            seenItems.Add(item.Id);
        }

        // Les meubles poussables (livrable 11) : on les memorise comme les objets, et pour la meme
        // raison — un allie peut en avoir deplace un pendant qu'on regardait ailleurs.
        var seenHeavy = new List<string>();

        foreach (var heavy in world.Heavy.All)
        {
            if (!visibleSet.Contains(heavy.Position)) continue;

            knowledge.RememberHeavy(heavy.Id, heavy.Kind, heavy.Position, heavy.Mass, now);
            seenHeavy.Add(heavy.Id);
        }

        InvalidateVanishedEntities(knowledge, visibleSet, seenEntities, now);
        InvalidateVanishedItems(knowledge, visibleSet, seenItems, now);
        InvalidateVanishedHeavy(knowledge, visibleSet, seenHeavy, now);

        return new PerceptionResult(visible, discovered, seenEntities, seenItems);
    }

    /// <summary>
    /// Fait entendre a l'agent les bruits qui lui parviennent (specification section 18.3, etape 1).
    ///
    /// Ce qui entre dans sa memoire est une position <b>approximative</b>, d'autant plus fausse que
    /// le bruit est arrive attenue. Un agent qui n'a fait qu'entendre ne sait donc jamais ou se
    /// trouve exactement celui qui a fait le bruit : c'est le critere de validation du livrable 3.
    ///
    /// <paramref name="rng"/> porte l'erreur de localisation ; en passer un de graine connue rend
    /// le comportement reproductible.
    /// </summary>
    public IReadOnlyList<HeardNoise> Listen(
        IPerceivingAgent agent, WorldState world, Random? rng = null, double? time = null)
    {
        double now = time ?? world.Time;
        var heard = new List<HeardNoise>();

        foreach (var audible in world.Noises.Audible(agent.Position, now, world.Grid))
        {
            // On ne sursaute pas a ses propres pas.
            if (audible.Noise.SourceId is { } source && source == agent.Id) continue;

            var believed = world.Noises.Localize(audible.Noise, audible.PerceivedIntensity, rng);

            // La confiance dit « ce renseignement est frais », pas « ce bruit etait fort » : entendre
            // quelque chose ne laisse aucun doute sur le fait de l'avoir entendu. L'incertitude sur
            // l'endroit est deja portee par l'erreur de localisation, et l'interet relatif des bruits
            // par leur intensite percue.
            // Le type de bruit passe, l'emetteur non : on reconnait un fracas de vitre sans savoir
            // qui l'a brisee.
            if (agent.Knowledge.RememberNoise(
                    audible.Noise.Id, believed, audible.PerceivedIntensity, now, action: audible.Noise.Action))
            {
                heard.Add(new HeardNoise(audible.Noise, believed, audible.PerceivedIntensity));

                // Un bruit marque aussi la carte de chaleur, mais moins fort qu'une vue : on ne sait
                // ni ce qui l'a produit, ni exactement d'ou il venait.
                var settings = agent.Knowledge.Settings;
                float loudness = Math.Clamp(audible.PerceivedIntensity / MaximumScaleIntensity, 0f, 1f);

                agent.Knowledge.Danger.Mark(
                    believed, now, loudness * settings.DangerNoiseWeight, KnowledgeSource.Sound);
            }
        }

        return heard;
    }

    /// <summary>
    /// L'agent voit la case ou il croyait trouver quelqu'un et elle est vide : la croyance tombe.
    /// C'est la difference entre « la piece est vide » et « la piece n'a pas ete observee ».
    ///
    /// La case interrogee est la position <b>supposee</b> (<see cref="AgentKnowledge.PredictedPositionOf"/>),
    /// pas celle de la derniere observation. Sans cela, un survivant poursuivi qui se retourne
    /// verrait vide l'endroit ou le zombie se tenait, en conclurait qu'il n'y a plus de zombie, et
    /// repartirait tranquillement — alors qu'il vient precisement de confirmer qu'il avance.
    /// </summary>
    private static void InvalidateVanishedEntities(
        AgentKnowledge knowledge,
        HashSet<GridPosition> visible,
        List<string> seen,
        double now)
    {
        foreach (var belief in knowledge.KnownEntities.Values.ToList())
        {
            if (belief.Confidence <= 0f) continue;
            if (seen.Contains(belief.Id)) continue;
            if (!visible.Contains(knowledge.PredictedPositionOf(belief, now))) continue;

            knowledge.ForgetEntityAt(belief.Id, now);
        }
    }

    /// <summary>
    /// Meme regle pour les objets, avec une consequence differente : un objet ne bouge pas tout
    /// seul. S'il n'est plus la ou l'agent l'avait vu, c'est que quelqu'un l'a pris.
    ///
    /// L'invalidation est donc <b>differee</b> : rien ne previent qu'une boite de medicaments a
    /// change de main, et l'agent continue de planifier avec elle jusqu'a ce qu'il revienne sur la
    /// case et constate le vide. C'est exactement le declencheur « un objet attendu est absent » de
    /// la replanification — et c'est ce qui rend la carte mentale faillible plutot que magique.
    ///
    /// La question porte sur la position memorisee, sans extrapolation : un objet n'a pas de cap.
    /// </summary>
    private static void InvalidateVanishedItems(
        AgentKnowledge knowledge,
        HashSet<GridPosition> visible,
        List<string> seen,
        double now)
    {
        foreach (var belief in knowledge.KnownItems.Values.ToList())
        {
            if (belief.Confidence <= 0f) continue;
            if (seen.Contains(belief.Id)) continue;
            if (!visible.Contains(belief.Value)) continue;

            knowledge.ForgetItemAt(belief.Id, now);
        }
    }

    /// <summary>
    /// Meme regle pour les meubles : revenir sur la case ou l'on croyait laisser un rayonnage et ne
    /// plus l'y trouver, c'est apprendre que quelqu'un l'a pousse. Tant qu'on n'y revient pas, on
    /// continue de contourner un obstacle qui n'est plus la.
    /// </summary>
    private static void InvalidateVanishedHeavy(
        AgentKnowledge knowledge,
        HashSet<GridPosition> visible,
        List<string> seen,
        double now)
    {
        foreach (var belief in knowledge.KnownHeavy.Values.ToList())
        {
            if (belief.Confidence <= 0f) continue;
            if (seen.Contains(belief.Id)) continue;
            if (!visible.Contains(belief.Value)) continue;

            knowledge.ForgetHeavyAt(belief.Id, now);
        }
    }
}

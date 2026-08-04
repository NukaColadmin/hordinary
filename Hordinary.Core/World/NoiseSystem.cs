namespace Hordinary.World;

/// <summary>Un bruit tel qu'il parvient a un auditeur donne.</summary>
public readonly record struct AudibleNoise(NoiseEvent Noise, float PerceivedIntensity);

/// <summary>
/// Evenements sonores du monde (specification section 18) : emission, expiration, propagation.
///
/// C'est un systeme du monde, comme <see cref="PerceptionSystem"/> : il decrit ce qui est
/// physiquement audible. Ce qu'un agent en retient — une position <b>approximative</b> — est ecrit
/// dans sa memoire, jamais lu ici.
/// </summary>
public sealed class NoiseSystem(NoiseSettings? settings = null)
{
    private readonly List<NoiseEvent> _active = [];
    private int _nextId;

    public NoiseSettings Settings { get; } = settings ?? NoiseSettings.Default;

    /// <summary>Bruits encore en vie, du plus ancien au plus recent.</summary>
    public IReadOnlyList<NoiseEvent> Active => _active;

    // --- Emission -------------------------------------------------------------------------

    public NoiseEvent Emit(NoiseAction action, GridPosition position, double now, string? sourceId = null)
        => Emit(position, Settings.LevelOf(action), now, action, sourceId);

    /// <summary>
    /// Emet un bruit dont l'intensite ne vient pas du bareme mais de l'appelant — le cas d'une action
    /// de plan, qui porte son propre niveau. <paramref name="action"/> nomme quand meme l'evenement,
    /// pour que le journal et la memoire auditive des agents ne voient pas tout comme une
    /// distraction. Une intensite nulle ou negative n'emet rien.
    /// </summary>
    public NoiseEvent? EmitRaw(
        GridPosition position,
        float intensity,
        double now,
        string? sourceId = null,
        NoiseAction action = NoiseAction.Distraction)
        => intensity > 0f ? Emit(position, intensity, now, action, sourceId) : null;

    private NoiseEvent Emit(
        GridPosition position, float intensity, double now, NoiseAction action, string? sourceId)
    {
        var noise = new NoiseEvent($"noise-{++_nextId}", position, intensity, now, action, sourceId);
        _active.Add(noise);
        return noise;
    }

    /// <summary>Retire les bruits dont la duree de vie est ecoulee.</summary>
    public int Expire(double now)
        => _active.RemoveAll(noise => now - noise.CreatedAt > Settings.Lifetime);

    public void Clear() => _active.Clear();

    // --- Propagation ----------------------------------------------------------------------

    /// <summary>
    /// Intensite percue a une position (specification section 18.2) :
    /// <c>intensite - distance</c>, diminuee en plus par les cellules opaques traversees.
    /// Peut etre negative : le bruit ne parvient alors pas jusque-la.
    /// </summary>
    public float PerceivedIntensity(NoiseEvent noise, GridPosition listener, GridMap? map = null)
    {
        float distance = Distance(noise.Position, listener);
        float perceived = noise.Intensity - distance;

        if (map is null || Settings.ObstacleAttenuation <= 0f) return perceived;

        return perceived - CountOpaqueCells(map, noise.Position, listener) * Settings.ObstacleAttenuation;
    }

    public bool CanHear(NoiseEvent noise, GridPosition listener, double now, GridMap? map = null)
        => now - noise.CreatedAt <= Settings.Lifetime
        && PerceivedIntensity(noise, listener, map) >= Settings.HearingThreshold;

    /// <summary>Bruits audibles depuis une position, du plus fort au plus faible.</summary>
    public IEnumerable<AudibleNoise> Audible(GridPosition listener, double now, GridMap? map = null)
        => _active
            .Where(noise => now - noise.CreatedAt <= Settings.Lifetime)
            .Select(noise => new AudibleNoise(noise, PerceivedIntensity(noise, listener, map)))
            .Where(heard => heard.PerceivedIntensity >= Settings.HearingThreshold)
            .OrderByDescending(heard => heard.PerceivedIntensity);

    public AudibleNoise? Loudest(GridPosition listener, double now, GridMap? map = null)
    {
        foreach (var heard in Audible(listener, now, map)) return heard;
        return null;
    }

    // --- Localisation ---------------------------------------------------------------------

    /// <summary>
    /// Position <b>approximative</b> attribuee a un bruit par un auditeur. L'erreur croit avec ce
    /// que le bruit a perdu en chemin (distance et obstacles) : entendre n'est pas voir, et c'est
    /// precisement ce qui interdit a un zombie de connaitre la position exacte de sa source.
    /// </summary>
    public GridPosition Localize(NoiseEvent noise, float perceivedIntensity, Random? rng = null)
    {
        int radius = LocalisationRadius(noise, perceivedIntensity);
        if (radius <= 0) return noise.Position;

        rng ??= Random.Shared;
        return new GridPosition(
            noise.Position.X + rng.Next(-radius, radius + 1),
            noise.Position.Y + rng.Next(-radius, radius + 1));
    }

    /// <summary>Rayon d'incertitude, en cellules, sur l'origine d'un bruit.</summary>
    public int LocalisationRadius(NoiseEvent noise, float perceivedIntensity)
    {
        float lost = Math.Max(0f, noise.Intensity - perceivedIntensity);
        int radius = (int)MathF.Round(lost * Settings.LocalisationErrorFactor);
        return Math.Clamp(radius, 0, Settings.MaxLocalisationError);
    }

    // --- Utilitaires ----------------------------------------------------------------------

    private static float Distance(GridPosition a, GridPosition b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Cellules opaques strictement entre la source et l'auditeur.</summary>
    private static int CountOpaqueCells(GridMap map, GridPosition from, GridPosition to)
    {
        int count = 0;
        foreach (var cell in LineOfSight.Trace(from, to))
        {
            if (cell == to) break;
            if (map.GetCell(cell).BlocksSight()) count++;
        }

        return count;
    }
}

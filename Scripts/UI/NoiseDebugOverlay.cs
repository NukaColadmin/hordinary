using Godot;
using Hordinary.AI.Zombies;
using Hordinary.World;
using ZombieAgent = Hordinary.Agents.Zombie;

namespace Hordinary.UI;

/// <summary>
/// Affichage debug du livrable 3 : la portee des bruits en cours et ce que chaque zombie croit
/// savoir.
///
/// Le trait qui relie un zombie a sa destination est le plus parlant : en mode
/// <see cref="ZombieState.InvestigateNoise"/> il pointe la position <b>approximative</b> qu'il prete
/// au bruit, jamais la position reelle du survivant.
/// </summary>
public partial class NoiseDebugOverlay : Node2D
{
    private static readonly Color NoiseColor = new(1f, 0.85f, 0.35f);
    private static readonly Color BelievedNoiseColor = new(1f, 0.55f, 0.15f);
    private static readonly Color Hunting = new(0.95f, 0.20f, 0.22f);
    private static readonly Color Searching = new(0.95f, 0.55f, 0.20f);
    private static readonly Color Curious = new(0.90f, 0.78f, 0.25f);

    private NoiseSystem? _noises;
    private IReadOnlyList<ZombieAgent> _zombies = [];
    private int _tileSize = 16;
    private double _now;

    public void Configure(NoiseSystem noises, IReadOnlyList<ZombieAgent> zombies, int tileSize)
    {
        _noises = noises;
        _zombies = zombies;
        _tileSize = tileSize;
        QueueRedraw();
    }

    public void Refresh(double now)
    {
        _now = now;
        if (Visible) QueueRedraw();
    }

    public override void _Draw()
    {
        if (_noises is null) return;

        DrawNoises();
        DrawZombies();
    }

    /// <summary>
    /// Chaque bruit est un disque dont le rayon vaut la distance a laquelle il reste audible :
    /// <c>intensite - seuil</c>, avant attenuation par les murs.
    /// </summary>
    private void DrawNoises()
    {
        float threshold = _noises!.Settings.HearingThreshold;
        float lifetime = Math.Max(_noises.Settings.Lifetime, 0.001f);

        foreach (var noise in _noises.Active)
        {
            float age = (float)Math.Max(0d, _now - noise.CreatedAt);
            float remaining = Math.Clamp(1f - age / lifetime, 0f, 1f);
            float reach = Math.Max(0f, noise.Intensity - threshold) * _tileSize;

            DrawArc(CellCenter(noise.Position), reach, 0f, Mathf.Tau, 48,
                NoiseColor with { A = 0.55f * remaining }, 1.5f);
            DrawCircle(CellCenter(noise.Position), _tileSize * 0.18f,
                NoiseColor with { A = 0.85f * remaining });
        }
    }

    private void DrawZombies()
    {
        var font = ThemeDB.FallbackFont;

        foreach (var zombie in _zombies)
        {
            var from = CellCenter(zombie.Position);

            if (zombie.Destination is { } destination && zombie.State != ZombieState.Idle)
            {
                var color = ColorFor(zombie.State);
                DrawLine(from, CellCenter(destination), color with { A = 0.55f }, 2f);

                // La cible d'une enquete sonore est ce que le zombie croit, pas ou est le survivant.
                if (zombie.State == ZombieState.InvestigateNoise)
                {
                    DrawRect(CellRect(destination), BelievedNoiseColor with { A = 0.8f }, filled: false, width: 2f);
                }
            }

            DrawString(font, from + new Vector2(-_tileSize, -_tileSize * 0.55f),
                Label(zombie.State), HorizontalAlignment.Center, _tileSize * 2f, 12,
                ColorFor(zombie.State));
        }
    }

    private static string Label(ZombieState state) => state switch
    {
        ZombieState.Idle => "idle",
        ZombieState.Wander => "errance",
        ZombieState.InvestigateNoise => "bruit",
        ZombieState.Chase => "poursuite",
        ZombieState.Attack => "attaque",
        ZombieState.SearchLastKnownPosition => "fouille",
        _ => state.ToString(),
    };

    private static Color ColorFor(ZombieState state) => state switch
    {
        ZombieState.Chase or ZombieState.Attack => Hunting,
        ZombieState.InvestigateNoise => Curious,
        ZombieState.SearchLastKnownPosition => Searching,
        _ => new Color(0.6f, 0.7f, 0.55f),
    };

    private Rect2 CellRect(GridPosition cell)
        => new(cell.X * _tileSize, cell.Y * _tileSize, _tileSize, _tileSize);

    private Vector2 CellCenter(GridPosition cell)
        => new(cell.X * _tileSize + _tileSize * 0.5f, cell.Y * _tileSize + _tileSize * 0.5f);
}

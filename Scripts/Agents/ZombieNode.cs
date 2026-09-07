using Godot;
using Hordinary.AI.Zombies;
using ZombieAgent = Hordinary.Agents.Zombie;

namespace Hordinary.Agents;

/// <summary>
/// Representation visuelle d'un zombie. Comme <see cref="SurvivorNode"/>, ce noeud ne decide rien :
/// le corps est deplace par <see cref="World.EntityManager"/>, il se contente de le suivre.
/// La couleur trahit l'etat, ce qui rend la machine a etats lisible sans ouvrir l'overlay.
/// </summary>
public partial class ZombieNode : Node2D
{
    private static readonly Color Calm = new(0.42f, 0.55f, 0.32f);
    private static readonly Color Curious = new(0.90f, 0.78f, 0.25f);
    private static readonly Color Searching = new(0.95f, 0.55f, 0.20f);
    private static readonly Color Hunting = new(0.95f, 0.20f, 0.22f);

    private ZombieAgent? _zombie;
    private int _tileSize = 16;
    private ZombieState _drawnState = ZombieState.Idle;

    public void Configure(ZombieAgent zombie, int tileSize)
    {
        _zombie = zombie;
        _tileSize = tileSize;
        _drawnState = zombie.State;
        Position = ToPixels(zombie.Follower.Position);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_zombie is null) return;

        Position = ToPixels(_zombie.Follower.Position);

        // Le dessin ne change qu'aux transitions : inutile de le refaire a chaque frame.
        if (_drawnState == _zombie.State) return;

        _drawnState = _zombie.State;
        QueueRedraw();
    }

    public override void _Draw()
    {
        float radius = _tileSize * 0.30f;
        var color = ColorFor(_drawnState);

        DrawCircle(Vector2.Zero, radius + 2.4f, new Color(0f, 0f, 0f, 0.5f));
        DrawCircle(Vector2.Zero, radius, color);

        // Un liseré clair signale les zombies qui ont une piste a suivre.
        if (_drawnState is ZombieState.Idle or ZombieState.Wander) return;

        DrawArc(Vector2.Zero, radius + 5f, 0f, Mathf.Tau, 16, color with { A = 0.75f }, 2.4f);
    }

    private static Color ColorFor(ZombieState state) => state switch
    {
        ZombieState.Chase or ZombieState.Attack => Hunting,
        ZombieState.InvestigateNoise => Curious,
        ZombieState.SearchLastKnownPosition => Searching,
        _ => Calm,
    };

    private Vector2 ToPixels(System.Numerics.Vector2 cellPosition) => new(
        cellPosition.X * _tileSize + _tileSize * 0.5f,
        cellPosition.Y * _tileSize + _tileSize * 0.5f);
}

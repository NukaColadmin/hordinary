using Godot;
using Hordinary.AI.Navigation;
using Hordinary.World;

namespace Hordinary.Agents;

/// <summary>
/// Representation visuelle d'un survivant. Toute la logique vit dans le coeur : ce noeud ne fait que
/// lire la position que <see cref="PathFollower"/> a deja calculee et la convertir en pixels.
///
/// Depuis le livrable 7 il n'avance plus le suiveur lui-meme. Avec trois survivants, faire glisser
/// les corps depuis trois <c>_Process</c> independants reviendrait a laisser l'affichage cadencer la
/// simulation : c'est <see cref="SurvivorBand"/> qui le fait, une fois par frame, pour tout le monde.
/// </summary>
public partial class SurvivorNode : Node2D
{
    [Export] public Color BodyColor { get; set; } = new(0.31f, 0.78f, 1f);

    private SurvivorAgent _agent = null!;
    private int _tileSize = 16;
    private bool _selected;

    public string SurvivorName => _agent.Survivor.Name;

    public GridPosition Cell => _agent.Follower.CurrentCell;

    /// <summary>Le survivant que le joueur a sous les yeux : entoure d'un lisere clair.</summary>
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;

            _selected = value;
            QueueRedraw();
        }
    }

    public void Configure(SurvivorAgent agent, int tileSize)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _tileSize = tileSize;
        Name = agent.Survivor.Name;
        Position = ToPixels(agent.Follower.Position);
    }

    public override void _Process(double delta)
    {
        Position = ToPixels(_agent.Follower.Position);

        // Un survivant hors de combat s'eteint plutot que de disparaitre : la defaite complete est
        // l'affaire du livrable 8.
        Modulate = _agent.Survivor.Alive ? Colors.White : new Color(1f, 1f, 1f, 0.35f);
    }

    public override void _Draw()
    {
        float radius = _tileSize * 0.34f;

        DrawCircle(Vector2.Zero, radius + 1.5f, new Color(0f, 0f, 0f, 0.55f));
        DrawCircle(Vector2.Zero, radius, BodyColor);

        if (Selected) DrawArc(Vector2.Zero, radius + 3f, 0f, Mathf.Tau, 24, Colors.White, 1.5f);

        var font = ThemeDB.FallbackFont;
        DrawString(
            font,
            new Vector2(-_tileSize, -radius - 4f),
            SurvivorName,
            HorizontalAlignment.Center,
            _tileSize * 2f,
            8,
            Colors.White);
    }

    private Vector2 ToPixels(System.Numerics.Vector2 cellPosition) => new(
        cellPosition.X * _tileSize + _tileSize * 0.5f,
        cellPosition.Y * _tileSize + _tileSize * 0.5f);
}

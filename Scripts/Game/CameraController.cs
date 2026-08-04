using Godot;
using Hordinary.World;

// Godot expose un noeud 3D nomme GridMap : on desambigue en faveur de notre grille de jeu.
using GridMap = Hordinary.World.GridMap;

namespace Hordinary.Game;

/// <summary>Camera de debug : cadre la carte au demarrage, molette pour zoomer, clic milieu pour deplacer.</summary>
public partial class CameraController : Camera2D
{
    [Export] public float MinZoom { get; set; } = 0.4f;
    [Export] public float MaxZoom { get; set; } = 6f;
    [Export] public float ZoomStep { get; set; } = 1.15f;

    private bool _dragging;

    public override void _Ready() => MakeCurrent();

    /// <summary>Centre et zoome pour que toute la carte tienne a l'ecran.</summary>
    public void FrameMap(GridMap map, int tileSize)
    {
        var mapSize = new Vector2(map.Width * tileSize, map.Height * tileSize);
        var viewport = GetViewportRect().Size;

        GlobalPosition = new Vector2(
            (map.Origin.X * tileSize) + mapSize.X * 0.5f,
            (map.Origin.Y * tileSize) + mapSize.Y * 0.5f);

        float fit = Mathf.Min(viewport.X / mapSize.X, viewport.Y / mapSize.Y) * 0.92f;
        float zoom = Mathf.Clamp(fit, MinZoom, MaxZoom);
        Zoom = new Vector2(zoom, zoom);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                ApplyZoom(ZoomStep);
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                ApplyZoom(1f / ZoomStep);
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } middle:
                _dragging = middle.Pressed;
                break;

            case InputEventMouseMotion motion when _dragging:
                GlobalPosition -= motion.Relative / Zoom;
                break;
        }
    }

    private void ApplyZoom(float factor)
    {
        float zoom = Mathf.Clamp(Zoom.X * factor, MinZoom, MaxZoom);
        Zoom = new Vector2(zoom, zoom);
    }
}

using Godot;
using Hordinary.World;

// Godot expose un noeud 3D nomme GridMap : on desambigue en faveur de notre grille de jeu.
using GridMap = Hordinary.World.GridMap;

namespace Hordinary.Game;

/// <summary>Camera de debug : cadre la carte au demarrage, molette pour zoomer, clic milieu pour deplacer.</summary>
public partial class CameraController : Camera2D
{
    [Export] public float MinZoom { get; set; } = 0.2f;
    [Export] public float MaxZoom { get; set; } = 6f;
    [Export] public float ZoomStep { get; set; } = 1.15f;

    private bool _dragging;

    /// <summary>Le decor, en pixels. Vide tant que personne n'a borne la camera.</summary>
    private Rect2 _bounds;
    private bool _bounded;

    /// <summary>
    /// Ce que la camera suit, en pixels, ou <c>null</c> quand elle reste ou le joueur l'a laissee.
    ///
    /// C'est une fermeture et non un noeud : la camera n'a pas a savoir ce qu'est un survivant, ni a
    /// tenir une reference sur un corps qui peut mourir, rentrer au camp et etre libere sous elle.
    /// Celui qui la met en poursuite est aussi celui qui sait quand la relacher.
    /// </summary>
    private Func<Vector2>? _followed;

    /// <summary>Vrai quand la camera suit quelqu'un plutot que de rester ou on l'a posee.</summary>
    public bool IsFollowing => _followed is not null;

    public override void _Ready() => MakeCurrent();

    /// <summary>
    /// Met la camera en poursuite. Le zoom continue d'obeir au joueur : suivre quelqu'un, c'est
    /// rester centre sur lui, pas renoncer a regarder de plus loin.
    /// </summary>
    public void Follow(Func<Vector2> target)
    {
        _followed = target ?? throw new ArgumentNullException(nameof(target));
        Snap();
    }

    /// <summary>Relache la poursuite. La camera reste ou elle se trouve.</summary>
    public void Unfollow() => _followed = null;

    public override void _Process(double delta) => Snap();

    private void Snap()
    {
        if (_followed is not { } target) return;

        GlobalPosition = target();
        Clamp();
    }

    /// <summary>Centre et zoome pour que toute la carte tienne a l'ecran.</summary>
    public void FrameMap(GridMap map, int tileSize)
    {
        var mapSize = new Vector2(map.Width * tileSize, map.Height * tileSize);
        var viewport = GetViewportRect().Size;

        GlobalPosition = new Vector2(
            (map.Origin.X * tileSize) + mapSize.X * 0.5f,
            (map.Origin.Y * tileSize) + mapSize.Y * 0.5f);

        float fit = Mathf.Min(viewport.X / mapSize.X, viewport.Y / mapSize.Y) * 0.92f;
        Zoom = Vector2.One * Mathf.Clamp(fit, Floor(), MaxZoom);
        Clamp();
    }

    /// <summary>
    /// Borne le cadre au decor : la vue ne montre jamais rien au-dela de la derniere case.
    ///
    /// C'est une exigence du livrable 13, et elle n'a de sens que pour un quartier genere : celui-ci
    /// se termine par une ceinture de brouillard, epaisse de quatre a huit cases, qui existe
    /// precisement pour qu'il y ait quelque chose a montrer au bord. La carte dessinee, elle, n'a pas
    /// de ceinture — la borner reviendrait a interdire de la regarder en entier. D'ou l'appel
    /// explicite : on borne ce qui a ete concu pour l'etre.
    ///
    /// Le zoom en fait partie. Deborder ne demande pas de se deplacer : il suffit de reculer assez
    /// pour que l'ecran devienne plus large que le monde. Le plancher de zoom est donc celui qui
    /// <b>couvre</b> la fenetre (le plus grand des deux rapports), et non celui qui fait tenir la
    /// carte dedans — un grand quartier ne se voit pas d'un seul coup d'oeil, et c'est voulu.
    /// </summary>
    public void Bound(GridMap map, int tileSize)
    {
        ArgumentNullException.ThrowIfNull(map);

        _bounds = new Rect2(
            map.Origin.X * tileSize,
            map.Origin.Y * tileSize,
            map.Width * tileSize,
            map.Height * tileSize);

        _bounded = _bounds.Size is { X: > 0f, Y: > 0f };
        if (!_bounded) return;

        // Les limites natives de Godot font le travail au rendu ; le reste de cette classe les
        // double sur la position et le zoom, pour que le deplacement n'accumule pas de course morte
        // hors du decor.
        LimitLeft = Mathf.FloorToInt(_bounds.Position.X);
        LimitTop = Mathf.FloorToInt(_bounds.Position.Y);
        LimitRight = Mathf.CeilToInt(_bounds.End.X);
        LimitBottom = Mathf.CeilToInt(_bounds.End.Y);
        LimitSmoothed = false;

        Zoom = Vector2.One * Mathf.Clamp(Zoom.X, Floor(), MaxZoom);
        Clamp();
    }

    /// <summary>Cadre un quartier genere : borne d'abord, centre ensuite sur le point de depose.</summary>
    public void FrameDistrict(GridMap map, int tileSize, GridPosition focus)
    {
        Bound(map, tileSize);

        Zoom = Vector2.One * Mathf.Clamp(1f, Floor(), MaxZoom);
        GlobalPosition = new Vector2((focus.X + 0.5f) * tileSize, (focus.Y + 0.5f) * tileSize);
        Clamp();
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
                // Deplacer la vue a la main, c'est reprendre la main : on ne se bat pas contre une
                // camera qui revient sur un survivant a chaque image.
                Unfollow();
                GlobalPosition -= motion.Relative / Zoom;
                Clamp();
                break;
        }
    }

    private void ApplyZoom(float factor)
    {
        Zoom = Vector2.One * Mathf.Clamp(Zoom.X * factor, Floor(), MaxZoom);
        Clamp();
    }

    /// <summary>
    /// Le zoom le plus large autorise. Sans bornage c'est le reglage du joueur ; avec, c'est celui
    /// qui couvre exactement la fenetre — en deca, on verrait du vide au-dela du decor.
    /// </summary>
    private float Floor()
    {
        if (!_bounded) return MinZoom;

        var viewport = GetViewportRect().Size;
        return Mathf.Max(MinZoom, Mathf.Max(viewport.X / _bounds.Size.X, viewport.Y / _bounds.Size.Y));
    }

    /// <summary>Ramene le centre de la camera pour que la demi-fenetre reste dans le decor.</summary>
    private void Clamp()
    {
        if (!_bounded) return;

        var half = GetViewportRect().Size / (2f * Zoom.X);

        GlobalPosition = new Vector2(
            Axis(GlobalPosition.X, _bounds.Position.X, _bounds.End.X, half.X),
            Axis(GlobalPosition.Y, _bounds.Position.Y, _bounds.End.Y, half.Y));
    }

    /// <summary>
    /// Borne un axe entre les deux positions extremes qui ne montrent pas de vide.
    ///
    /// Au plancher de zoom, la demi-fenetre vaut <b>exactement</b> le demi-decor — en arithmetique
    /// exacte. En virgule flottante elle le depasse d'un millieme de pixel et les deux bornes se
    /// croisent ; <c>Mathf.Clamp</c> leve alors plutot que de choisir. Le cas etait theorique tant
    /// qu'un quartier debordait largement de l'ecran, il est devenu courant depuis que les quartiers
    /// generes tiennent presque dedans.
    ///
    /// Quand les bornes se croisent, il n'existe qu'une position acceptable, le centre du decor :
    /// c'est elle qu'on rend, plutot que d'elargir la tolerance d'un epsilon arbitraire qui laisserait
    /// passer une fraction de pixel de vide.
    /// </summary>
    private static float Axis(float value, float low, float high, float half)
    {
        float min = low + half;
        float max = high - half;

        return min > max ? (low + high) * 0.5f : Mathf.Clamp(value, min, max);
    }
}

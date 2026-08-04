using System.Numerics;
using Hordinary.World;

namespace Hordinary.AI.Navigation;

/// <summary>
/// Fait avancer une position continue le long d'un chemin de cellules.
/// Volontairement sans dependance au moteur : la couche Godot se contente de convertir
/// <see cref="Position"/> (en unites de cellule) en pixels.
/// </summary>
public sealed class PathFollower
{
    private IReadOnlyList<GridPosition> _path = [];
    private bool _arrivalRaised = true;

    /// <summary>Vitesse en cellules par seconde.</summary>
    public float Speed { get; set; } = 3.5f;

    public IReadOnlyList<GridPosition> Path => _path;

    /// <summary>Index de la prochaine cellule a atteindre dans <see cref="Path"/>.</summary>
    public int NextIndex { get; private set; }

    /// <summary>Position continue, exprimee en cellules (un entier = centre de la cellule).</summary>
    public Vector2 Position { get; private set; }

    /// <summary>
    /// Direction du dernier deplacement, conservee a l'arret. Elle sert d'axe au champ de vision
    /// du livrable 2 : un survivant regarde ou il va.
    /// </summary>
    public Vector2 Facing { get; private set; } = new(0f, 1f);

    public GridPosition CurrentCell => new((int)MathF.Round(Position.X), (int)MathF.Round(Position.Y));

    public GridPosition? NextCell => NextIndex < _path.Count ? _path[NextIndex] : null;

    public GridPosition? Destination => _path.Count > 0 ? _path[^1] : null;

    public bool HasPath => _path.Count > 0;

    public bool IsFinished => NextIndex >= _path.Count;

    /// <summary>Emis quand la prochaine cellule est devenue infranchissable. Point d'accroche du livrable 6.</summary>
    public event Action<GridPosition>? Blocked;

    public event Action<GridPosition>? Arrived;

    /// <summary>Place l'agent sur une cellule sans animation.</summary>
    public void Teleport(GridPosition cell)
    {
        Position = new Vector2(cell.X, cell.Y);
        Clear();
    }

    /// <summary>
    /// Definit un nouveau chemin. La position courante n'est pas replacee : l'agent rejoint
    /// naturellement la premiere cellule suivante depuis la ou il se trouve.
    /// </summary>
    public void SetPath(IReadOnlyList<GridPosition>? path)
    {
        _path = path ?? [];
        // La premiere cellule est celle sur laquelle l'agent se trouve deja.
        NextIndex = _path.Count > 1 ? 1 : _path.Count;
        _arrivalRaised = _path.Count == 0;
    }

    public void Clear()
    {
        _path = [];
        NextIndex = 0;
        _arrivalRaised = true;
    }

    /// <summary>
    /// Avance de <paramref name="delta"/> secondes. <paramref name="isTraversable"/> permet de
    /// verifier a chaque pas que la cellule visee est toujours praticable ; si elle ne l'est plus,
    /// le chemin est abandonne et <see cref="Blocked"/> est emis.
    /// </summary>
    public void Advance(float delta, Func<GridPosition, bool>? isTraversable = null)
    {
        if (delta <= 0f || IsFinished)
        {
            RaiseArrivalOnce();
            return;
        }

        float remaining = Speed * delta;

        while (remaining > 0f && NextIndex < _path.Count)
        {
            var target = _path[NextIndex];

            if (isTraversable is not null && !isTraversable(target))
            {
                Clear();
                Blocked?.Invoke(target);
                return;
            }

            var delta2 = new Vector2(target.X, target.Y) - Position;
            float distance = delta2.Length();

            if (distance > 0f) Facing = delta2 / distance;

            if (distance <= remaining)
            {
                Position = new Vector2(target.X, target.Y);
                remaining -= distance;
                NextIndex++;
            }
            else
            {
                Position += delta2 / distance * remaining;
                remaining = 0f;
            }
        }

        RaiseArrivalOnce();
    }

    private void RaiseArrivalOnce()
    {
        if (_arrivalRaised || !IsFinished) return;
        _arrivalRaised = true;
        Arrived?.Invoke(CurrentCell);
    }
}
